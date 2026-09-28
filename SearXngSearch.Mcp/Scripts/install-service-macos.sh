#!/usr/bin/env bash
# ============================================================
# SearXngSearchMcpServer macOS launchd(LaunchAgent) 설치 스크립트
#
# 사용법 (macOS, 관리자 권한 불필요 — 현재 사용자 에이전트):
#   1) 먼저 publish:
#        dotnet publish SearXngSearch.Mcp -c Release -r osx-x64 --self-contained false
#        # Apple Silicon(M1/M2/...)이면:
#        dotnet publish SearXngSearch.Mcp -c Release -r osx-arm64 --self-contained false
#   2) 서비스 설치 (publish 결과물을 설치 디렉터리로 복사 후 등록 + 시작):
#        bash SearXngSearch.Mcp/Scripts/install-service-macos.sh
#        # 또는 publish 폴더에서 (스크립트가 publish 결과물에 포함됨):
#        cd SearXngSearch.Mcp/bin/Release/net10.0/osx-x64/publish
#        bash Scripts/install-service-macos.sh
#   3) 서비스 제거:
#        bash Scripts/install-service-macos.sh uninstall
#        bash Scripts/install-service-macos.sh uninstall --remove-files   # 설치 파일까지 제거
#
# 옵션:
#   --searxng-url http://<searxng-host>:8080   # appsettings.json 수정 없이 SearXNG 주소 지정
#   --urls http://localhost:8612              # 바인딩 URL 지정
#   --urls http://0.0.0.0:8612                # 모든 인터페이스 (LAN/외부 접근, 방화벽 규칙 필요)
#   --publish-dir /path/to/publish            # publish 출력 위치 (기본: bin/Release/net10.0/{osx-x64|osx-arm64}/publish)
#
# 서비스는 프로젝트 폴더가 아닌 전용 설치 디렉터리(/usr/local/searxng-search-mcp)에서
# 실행되므로, 서비스 실행 중에도 dotnet publish/build가 정상 동작합니다.
# ============================================================
set -euo pipefail

LABEL="com.searxng.mcp"
INSTALL_DIR="/usr/local/searxng-search-mcp"
LOG_DIR="$HOME/Library/Logs/searxng-search-mcp"
PLIST_PATH="$HOME/Library/LaunchAgents/${LABEL}.plist"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# 스크립트는 SearXngSearch.Mcp/Scripts/ 에 위치 (publish 시 publish/Scripts/ 로 복사됨)
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

SEARXNG_BASE_URL=""
BIND_URLS=""
PUBLISH_DIR=""
REMOVE_FILES=0
UNINSTALL=0

# --- 인자 파싱 (uninstall도 플래그로만 처리 — 모든 인자 파싱 후 실행, --remove-files 순서 독립) ---
while [[ $# -gt 0 ]]; do
    case "$1" in
        uninstall)
            UNINSTALL=1; shift ;;
        --searxng-url)
            SEARXNG_BASE_URL="$2"; shift 2 ;;
        --urls)
            BIND_URLS="$2"; shift 2 ;;
        --publish-dir)
            PUBLISH_DIR="$2"; shift 2 ;;
        --remove-files)
            REMOVE_FILES=1; shift ;;
        *)
            echo "오류: 알 수 없는 인자: $1" >&2
            exit 1
            ;;
    esac
done

# --- uninstall (인자 파싱 완료 후 실행) ---
if [[ "$UNINSTALL" -eq 1 ]]; then
    echo "==> LaunchAgent '$LABEL' 제거 중..."
    launchctl bootout "gui/$(id -u)/${LABEL}" 2>/dev/null || true
    rm -f "$PLIST_PATH"
    if [[ "$REMOVE_FILES" -eq 1 ]]; then
        rm -rf "$INSTALL_DIR"
        echo "==> 설치 파일 제거: $INSTALL_DIR"
    else
        echo "==> 완료. (실행 파일은 $INSTALL_DIR 에 남아있음, 제거하려면 --remove-files)"
    fi
    exit 0
fi

# --- publish 출력 위치 결정 (미지정 시) ---
# - 스크립트가 publish 폴더의 Scripts/ 안에 있으면 (SearXngSearch.Mcp 실행 파일이 부모 폴더에 있는 경우) 부모 폴더를 사용
# - 그렇지 않으면(소스 리포지토리에서 실행) 프로젝트의 기본 publish 경로를 사용
#   (osx-x64 / osx-arm64 중 존재하는 쪽을 자동 감지)
if [[ -z "$PUBLISH_DIR" ]]; then
    if [[ -f "$PROJECT_DIR/SearXngSearch.Mcp" ]]; then
        PUBLISH_DIR="$PROJECT_DIR"
    else
        if [[ -d "$PROJECT_DIR/bin/Release/net10.0/osx-x64/publish" ]]; then
            PUBLISH_DIR="$PROJECT_DIR/bin/Release/net10.0/osx-x64/publish"
        elif [[ -d "$PROJECT_DIR/bin/Release/net10.0/osx-arm64/publish" ]]; then
            PUBLISH_DIR="$PROJECT_DIR/bin/Release/net10.0/osx-arm64/publish"
        else
            echo "오류: publish 결과물을 찾을 수 없습니다." >&2
            echo "먼저 실행하세요: dotnet publish SearXngSearch.Mcp -c Release -r osx-x64 --self-contained false" >&2
            echo "  (Apple Silicon이면 -r osx-arm64)" >&2
            exit 1
        fi
    fi
fi

# --- 0) publish 결과물 확인 (이 스크립트는 publish하지 않습니다 — 먼저 dotnet publish 실행 필요) ---
if [ ! -f "$PUBLISH_DIR/SearXngSearch.Mcp" ]; then
    echo "오류: publish 결과물을 찾을 수 없습니다: $PUBLISH_DIR/SearXngSearch.Mcp" >&2
    echo "먼저 실행하세요: dotnet publish SearXngSearch.Mcp -c Release -r osx-x64 --self-contained false" >&2
    echo "  (Apple Silicon이면 -r osx-arm64)" >&2
    exit 1
fi

# --- 1) 기존 에이전트 중지 (재설치를 위해, 설치 디렉터리 파일 잠금 해제) ---
if [ -f "$PLIST_PATH" ]; then
    echo "==> 기존 LaunchAgent 중지 중..."
    launchctl bootout "gui/$(id -u)/${LABEL}" 2>/dev/null || true
fi

# --- 2) publish 결과물 → 설치 디렉터리 복사 ---
echo "==> $PUBLISH_DIR → $INSTALL_DIR 복사 중..."
mkdir -p "$INSTALL_DIR"
rm -rf "$INSTALL_DIR"/*
cp -r "$PUBLISH_DIR"/* "$INSTALL_DIR"/
chmod +x "$INSTALL_DIR/SearXngSearch.Mcp"

# --- 3) 로그 디렉터리 생성 ---
mkdir -p "$LOG_DIR"

# --- 4) plist 생성 (설치 시 설정은 EnvironmentVariables로 주입) ---
echo "==> LaunchAgent plist 생성 중: $PLIST_PATH"
mkdir -p "$(dirname "$PLIST_PATH")"

# Kestrel 기본값과 일치 (appsettings.json에는 Urls 키가 없음)
DEFAULT_BIND_URL="http://localhost:5000"
DEFAULT_SEARXNG_URL="http://localhost:8080"
BIND_URL="${BIND_URLS:-$DEFAULT_BIND_URL}"
SEARXNG_URL="${SEARXNG_BASE_URL:-$DEFAULT_SEARXNG_URL}"

sed -e "s|__INSTALL_DIR__|$INSTALL_DIR|g" \
    -e "s|__ASPNETCORE_URLS__|$BIND_URL|g" \
    -e "s|__SEARXNG_BASEURL__|$SEARXNG_URL|g" \
    -e "s|__LOG_DIR__|$LOG_DIR|g" \
    "$SCRIPT_DIR/searxng-search-mcp.plist" > "$PLIST_PATH"

if [[ -n "$BIND_URLS" || -n "$SEARXNG_BASE_URL" ]]; then
    echo "==> 설치 시 설정: ASPNETCORE_URLS=$BIND_URL SEARXNG__BASEURL=$SEARXNG_URL"
fi

# --- 5) 시작 + 로그인 시 자동 시작 ---
echo "==> LaunchAgent 등록/시작 중..."
launchctl bootstrap "gui/$(id -u)" "$PLIST_PATH"
sleep 3
launchctl print "gui/$(id -u)/${LABEL}" | head -5 || true

# --- 6) 헬스 체크 (로컬 확인용 — 0.0.0.0/바인딩 주소도 localhost로 확인) ---
HEALTH_URL="http://localhost:5000"
# 사용자에게 안내할 실제 접근 엔드포인트 (0.0.0.0 바인딩 시 LAN IP로 표시)
DISPLAY_URL="http://localhost:5000"
if [[ -n "$BIND_URLS" ]]; then
    host="${BIND_URLS#*://}"; host="${host%%:*}"
    port="${BIND_URLS##*:}"
    if [[ "$host" == "0.0.0.0" || "$host" == "+" || "$host" == "[::]" ]]; then
        # 모든 인터페이스 바인딩: 헬스 체크는 localhost, 안내는 LAN IP
        HEALTH_URL="http://localhost:${port}"
        LAN_IP="$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null || true)"
        DISPLAY_URL="http://${LAN_IP:-localhost}:${port}"
    else
        HEALTH_URL="http://${host}:${port}"
        DISPLAY_URL="$HEALTH_URL"
    fi
fi

echo "==> 헬스 체크: $HEALTH_URL/health"
if curl -sf "$HEALTH_URL/health"; then
    echo ""
    echo "==> 정상 동작 확인!"
else
    echo "오류: 헬스 체크 실패. 'tail -50 $LOG_DIR/searxng-search-mcp.err.log' 로 로그를 확인하세요." >&2
    exit 1
fi

echo ""
echo "완료! MCP 엔드포인트: $DISPLAY_URL/mcp"
echo "관리 명령:"
echo "  launchctl bootout gui/$(id -u)/${LABEL}      # 중지"
echo "  launchctl bootstrap gui/$(id -u) $PLIST_PATH  # 시작"
echo "  tail -f $LOG_DIR/searxng-search-mcp.err.log          # 실시간 로그"
