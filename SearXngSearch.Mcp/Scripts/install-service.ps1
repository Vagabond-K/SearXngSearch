# ============================================================
# SearXngSearchMcpServer Windows 서비스 등록/제거 스크립트
#
# 사용법 (관리자 권한 PowerShell):
#   1) 먼저 publish:
#        dotnet publish SearXngSearch.Mcp -c Release -r win-x64 --self-contained false
#   2) 서비스 설치 (publish 결과물을 설치 디렉터리로 복사 후 등록 + 시작):
#        pwsh -File SearXngSearch.Mcp\Scripts\install-service.ps1
#        # 또는 publish 폴더에서 (스크립트가 publish 결과물에 포함됨):
#        cd SearXngSearch.Mcp\bin\Release\net10.0\win-x64\publish
#        pwsh -File Scripts\install-service.ps1
#   3) 서비스 제거:
#        pwsh -File install-service.ps1 -Uninstall
#        pwsh -File install-service.ps1 -Uninstall -RemoveFiles   # 설치 파일까지 제거
#
# 옵션:
#   -SearXngBaseUrl "http://<searxng-host>:8080"  # appsettings.json 수정 없이 SearXNG 주소 지정
#   -Urls "http://localhost:8612"                # 바인딩 URL 지정
#   -Urls "http://0.0.0.0:8612"                  # 모든 인터페이스 (LAN/외부 접근, 방화벽 규칙 필요)
#   -InstallDir "C:\Program Files\SearXngSearch.Mcp"    # 설치 디렉터리 (기본값)
#   -PublishDir "..."                            # publish 출력 위치 (기본: bin\Release\net10.0\win-x64\publish)
#   -NoStart                                     # 등록만 하고 시작은 안 함
#
# 서비스는 프로젝트 폴더가 아닌 전용 설치 디렉터리에서 실행되므로,
# 서비스 실행 중에도 dotnet publish/build가 정상 동작합니다.
# ============================================================
[CmdletBinding()]
param(
    [string]$ServiceName = "SearXngSearchMcpServer",
    [string]$DisplayName = "SearXNG MCP Server",
    [string]$SearXngBaseUrl = "",
    [string]$Urls = "",
    [string]$InstallDir = "C:\Program Files\SearXngSearch.Mcp",
    [string]$PublishDir = "",
    [switch]$Uninstall,
    [switch]$NoStart,
    [switch]$RemoveFiles
)

$ErrorActionPreference = "Stop"

# 관리자 권한 확인
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "관리자 권한으로 실행해야 합니다. PowerShell을 '관리자 권한으로 실행'하세요."
    exit 1
}

# publish 출력 위치 결정:
# - 스크립트가 publish 폴더의 Scripts/ 안에 있으면 (SearXngSearch.Mcp.exe가 부모 폴더에 있는 경우) 부모 폴더를 사용
# - 그렇지 않으면(소스 리포지토리에서 실행) 프로젝트의 기본 publish 경로를 사용
# (스크립트는 SearXngSearch.Mcp\Scripts\ 에 위치하며, publish 시 publish\Scripts\ 로 복사됨)
if (-not $PublishDir) {
    $parentDir = Split-Path -Parent $PSScriptRoot
    if (Test-Path (Join-Path $parentDir "SearXngSearch.Mcp.exe")) {
        $PublishDir = $parentDir
    } else {
        $PublishDir = Join-Path $parentDir "bin\Release\net10.0\win-x64\publish"
    }
}
$publishExe = Join-Path $PublishDir "SearXngSearch.Mcp.exe"
# 서비스는 프로젝트 폴더가 아닌 전용 설치 디렉터리에서 실행됩니다.
# 이렇게 하면 서비스 실행 중에도 dotnet publish/build가 정상 동작합니다.
$binPath = Join-Path $InstallDir "SearXngSearch.Mcp.exe"

if ($Uninstall) {
    Write-Host "==> 서비스 '$ServiceName' 제거 중..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe stop $ServiceName | Out-Null
    sc.exe delete $ServiceName
    if ($LASTEXITCODE -eq 0) {
        Write-Host "==> 서비스 '$ServiceName'이(가) 제거되었습니다." -ForegroundColor Green
    } else {
        Write-Error "서비스 제거에 실패했습니다. (exit code: $LASTEXITCODE)"
    }
    if ($RemoveFiles) {
        if (Test-Path $InstallDir) {
            Remove-Item -Path $InstallDir -Recurse -Force
            Write-Host "==> 설치 파일 제거: $InstallDir" -ForegroundColor Green
        }
    } else {
        Write-Host "==> 설치 파일은 $InstallDir 에 남아있습니다. (제거하려면 -RemoveFiles)" -ForegroundColor DarkGray
    }
    exit $LASTEXITCODE
}

# 1) publish 결과물 확인 (이 스크립트는 publish하지 않습니다 — 먼저 dotnet publish 실행 필요)
if (-not (Test-Path $publishExe)) {
    Write-Error "publish 결과물을 찾을 수 없습니다: $publishExe`n먼저 실행하세요: dotnet publish SearXngSearch.Mcp -c Release -r win-x64 --self-contained false"
    exit 1
}

# 2) 기존 서비스 존재 시 제거 (재설치를 위해, 설치 디렉터리 파일 잠금 해제)
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "==> 기존 서비스 발견. 재설치를 위해 제거합니다." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe stop $ServiceName | Out-Null
    sc.exe delete $ServiceName | Out-Null
    # sc.exe delete는 서비스를 "삭제 예정(marked for delete)" 상태로만 표시하고,
    # 모든 핸들이 닫힐 때까지 실제 삭제는 지연됩니다. 그 상태에서 CreateService를
    # 하면 1072(ERROR_SERVICE_MARKED_FOR_DELETE)가 나므로, 실제 소멸을 대기합니다.
    $waited = 0
    while ((Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) -and $waited -lt 15) {
        Start-Sleep -Seconds 1
        $waited++
    }
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Write-Error "서비스가 완전히 삭제되지 않았습니다 (삭제 예정 상태 지속). 잠시 후 다시 실행하세요."
        exit 1
    }
}

# 3) publish 결과물 → 설치 디렉터리 복사
Write-Host "==> $PublishDir → $InstallDir 복사 중..." -ForegroundColor Cyan
if (Test-Path $InstallDir) {
    Remove-Item -Path (Join-Path $InstallDir "*") -Recurse -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}
Copy-Item -Path (Join-Path $PublishDir "*") -Destination $InstallDir -Recurse -Force

# 4) 서비스 등록 (binPath = exe 경로 + 인자)
$binPathValue = $binPath
if ($Urls) { $binPathValue += " --urls $Urls" }
if ($SearXngBaseUrl) { $binPathValue += " --SearXng:BaseUrl $SearXngBaseUrl" }

Write-Host "==> 서비스 등록 중: $ServiceName" -ForegroundColor Cyan
Write-Host "==> binPath: $binPathValue" -ForegroundColor Cyan
& sc.exe create $ServiceName "binPath=" $binPathValue "start=" "auto" "DisplayName=" $DisplayName

if ($LASTEXITCODE -ne 0) {
    Write-Error "서비스 등록에 실패했습니다. (exit code: $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# 헬스 체크 URL (로컬 확인용 — 0.0.0.0/바인딩 주소도 localhost로 확인)
$healthUrl = "http://localhost:5000"
# 사용자에게 안내할 실제 접근 엔드포인트 (0.0.0.0 바인딩 시 LAN IP로 표시)
$displayUrl = "http://localhost:5000"
if ($Urls) {
    $u = [System.Uri]$Urls
    $port = $u.Port
    if ($u.Host -eq "0.0.0.0" -or $u.Host -eq "+" -or $u.Host -eq "[::]") {
        # 모든 인터페이스 바인딩: 헬스 체크는 localhost, 안내는 LAN IP
        $healthUrl = "http://localhost:$port"
        $lanIp = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -ne "127.0.0.1" -and $_.IPAddress -notlike "169.*" } |
            Select-Object -First 1).IPAddress
        $displayUrl = if ($lanIp) { "http://${lanIp}:$port" } else { "http://localhost:$port" }
    } else {
        $healthUrl = "http://${u.Host}:$port"
        $displayUrl = $healthUrl
    }
}

# 5) 시작
if (-not $NoStart) {
    Write-Host "==> 서비스 시작 중..." -ForegroundColor Cyan
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 3
    $svc = Get-Service -Name $ServiceName
    Write-Host "==> 서비스 상태: $($svc.Status)" -ForegroundColor Green

    # 헬스 체크
    try {
        $health = Invoke-WebRequest -Uri "$healthUrl/health" -TimeoutSec 5 -UseBasicParsing
        Write-Host "==> 헬스 체크: $($health.StatusCode) $($health.Content)" -ForegroundColor Green
    } catch {
        Write-Warning "헬스 체크에 실패했습니다: $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "완료! MCP 엔드포인트: $displayUrl/mcp" -ForegroundColor Green
Write-Host "서비스 관리 명령:"
Write-Host "  Get-Service $ServiceName"
Write-Host "  Restart-Service $ServiceName"
Write-Host "  Get-EventLog -LogName Application -Source $ServiceName -Newest 20"
