#!/usr/bin/env bash
# ============================================================
# SearXngSearchMcpServer Ubuntu/Debian systemd 서비스 설치 스크립트
#
# 사용법 (Ubuntu/Debian, sudo 권한 필요):
#   1) 먼저 publish:
#        dotnet publish SearXngSearch.Mcp -c Release -r linux-x64 --self-contained false
#   2) 서비스 설치 (publish 결과물을 설치 디렉터리로 복사 후 등록 + 시작):
#        sudo bash SearXngSearch.Mcp/Scripts/install-service.sh
#        # 또는 publish 폴더에서 (스크립트가 publish 결과물에 포함됨):
#        cd SearXngSearch.Mcp/bin/Release/net10.0/linux-x64/publish
#        sudo bash Scripts/install-service.sh
#   3) 서비스 제거:
#        sudo bash Scripts/install-service.sh uninstall
#        sudo bash Scripts/install-service.sh uninstall --remove-files   # 설치 파일까지 제거
#
# 옵션:
#   --searxng-url http://<searxng-host>:8080   # appsettings.json 수정 없이 SearXNG 주소 지정
#   --urls http://localhost:8612              # 바인딩 URL 지정
#   --urls http://0.0.0.0:8612                # 모든 인터페이스 (LAN/외부 접근, 방화벽 규칙 필요)
#   --publish-dir /path/to/publish            # publish 출력 위치 (기본: bin/Release/net10.0/linux-x64/publish)
#
# 서비스는 프로젝트 폴더가 아닌 전용 설치 디렉터리(/opt/searxng-search-mcp)에서 실행되므로,
# 서비스 실행 중에도 dotnet publish/build가 정상 동작합니다.
# ============================================================
set -euo pipefail

SERVICE_NAME="searxng-search-mcp"
INSTALL_DIR="/opt/searxng-search-mcp"
SERVICE_USER="searxng-search-mcp"
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
    echo "==> 서비스 '$SERVICE_NAME' 제거 중..."
    systemctl disable --now "$SERVICE_NAME" 2>/dev/null || true
    rm -f "/etc/systemd/system/${SERVICE_NAME}.service"
    systemctl daemon-reload
    userdel "$SERVICE_USER" 2>/dev/null || true
    groupdel "$SERVICE_USER" 2>/dev/null || true
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
if [[ -z "$PUBLISH_DIR" ]]; then
    if [[ -f "$PROJECT_DIR/SearXngSearch.Mcp" ]]; then
        PUBLISH_DIR="$PROJECT_DIR"
    else
        PUBLISH_DIR="$PROJECT_DIR/bin/Release/net10.0/linux-x64/publish"
    fi
fi

# --- 루트 확인 ---
if [[ $EUID -ne 0 ]]; then
    echo "오류: sudo 권한으로 실행해야 합니다." >&2
    exit 1
fi

# --- 0) publish 결과물 확인 (이 스크립트는 publish하지 않습니다 — 먼저 dotnet publish 실행 필요) ---
if [ ! -f "$PUBLISH_DIR/SearXngSearch.Mcp" ]; then
    echo "오류: publish 결과물을 찾을 수 없습니다: $PUBLISH_DIR/SearXngSearch.Mcp" >&2
    echo "먼저 실행하세요: dotnet publish SearXngSearch.Mcp -c Release -r linux-x64 --self-contained false" >&2
    exit 1
fi

# --- 1) 기존 서비스 중지 (재설치를 위해, 설치 디렉터리 파일 잠금 해제) ---
if [ -f "/etc/systemd/system/${SERVICE_NAME}.service" ]; then
    echo "==> 기존 서비스 중지 중..."
    systemctl stop "$SERVICE_NAME" 2>/dev/null || true
fi

# --- 2) publish 결과물 → 설치 디렉터리 복사 ---
echo "==> $PUBLISH_DIR → $INSTALL_DIR 복사 중..."
mkdir -p "$INSTALL_DIR"
rm -rf "$INSTALL_DIR"/*
cp -r "$PUBLISH_DIR"/* "$INSTALL_DIR"/

# 실행 권한 보장 (Windows에서 tar로 압축/전송 시 +x 비트가 유실될 수 있음 → 203/EXEC 방지)
chmod +x "$INSTALL_DIR/SearXngSearch.Mcp"

# --- 3) 전용 사용자 생성 ---
if ! id "$SERVICE_USER" &>/dev/null; then
    echo "==> 전용 사용자 '$SERVICE_USER' 생성 중..."
    # -U: 사용자와 같은 이름의 그룹도 함께 생성 (유닛 파일의 Group=searxng-search-mcp에 필요)
    useradd --system -U --home-dir "$INSTALL_DIR" --shell /usr/sbin/nologin "$SERVICE_USER"
fi
chown -R "$SERVICE_USER:$SERVICE_USER" "$INSTALL_DIR"

# --- 4) systemd 유닛 설치 (설치 시 설정은 ExecStart 인자로 주입) ---
echo "==> systemd 유닛 설치 중: /etc/systemd/system/${SERVICE_NAME}.service"
cp "$SCRIPT_DIR/searxng-search-mcp.service" "/etc/systemd/system/${SERVICE_NAME}.service"

EXTRA_ARGS=""
if [[ -n "$BIND_URLS" ]]; then EXTRA_ARGS+=" --urls $BIND_URLS"; fi
if [[ -n "$SEARXNG_BASE_URL" ]]; then EXTRA_ARGS+=" --SearXng:BaseUrl $SEARXNG_BASE_URL"; fi
if [[ -n "$EXTRA_ARGS" ]]; then
    sed -i "s|^ExecStart=.*|ExecStart=${INSTALL_DIR}/SearXngSearch.Mcp${EXTRA_ARGS}|" "/etc/systemd/system/${SERVICE_NAME}.service"
    echo "==> 설치 시 설정:$EXTRA_ARGS"
fi
systemctl daemon-reload

# --- 5) 시작 + 부팅 시 자동 시작 ---
echo "==> 서비스 시작 중..."
systemctl enable --now "$SERVICE_NAME"
sleep 3
systemctl status "$SERVICE_NAME" --no-pager || true

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
        LAN_IP="$(hostname -I 2>/dev/null | tr ' ' '\n' | grep -v '^127\.' | grep -v '^169\.254\.' | head -1 || true)"
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
    echo "오류: 헬스 체크 실패. 'journalctl -u $SERVICE_NAME -n 50' 로 로그를 확인하세요." >&2
    exit 1
fi

echo ""
echo "완료! MCP 엔드포인트: $DISPLAY_URL/mcp"
echo "관리 명령:"
echo "  systemctl status $SERVICE_NAME"
echo "  systemctl restart $SERVICE_NAME"
echo "  journalctl -u $SERVICE_NAME -f"
