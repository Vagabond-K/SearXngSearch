# SearXNG Search (.NET)

> 🤖 **이 프로젝트는 GitHub Copilot(AI 코딩 어시스턴트)이 생성했습니다.**
> 요구사항 정의부터 구현, 빌드/테스트, Windows·Ubuntu·macOS 서비스 등록 스크립트, Dockerfile, 문서화까지 Copilot이 수행했습니다.
>
> - **사용한 AI 모델**: `Qwen/Qwen3.8-27B-FP8` (vLLM으로 서빙)

[SearXNG](https://docs.searxng.org/) 메타 검색 엔진의 JSON API를 사용하는 .NET 10 솔루션입니다. 세 가지 형태로 제공합니다:

1. **일반 .NET 라이브러리** — `SearXngClient`로 직접 SearXNG 검색 호출 (MCP/AI 프레임워크 무의존)
2. **AI 에이전트 라이브러리** — [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)(MAF) 등 AI 프레임워크에 `AITool`로 인 프로세스 연결
3. **MCP 서버** — [Model Context Protocol(MCP)](https://modelcontextprotocol.io/) 서버로 검색 도구를 노출 (VS Code, Claude Desktop 등)

- **MCP SDK**: [ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore) 2.2.0 (공식 C# SDK)
- **전송 방식**: Streamable HTTP (stateless 모드) + stdio
- **엔드포인트**: `http://localhost:5000/mcp` (HTTP 모드)

## 📁 프로젝트 구조

```text
SearXngMcp/                          # (워크스페이스 폴더명 — 솔루션은 SearXngSearch)
├── SearXngSearch.slnx               # 솔루션 파일 (.slnx)
├── Dockerfile                       # Docker 이미지 빌드
├── docker-compose.yml               # SearXNG + MCP 서버 원클릭 실행
├── docker/                          # Docker용 SearXNG 설정 (JSON API 활성화)
├── SearXngSearch/                   # ★ 코어 라이브러리 (클래스 라이브러리)
│   ├── SearXngSearch.csproj         # net10.0
│   ├── Options/                     # SearXngOptions
│   ├── Models/                      # SearXNG 응답 모델
│   └── Services/                    # SearXngClient, 포맷터, HTML 파서, 레이트 리미터
├── SearXngSearch.Mcp/               # ★ MCP 서버 (ASP.NET Core 웹 앱)
│   ├── SearXngSearch.Mcp.csproj     # net10.0, AssemblyName=SearXngSearch.Mcp
│   ├── Program.cs                   # stdio + HTTP 모드, Windows 서비스
│   ├── appsettings.json
│   ├── Tools/                       # MCP 도구 ([McpServerToolType])
│   └── Scripts/                     # 서비스 설치 스크립트 (Windows/Linux/macOS)
├── SearXngSearch.Ai/                # ★ AI 통합 라이브러리 (MAF/MEAI용 AITool)
│   ├── SearXngSearch.Ai.csproj
│   ├── SearXngAiTools.cs            # SearXngClient를 AITool로 감싼 5개 메서드
│   └── SearXngAiToolsExtensions.cs  # AsSearXngTools() / GetSearXngTools()
├── SearXngSearch.Tests/             # xUnit 테스트 (단위 + 통합, SearXNG 스텁 기반)
│   ├── Unit/                        # 레이트 리미터, 포맷터, HTML 파서, SearXngClient
│   ├── Integration/                 # /health, Bearer 인증, MCP 프로토콜 (raw JSON-RPC)
│   └── TestHelpers/                 # FakeSearXngHandler, TestServerFactory, McpTestClient
└── Samples/
    ├── MicrosoftAgentFrameworkSample/  # MAF 에이전트 + OpenAI + SearXNG 도구
    └── SimpleSearchSample/             # 일반 콘솔 검색 샘플
```

**의존 관계**: `SearXngSearch.Mcp` → `SearXngSearch`, `SearXngSearch.Ai` → `SearXngSearch`. 코어 라이브러리는 MCP/AI 프레임워크에 의존하지 않습니다.

## 🛠️ 제공되는 도구 (Tools)

MCP 서버와 AI 라이브러리 모두 동일한 5개 도구를 제공합니다.

| 도구명 | 설명 | 주요 파라미터 |
|---|---|---|
| `web_search` | 일반 웹 검색 (최상위 결과 강조 포함) | `query`, `language`, `categories`, `timeRange`, `maxResults`, `page`, `outputFormat`(text/json) |
| `news_search` | 뉴스 기사 검색 (categories=news) | `query`, `language`, `timeRange`(기본 week), `maxResults` |
| `get_instance_info` | 인스턴스 정보 (버전, 카테고리/엔진 목록) | - |
| `get_search_suggestions` | 검색어 자동완성 후보 | `query` |
| `check_searxng_status` | SearXNG 인스턴스 연결 상태 확인 | - |

## ✅ 사전 요구 사항

1. [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet) 이상 (Docker로 실행하는 경우 불필요)
2. JSON API가 활성화된 SearXNG 인스턴스 (아래 [Docker로 실행](#docker로-실행) 섹션에서 SearXNG도 함께 띄울 수 있음)

### SearXNG 인스턴스 준비

Docker로 빠르게 실행합니다. **JSON 형식을 활성화**한 설정 파일(`settings.yml`)을 먼저 만들어야 합니다 (기본값은 html만 허용):

```bash
# 1) 설정 파일 준비 (JSON API 활성화)
mkdir searxng
cat > searxng/settings.yml << 'EOF'
# 필수: 이 키가 없으면 이 파일이 기본 설정을 "전체 대체"해
# SearXNG가 필수 키 부재로 에러(모든 요청 500) 발생합니다.
use_default_settings: true

search:
  formats:
    - html
    - json
EOF

# 2) Docker 실행
docker run -d --name searxng -p 8080:8080 -v ./searxng:/etc/searxng searxng/searxng:latest
```

> - 설정 파일 없이 실행하면 SearXNG가 기본 설정(html만)으로 동작해 JSON API가 비활성화됩니다.
> - 공개 인스턴스(예: `https://searx.be`)도 사용할 수 있지만, 대부분 JSON API를 비활성화해 두거나 레이트 리미트가 적용됩니다. 안정적인 사용을 위해 자체 인스턴스 운영을 권장합니다.

## 📚 코어 라이브러리 (SearXngSearch)

MCP/AI 프레임워크에 의존하지 않는 순수 클래스 라이브러리입니다. `SearXngClient`를 직접 사용해 SearXNG를 검색에 활용할 수 있습니다.

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using SearXngSearch.Options;
using SearXngSearch.Services;

var services = new ServiceCollection();
services.AddMemoryCache();
services.Configure<SearXngOptions>(o => o.BaseUrl = "http://localhost:8080");
services.AddSearXngClient();          // IHttpClientFactory + SearXngClient 등록
services.AddSingleton<SearXngClient>();

using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<SearXngClient>();

var response = await client.SearchAsync(new SearchRequest { Query = ".NET 10", MaxResults = 5 });
// response.Results: 제목/URL/요약/엔진/점수/날짜
```

`SearchResultFormatter.ToText(response, query, maxResults)`로 텍스트로 포맷할 수도 있습니다.

## 🤖 AI 에이전트 라이브러리 (SearXngSearch.Ai)

[SearXngClient](#-코어-라이브러리-searxngsearch)를 [Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI)의 `AITool`로 감쌉니다. [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)(MAF), Semantic Kernel 등 `AITool`/`IChatClient`를 사용하는 프레임워크에 인 프로세스 도구로 연결할 수 있습니다.

```csharp
using Microsoft.Extensions.AI;
using SearXngSearch.Ai;

// DI 컨테이너에서 AITool[] 획득
AITool[] tools = provider.GetSearXngTools();

// 또는 인스턴스 직접 생성
var tools = new SearXngAiTools(client).AsSearXngTools();
```

`AsSearXngTools()`는 5개 메서드(`WebSearch`, `NewsSearch`, `GetInstanceInfo`, `GetSearchSuggestions`, `CheckSearXngStatus`)를 `AIFunctionFactory.Create`로 `AITool` 배열로 변환합니다. 메서드의 `[Description]` 속성이 도구 설명으로 사용됩니다.

### 샘플: Microsoft Agent Framework

`Samples/MicrosoftAgentFrameworkSample`은 MAF 에이전트에 SearXNG 도구를 연결해 대화형 검색을 수행하는 예제입니다.

```bash
$env:OPENAI_API_KEY = "sk-..."
# (선택) $env:OPENAI_BASE_URL = "http://localhost:8000/v1"   # vLLM 등 호환 엔드포인트
# (선택) $env:OPENAI_MODEL = "gpt-4o-mini"
# (선택) $env:SEARXNG_BASE_URL = "http://localhost:8080"
dotnet run --project Samples/MicrosoftAgentFrameworkSample
```

### 샘플: 일반 콘솔 검색

`Samples/SimpleSearchSample`은 AI 없이 텍스트를 입력해 검색 결과를 출력하는 최소 예제입니다.

```bash
$env:SEARXNG_BASE_URL = "http://localhost:8080"
dotnet run --project Samples/SimpleSearchSample
```

## ▶️ MCP 서버 실행

서버는 두 가지 전송 모드를 지원합니다:

| 모드 | 실행 방법 | 용도 |
|---|---|---|
| **Streamable HTTP** (기본) | `dotnet run --project SearXngSearch.Mcp` | 서비스로 상시 실행, 여러 클라이언트 공유 |
| **stdio** | `dotnet run --project SearXngSearch.Mcp --no-launch-profile -- --stdio` | MCP 클라이언트가 프로세스를 직접 시작하는 방식 |

### Streamable HTTP (기본)

```bash
dotnet run --project SearXngSearch.Mcp
```

서버는 `http://localhost:5000`에서 시작되며, MCP 엔드포인트는 `http://localhost:5000/mcp`입니다.

### stdio

```bash
dotnet run --project SearXngSearch.Mcp --no-launch-profile -- --stdio
```

클라이언트가 stdin/stdout으로 JSON-RPC 메시지를 주고받는 방식입니다. HTTP 서버를 띄우지 않고, MCP 클라이언트(Claude Desktop, Cursor, VS Code 등)가 서버 프로세스를 직접 시작할 때 사용합니다.

> ⚠️ **중요**: stdio 모드에서는 stdout이 MCP 프로토콜 채널입니다. `--no-launch-profile`은 `launchSettings.json`의 시작 설정 안내 메시지가 stdout에 출력되는 것을 막기 위해 필요합니다 (없으면 클라이언트 초기화가 실패할 수 있습니다). 앱 로그는 stderr로 출력됩니다.

## 🪟 Windows 서비스로 등록

서버는 `Microsoft.Extensions.Hosting.WindowsServices`를 사용해 Windows 서비스로 실행되도록 구성되어 있습니다.

### 1) publish

```powershell
dotnet publish SearXngSearch.Mcp -c Release -r win-x64 --self-contained false
```

> ℹ️ **참고**: publish 결과물에는 `Scripts/` 폴더(설치 스크립트)도 함께 포함됩니다. 소스 리포지토리가 없는 머신에서도 publish 폴더만 복사해 스크립트를 실행할 수 있습니다.

### 2) 서비스 설치 (관리자 권한 PowerShell)

```powershell
# 소스 리포지토리에서
pwsh -File SearXngSearch.Mcp\Scripts\install-service.ps1

# 또는 publish 폴더에서 (스크립트가 publish 결과물에 포함됨)
cd SearXngSearch.Mcp\bin\Release\net10.0\win-x64\publish
pwsh -File Scripts\install-service.ps1
```

스크립트는 publish 결과물(`SearXngSearch.Mcp\bin\Release\net10.0\win-x64\publish`)을 **전용 설치 디렉터리(`C:\Program Files\SearXngSearch.Mcp`)** 로 복사한 뒤, 그 디렉터리의 `SearXngSearch.Mcp.exe`로 `SearXngSearchMcpServer` 서비스를 등록하고 자동으로 시작합니다.

> ℹ️ **참고**: 서비스는 프로젝트 폴더가 아닌 설치 디렉터리에서 실행되므로, **서비스 실행 중에도 `dotnet publish`/`build`가 정상 동작**합니다. 재설치 시 스크립트가 기존 서비스를 제거하고 설치 디렉터리를 새로 채웁니다.

**설치 시 설정 주입** (`appsettings.json` 수정 없이):

```powershell
# SearXNG 주소 + 바인딩 URL 지정
pwsh -File SearXngSearch.Mcp\Scripts\install-service.ps1 -SearXngBaseUrl "http://<searxng-host>:8080" -Urls "http://localhost:8612"

# 모든 인터페이스에 바인딩 (LAN/외부 접근 가능, 방화벽 규칙 필요)
pwsh -File SearXngSearch.Mcp\Scripts\install-service.ps1 -SearXngBaseUrl "http://<searxng-host>:8080" -Urls "http://0.0.0.0:8612"
```

> ℹ️ **참고**: 설정은 서비스 binPath의 커맨드 라인 인자로 기록됩니다 (`sc.exe qc SearXngSearchMcpServer`로 확인 가능). 옵션을 생략하면 `appsettings.json` 값 그대로 등록됩니다.

### 3) 서비스 관리

```powershell
Get-Service SearXngSearchMcpServer          # 상태 확인
Restart-Service SearXngSearchMcpServer      # 재시작
Stop-Service SearXngSearchMcpServer         # 중지

# 서비스 제거 (설치 파일은 유지)
pwsh -File SearXngSearch.Mcp\Scripts\install-service.ps1 -Uninstall

# 서비스 + 설치 파일(C:\Program Files\SearXngSearch.Mcp)까지 제거
pwsh -File SearXngSearch.Mcp\Scripts\install-service.ps1 -Uninstall -RemoveFiles
```

### 수동 등록 (스크립트 없이)

```powershell
# 관리자 권한 PowerShell
# 1) publish 결과물을 설치 디렉터리로 복사
Copy-Item -Recurse -Force "SearXngSearch.Mcp\bin\Release\net10.0\win-x64\publish\*" "C:\Program Files\SearXngSearch.Mcp"
# 2) 서비스 등록
sc.exe create SearXngSearchMcpServer binPath= "C:\Program Files\SearXngSearch.Mcp\SearXngSearch.Mcp.exe" start= auto DisplayName= "SearXNG MCP Server"
Start-Service SearXngSearchMcpServer
```

> ℹ️ **참고**: 서비스는 `SYSTEM` 계정으로 실행됩니다. SearXNG 인스턴스(`appsettings.json`의 `BaseUrl`)에 네트워크 접근이 가능해야 하며, 로그는 Windows 이벤트 뷰어(Application)에서 확인합니다.

## 🐧 Ubuntu/Debian (systemd) 서비스로 등록

Ubuntu에서는 **systemd** 서비스를 사용합니다. `SearXngSearch.Mcp/Scripts/searxng-search-mcp.service` 유닛 파일과 `SearXngSearch.Mcp/Scripts/install-service.sh` 설치 스크립트가 제공됩니다.

### 1) 설치 (Ubuntu 머신에서, sudo 권한)

```bash
# 1) publish
dotnet publish SearXngSearch.Mcp -c Release -r linux-x64 --self-contained false

# 2) 서비스 설치
sudo bash SearXngSearch.Mcp/Scripts/install-service.sh

# 또는 publish 폴더에서 (스크립트가 publish 결과물에 포함됨)
cd SearXngSearch.Mcp/bin/Release/net10.0/linux-x64/publish
sudo bash Scripts/install-service.sh
```

스크립트가 자동으로 수행하는 일:
1. publish 결과물(`SearXngSearch.Mcp/bin/Release/net10.0/linux-x64/publish`) → **전용 설치 디렉터리 `/opt/searxng-search-mcp`** 로 복사
2. 전용 시스템 사용자 `searxng-search-mcp` 생성
3. `/etc/systemd/system/searxng-search-mcp.service` 설치
4. `systemctl enable --now` (부팅 시 자동 시작 + 즉시 시작)
5. `http://localhost:5000/health` 헬스 체크

> ℹ️ **참고**: 서비스는 프로젝트 폴더가 아닌 `/opt/searxng-search-mcp`에서 실행되므로, **서비스 실행 중에도 `dotnet publish`/`build`가 정상 동작**합니다. publish 위치가 다르면 `--publish-dir`으로 지정할 수 있습니다.

**설치 시 설정 주입** (`appsettings.json` 수정 없이, 유닛 파일 `ExecStart`에 인자로 기록됨):

```bash
# SearXNG 주소 + 바인딩 URL 지정
sudo bash SearXngSearch.Mcp/Scripts/install-service.sh --searxng-url http://<searxng-host>:8080 --urls http://localhost:8612

# 모든 인터페이스에 바인딩 (LAN/외부 접근 가능, 방화벽 규칙 필요)
sudo bash SearXngSearch.Mcp/Scripts/install-service.sh --searxng-url http://<searxng-host>:8080 --urls http://0.0.0.0:8612
```

> ℹ️ **참고**: 인자는 `systemctl cat searxng-search-mcp`의 `ExecStart`로 확인할 수 있습니다. 옵션을 생략하면 `appsettings.json` 값 그대로 설치됩니다.

### 2) 서비스 관리

```bash
systemctl status searxng-search-mcp       # 상태 확인
systemctl restart searxng-search-mcp      # 재시작
systemctl stop searxng-search-mcp         # 중지
journalctl -u searxng-search-mcp -f       # 실시간 로그

# 서비스 제거 (설치 파일은 유지)
sudo bash SearXngSearch.Mcp/Scripts/install-service.sh uninstall

# 서비스 + 설치 파일(/opt/searxng-search-mcp)까지 제거
sudo bash SearXngSearch.Mcp/Scripts/install-service.sh uninstall --remove-files
```

### 3) 수동 설치 (스크립트 없이)

```bash
# 1) publish
dotnet publish SearXngSearch.Mcp -c Release -r linux-x64 --self-contained false

# 2) 결과물을 설치 디렉터리로 복사 (sudo 필요, /opt는 root 소유)
sudo mkdir -p /opt/searxng-search-mcp
sudo cp -r SearXngSearch.Mcp/bin/Release/net10.0/linux-x64/publish/* /opt/searxng-search-mcp/

# 3) 유닛 파일 설치
sudo cp SearXngSearch.Mcp/Scripts/searxng-search-mcp.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now searxng-search-mcp
```

> ℹ️ **참고**:
> - 유닛 파일의 `ExecStart`/`User`/`WorkingDirectory`는 배포 환경에 맞게 수정하세요.
> - `Environment=SEARXNG__BASEURL=...` 으로 SearXNG 인스턴스 주소를 환경 변수로 주입할 수 있습니다.
> - `Restart=on-failure`로 실패 시 5초 후 자동 재시작됩니다.
> - .NET 10 런타임이 설치되어 있어야 합니다 (`apt install aspnetcore-runtime-10.0` 또는 self-contained publish).

## 🍎 macOS (launchd) 서비스로 등록

macOS에서는 **launchd** LaunchAgent를 사용합니다. `SearXngSearch.Mcp/Scripts/searxng-search-mcp.plist` 템플릿과 `SearXngSearch.Mcp/Scripts/install-service-macos.sh` 설치 스크립트가 제공됩니다.

> ℹ️ **참고**: LaunchAgent는 **현재 사용자**로 로그인 시 실행되며, 관리자 권한이 필요하지 않습니다. (전체 시스템 데몬이 필요하면 LaunchDaemon으로 변경하세요.)

### 1) 설치 (macOS 터미널)

```bash
# 1) publish (CPU 아키텍처에 맞게)
# Intel Mac:
dotnet publish SearXngSearch.Mcp -c Release -r osx-x64 --self-contained false
# Apple Silicon (M1/M2/...):
dotnet publish SearXngSearch.Mcp -c Release -r osx-arm64 --self-contained false

# 2) 서비스 설치
bash SearXngSearch.Mcp/Scripts/install-service-macos.sh

# 또는 publish 폴더에서 (스크립트가 publish 결과물에 포함됨)
cd SearXngSearch.Mcp/bin/Release/net10.0/osx-x64/publish
bash Scripts/install-service-macos.sh
```

스크립트가 자동으로 수행하는 일:
1. publish 결과물 → **전용 설치 디렉터리 `/usr/local/searxng-search-mcp`** 로 복사
2. `~/Library/LaunchAgents/com.searxng.mcp.plist` 생성 (설치 시 설정은 `EnvironmentVariables`로 주입)
3. `launchctl bootstrap` (로그인 시 자동 시작 + 즉시 시작)
4. `http://localhost:5000/health` 헬스 체크

> ℹ️ **참고**: 서비스는 프로젝트 폴더가 아닌 `/usr/local/searxng-search-mcp`에서 실행되므로, **서비스 실행 중에도 `dotnet publish`/`build`가 정상 동작**합니다. publish 위치가 다르면 `--publish-dir`으로 지정할 수 있습니다.

**설치 시 설정 주입** (`appsettings.json` 수정 없이, plist `EnvironmentVariables`에 기록됨):

```bash
# SearXNG 주소 + 바인딩 URL 지정
bash SearXngSearch.Mcp/Scripts/install-service-macos.sh --searxng-url http://<searxng-host>:8080 --urls http://localhost:8612

# 모든 인터페이스에 바인딩 (LAN/외부 접근 가능, 방화벽 규칙 필요)
bash SearXngSearch.Mcp/Scripts/install-service-macos.sh --searxng-url http://<searxng-host>:8080 --urls http://0.0.0.0:8612
```

> ℹ️ **참고**: 인자는 `cat ~/Library/LaunchAgents/com.searxng.mcp.plist`의 `EnvironmentVariables`로 확인할 수 있습니다. 옵션을 생략하면 `appsettings.json` 값 그대로 설치됩니다.

### 2) 서비스 관리

```bash
launchctl bootout gui/$(id -u)/com.searxng.mcp       # 중지
launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/com.searxng.mcp.plist  # 시작
tail -f ~/Library/Logs/searxng-search-mcp.err.log           # 실시간 로그

# 서비스 제거 (설치 파일은 유지)
bash SearXngSearch.Mcp/Scripts/install-service-macos.sh uninstall

# 서비스 + 설치 파일(/usr/local/searxng-search-mcp)까지 제거
bash SearXngSearch.Mcp/Scripts/install-service-macos.sh uninstall --remove-files
```

### 3) 수동 설치 (스크립트 없이)

```bash
# 1) publish
dotnet publish SearXngSearch.Mcp -c Release -r osx-x64 --self-contained false

# 2) 결과물을 설치 디렉터리로 복사
mkdir -p /usr/local/searxng-search-mcp
cp -r SearXngSearch.Mcp/bin/Release/net10.0/osx-x64/publish/* /usr/local/searxng-search-mcp/
chmod +x /usr/local/searxng-search-mcp/SearXngSearch.Mcp

# 3) plist 생성 후 등록 (SearXngSearch.Mcp/Scripts/searxng-search-mcp.plist를 참고해 직접 작성)
mkdir -p ~/Library/LaunchAgents
# ... plist 작성 ...
launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/com.searxng.mcp.plist
```

> ℹ️ **참고**:
> - plist의 `ProgramArguments`/`WorkingDirectory`/`EnvironmentVariables`는 배포 환경에 맞게 수정하세요.
> - `KeepAlive`의 `SuccessfulExit=false`로 실패 시에만 자동 재시작됩니다.
> - .NET 10 런타임이 설치되어 있어야 합니다 (self-contained publish 또는 `brew install dotnet`).

## 🐳 Docker로 실행

Docker가 설치되어 있으면 .NET SDK 없이도 실행할 수 있습니다. `docker-compose.yml`은 SearXNG 인스턴스와 MCP 서버를 **함께** 띄웁니다.

> ℹ️ **배포 모델**: Docker는 **소스 리포지토리**를 배포 대상으로 합니다. `Dockerfile`/`docker-compose.yml`/`docker/`은 리포지토리 **루트**에 있으며, `docker build`가 컨테이너 내부에서 소스를 컴파일합니다.
>
> - **네이티브 설치**: `dotnet publish` → `publish/` 폴더(실행 파일 + `Scripts/`) 배포 → 대상 머신에 .NET 런타임 필요
> - **Docker**: 소스 리포지토리(`git clone`) 배포 → `docker build`가 컨테이너에서 컴파일 → 대상 머신에 Docker만 필요
>
> 따라서 Docker 파일은 `publish` 결과물에 **포함되지 않습니다** (네이티브 설치 스크립트와 달리). Docker로 실행하려면 소스 리포지토리가 필요합니다.

### 1) SearXNG + MCP 서버 함께 실행 (권장)

```bash
docker compose up -d --build
```

실행 후:
- MCP 엔드포인트: `http://localhost:5000/mcp`
- 헬스 체크: `http://localhost:5000/health`
- SearXNG 웹 UI: `http://localhost:8080`

> ℹ️ **참고**: `docker/searxng-settings.yml`에 JSON API 활성화 설정이 포함되어 있어 별도 설정이 필요 없습니다.

### 2) MCP 서버만 실행 (기존 SearXNG 인스턴스가 있는 경우)

```bash
# 이미지 빌드
docker build -t searxng-search-mcp .

# 실행 (SearXNG 인스턴스 주소를 -e 로 지정)
docker run -d --name searxng-search-mcp -p 5000:5000 \
  -e SEARXNG__BASEURL=http://<host>:8080 \
  searxng-search-mcp
```

> ℹ️ **참고**:
> - 컨테이너 내부에서 `0.0.0.0:5000`에 바인딩되며, `-p 5000:5000`로 호스트에 노출됩니다.
> - SearXNG 인스턴스가 같은 Docker 네트워크에 있으면 서비스명(예: `http://searxng:8080`)을, 호스트의 인스턴스면 `http://host.docker.internal:8080`(macOS/Windows) 또는 `http://<호스트IP>:8080`(Linux)을 지정하세요.
> - Bearer 토큰 인증을 활성화하려면 `-e SEARXNG__AUTHTOKEN=<token>`을 추가하세요.

## ⚙️ 설정

`appsettings.json`의 `SearXng` 섹션:

| 키 | 기본값 | 설명 |
|---|---|---|
| `BaseUrl` | `http://localhost:8080` | SearXNG 인스턴스 URL |
| `ApiKey` | `null` | 인스턴스 인증 시 Bearer 토큰 |
| `TimeoutSeconds` | `30` | HTTP 요청 타임아웃(초) |
| `DefaultMaxResults` | `10` | 기본 반환 결과 수 |
| `MaxResultsLimit` | `30` | 허용되는 최대 결과 수 |
| `DefaultLanguage` | `all` | 기본 검색 언어 (`ko`, `en`, `ja`, `all` 등) |
| `DefaultTimeRange` | `null` | 기본 시간 범위 (`day`/`week`/`month`/`year`) |
| `DefaultCategories` | `null` | 기본 카테고리 (`general`, `news`, `it` 등) |
| `AuthToken` | `null` | MCP 엔드포인트 인증용 Bearer 토큰 (설정 시 `/mcp`에 인증 필요, `/health`는 항상 열림) |
| `CacheEnabled` | `true` | 검색 결과 캐싱 활성화 여부 |
| `CacheTtlSeconds` | `300` | 캐시 유지 시간(초) |
| `RateLimitPerMinute` | `30` | 인스턴스당 분당 최대 요청 수 (슬라이딩 윈도우) |
| `RateLimitWindowSeconds` | `60` | 레이트 리미트 슬라이딩 윈도우 기간(초) |
| `RateLimitWaitEnabled` | `true` | 한도 초과 시 슬롯 대기(큐잉) 여부. false면 즉시 오류 |
| `RateLimitWaitMaxSeconds` | `30` | 레이트 리미트 슬롯 대기 최대 시간(초) |
| `MaxRetries` | `2` | 429/503/연결 실패 시 같은 리플리카 재시도 횟수 (failover 전) |
| `RetryBaseDelaySeconds` | `1.0` | 재시도 기본 백오프 지연(초). 지수 배율(2^n) + ±25% jitter |
| `RetryMaxDelaySeconds` | `10.0` | 재시도 대기 최대 시간(초) |
| `MaxSnippetLength` | `null` | LLM 출력용 결과 요약 최대 길이(자). null이면 텍스트 300/JSON 500 |
| `MaxOutputChars` | `0` | LLM 출력용 전체 포맷 결과 최대 크기(자). 초과 시 하위 결과 생략. 0 = 제한 없음 |
| `HtmlFallbackEnabled` | `false` | JSON API 비활성화(403/404) 시 HTML 응답 파싱 (best-effort) |

환경 변수로 오버라이드할 수도 있습니다:

```bash
$env:SEARXNG__BASEURL = "https://my-searxng.example.com"
dotnet run --project SearXngSearch.Mcp
```

### 포트 변경 (기본 5000)

기본 포트는 Kestrel 기본값(`http://localhost:5000`)입니다. 다른 포트를 사용하려면 아래 중 하나를 쓰면 됩니다 (우선순위: `--urls` 커맨드 라인 > `appsettings.json`의 `Urls` > `ASPNETCORE_URLS` 환경 변수):

```bash
# 1) 환경 변수 (권장, 설정 파일 수정 불필요)
$env:ASPNETCORE_URLS = "http://localhost:9615"
dotnet run --project SearXngSearch.Mcp

# 2) 커맨드 라인 인자
dotnet run --project SearXngSearch.Mcp -- --urls http://localhost:9615
```

`appsettings.json`에 `"Urls": "http://localhost:9615"`를 추가하는 것도 가능합니다 (가장 높은 우선순위). 포트가 바뀌면 MCP 엔드포인트도 `http://localhost:9615/mcp`처럼 함께 바뀌므로, 클라이언트(`mcp.json` 등)의 URL도 함께 수정하세요.

> ℹ️ **참고**: Windows 서비스/`systemd`로 실행할 때도 `ASPNETCORE_URLS` 환경 변수로 포트를 지정할 수 있습니다 (systemd 유닛의 `Environment=ASPNETCORE_URLS=...` 주석 참조).

### 외부 접근 설정 (LAN/인터넷)

기본값(`http://localhost:5000`)은 **루프백만 바인딩**되어 같은 PC에서만 접근 가능합니다. 다른 PC에서 접속하려면 아래 3가지를 설정하세요.

**1) 모든 인터페이스에 바인딩**

```bash
$env:ASPNETCORE_URLS = "http://0.0.0.0:5000"
dotnet run --project SearXngSearch.Mcp
```

또는 `appsettings.json`에 `"Urls": "http://0.0.0.0:5000"`을 추가. 특정 IP만 허용하려면 `http://<your-ip>:5000`처럼 지정하세요.

**2) 방화벽 인바운드 규칙 허용 (Windows, 관리자 권한)**

```powershell
New-NetFirewallRule -DisplayName "SearXngSearch.Mcp" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5000
```

**3) 클라이언트 URL 변경**

다른 PC의 `mcp.json`에서 `http://<서버IP>:5000/mcp`로 지정.

> 🔒 **보안 경고**: `0.0.0.0`으로 열면 네트워크 내 모든 사람이 검색 도구를 호출할 수 있고, CORS도 전체 허용(`AllowAnyOrigin`) 상태입니다.
> - **인증 활성화**: `SearXng:AuthToken`에 토큰을 설정하면 `/mcp` 요청에 `Authorization: Bearer <token>` 헤더가 필요합니다 (아래 [Bearer 토큰 인증](#bearer-토큰-인증) 참조). `AuthToken`이 비어 있으면 인증 없이 열려 있으므로 **반드시 설정하세요**.
> - **LAN 공유**: 신뢰할 수 있는 네트워크에서만 사용하세요.
> - **인터넷 노출**: 직접 노출하지 말고 reverse proxy(nginx/Caddy 등) 뒤에서 **TLS + 인증**을 거세요. 이 경우 앱은 `localhost` 바인딩을 유지하고 프록시만 443으로 노출하는 것이 안전합니다.

## 🔌 MCP 클라이언트 연결

### Streamable HTTP (서버를 미리 실행해 둔 경우)

VS Code의 `mcp.json`(사용자 또는 워크스페이스 설정)에 추가:

```json
{
  "servers": {
    "SearXNG 검색 서버": {
      "type": "http",
      "url": "http://localhost:5000/mcp"
    }
  }
}
```

### stdio (클라이언트가 서버를 직접 시작하는 경우)

서버를 미리 실행할 필요가 없습니다. 클라이언트가 `dotnet` 프로세스를 시작하고 stdin/stdout으로 통신합니다.

VS Code의 `mcp.json`:

```json
{
  "servers": {
    "SearXNG 검색 서버 (stdio)": {
      "command": "dotnet",
      "args": ["run", "--project", "SearXngSearch.Mcp", "--no-launch-profile", "--", "--stdio"]
    }
  }
}
```

> ℹ️ **참고**: `--project` 경로는 `mcp.json`이 있는 위치(사용자 설정이면 홈 디렉터리 등) 기준입니다. 절대경로(`"D:/path/to/SearXngSearch.Mcp/SearXngSearch.Mcp"`)를 사용하는 것이 안전합니다. publish한 경우 `dotnet SearXngSearch.Mcp.dll --stdio` 또는 `SearXngSearch.Mcp.exe --stdio`로 대체할 수 있습니다.

Claude Desktop(`claude_desktop_config.json`) 등 다른 클라이언트도 동일한 `command`/`args` 형식을 사용합니다:

```json
{
  "mcpServers": {
    "searxng": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/path/to/SearXngSearch.Mcp/SearXngSearch.Mcp", "--no-launch-profile", "--", "--stdio"]
    }
  }
}
```

### MCP Inspector

```bash
npx @modelcontextprotocol/inspector
```

- Transport: **Streamable HTTP**
- URL: `http://localhost:5000/mcp`

### curl (직접 테스트)

```bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"web_search","arguments":{"query":".NET 10","maxResults":5}}}'
```

## 🧪 테스트

### 자동화 테스트 (SearXNG 인스턴스 불필요)

`SearXngSearch.Tests`는 SearXNG 인스턴스를 **스텁(`FakeSearXngHandler`)으로 대체**해 네트워크 없이 전체 기능을 검증합니다.

```bash
dotnet test
```

| 구분 | 대상 |
|---|---|
| 단위 | 슬라이딩 윈도우 레이트 리미터, 결과 포맷터(text/json), HTML 폴백 파서, `SearXngClient`(URI 구성, maxResults 클램핑, 캐싱, failover, 레이트 리미트, HTML 폴백, 타임아웃, /config categories dict/array 파싱) |
| 통합 | `/health`, Bearer 인증 4시나리오(무토큰/잘못된/올바른/health 예외), MCP 프로토콜(raw JSON-RPC로 `initialize` → `tools/list` 5개 도구 → 각 `tools/call`) |

### End-to-end (실인스턴스)

Docker로 SearXNG 인스턴스를 띄운 뒤 MCP 서버를 실행해 실제 검색 흐름을 확인하세요:

```bash
# 1) SearXNG 인스턴스 (http://localhost:8080)
#    ※ "SearXNG 인스턴스 준비" 섹션에서 만든 searxng/settings.yml(JSON API 활성화)이 필요합니다.
docker run -d --name searxng -p 8080:8080 -v ./searxng:/etc/searxng searxng/searxng:latest

# 2) MCP 서버 (다른 터미널)
dotnet run --project SearXngSearch.Mcp
```

이후 `http://localhost:5000/health`로 헬스 체크, `http://localhost:5000/mcp`로 MCP 도구(`check_searxng_status` 등)를 호출해 확인합니다.

## 📝 참고 사항

- **Stateless 모드**: 검색은 세션 상태를 필요로 하지 않으므로 `HttpServerSessionMode.Stateless`로 동작합니다. 세션 어피니티 없이 수평 확장이 가능합니다.
- **stdio 모드**: `--stdio` 인자로 실행하면 HTTP 서버 없이 stdin/stdout JSON-RPC로 동작합니다. stdout은 프로토콜 전용이므로 모든 로그는 stderr로 출력됩니다. Windows 서비스 등록은 HTTP 모드 전용입니다.
- **에러 처리**: SearXNG 인스턴스 미실행/오류 시 `check_searxng_status`, `get_instance_info`, `get_search_suggestions`는 원인을 설명하는 텍스트를 반환합니다 (JSON API 미활성화, 네트워크 오류, 타임아웃 등). `web_search`/`news_search`는 예외가 MCP isError 도구 결과로 전달됩니다.
- **CORS**: 브라우저 기반 클라이언트(MCP Inspector 등)를 위해 전체 허용으로 설정되어 있습니다. 공개 환경 배포 시 제한하세요.

## 🚀 고급 기능

### Bearer 토큰 인증

`SearXng:AuthToken`에 값을 설정하면 `/mcp` 엔드포인트에 Bearer 인증이 적용됩니다. `/health`는 모니터링을 위해 항상 열려 있습니다.

```bash
# 환경 변수로 토큰 지정
$env:SearXng__AuthToken = "my-secret-token"
dotnet run --project SearXngSearch.Mcp
```

클라이언트는 요청에 `Authorization: Bearer my-secret-token` 헤더를 포함해야 합니다.

```bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Authorization: Bearer my-secret-token" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

> ℹ️ **참고**: stdio 모드는 프로세스를 직접 시작하는 클라이언트 전용이므로 인증이 적용되지 않습니다. 인증은 HTTP 모드에만 유효합니다.

### 결과 캐싱

동일한 검색 조건(쿼리, 언어, 카테고리, 시간 범위, 결과 수, 페이지)에 대한 응답을 메모리에 캐시합니다 (기본 TTL 5분). 반복 검색 시 SearXNG 부하를 줄이고 응답을 빠르게 합니다.

- `CacheEnabled`: `false`로 비활성화
- `CacheTtlSeconds`: 유지 시간 변경

### 레이트 리미트

SearXNG 인스턴스별로 **슬라이딩 윈도우** 레이트 리미트가 적용되어, 몰아서 검색해도 업스트림에 도달하는 요청이 한도를 넘지 않습니다.

- `RateLimitPerMinute`: 분당 허용 요청 수 (기본 30)
- `RateLimitWindowSeconds`: 윈도우 기간(초, 기본 60)
- `RateLimitWaitEnabled`: 한도 초과 시 다음 요청이 **슬롯이 비어질 때까지 대기**하는지 여부 (기본 `true`). `false`면 즉시 오류 반환
- `RateLimitWaitMaxSeconds`: 슬롯 대기 최대 시간(초, 기본 30). 초과 시 오류

### 재시도 (백오프)

429/503 또는 연결 실패 시 같은 리플리카에서 **지수 백오프(±25% jitter)** 로 재시도한 뒤, 여전히 실패하면 다음 리플리카로 failover합니다. 429 응답의 `Retry-After` 헤더가 있으면 그 값을 우선 반영합니다.

- `MaxRetries`: 리플리카당 재시도 횟수 (기본 2, 0이면 재시도 없이 즉시 failover)
- `RetryBaseDelaySeconds`: 기본 백오프 지연(초, 기본 1.0) — n번째 재시도는 `base * 2^n`
- `RetryMaxDelaySeconds`: 대기 최대 시간(초, 기본 10)

### 출력 크기 제어 (LLM용)

검색 결과는 LLM 컨텍스트에 그대로 투입되므로, 포맷 단계에서 크기를 제한할 수 있습니다.

- `MaxSnippetLength`: 결과 요약(content) 최대 길이(자). 초과 시 `…`으로 잘립니다. null이면 형식별 기본값(텍스트 300자, JSON 500자)
- `MaxOutputChars`: 전체 포맷 결과 최대 크기(자). 초과 시 하위 결과부터 생략하고 생략 개수를 표시합니다. 0 이하이면 제한 없음

```json
"SearXng": {
  "MaxSnippetLength": 200,
  "MaxOutputChars": 4000
}
```

### 리플리카 failover

`BaseUrl`에 세미콜론(`;`)으로 여러 인스턴스를 나열하면, 첫 번째 인스턴스가 429/503을 반환하거나 연결 실패 시 다음 인스턴스로 자동 전환됩니다.

```json
"SearXng": {
  "BaseUrl": "http://searxng-1:8080;http://searxng-2:8080;http://searxng-3:8080"
}
```

> ℹ️ **참고**: 모든 리플리카가 실패하면 마지막 오류가 반환됩니다. 리플리카 간 캐시는 공유되지 않습니다 (인스턴스별 레이트 리미터만 별도).

### HTML 폴백

JSON API가 비활성화된 인스턴스(403/404)에서 HTML 검색 결과 페이지를 [AngleSharp](https://www.nuget.org/packages/AngleSharp)로 best-effort 파싱합니다. 제목/URL/요약은 추출되지만 엔진/점수/날짜 등 메타데이터는 제한적입니다.

- `HtmlFallbackEnabled`: `true`로 활성화 (기본 `false`)
- JSON API를 활성화할 수 있다면 `settings.yml`의 `search.formats`에 `json`을 추가하는 것이 더 좋습니다.

### 인스턴스 정보 / 자동완성

- `get_instance_info`: SearXNG `/config` 엔드포인트를 호출해 버전, 사용 가능한 카테고리, 검색 엔진 목록을 반환합니다. `web_search`의 `categories` 파라미터에 어떤 값을 쓸 수 있는지 확인할 때 유용합니다.
- `get_search_suggestions`: 검색 응답의 `suggestions` 필드를 노출해 자동완성 후보를 제공합니다.
