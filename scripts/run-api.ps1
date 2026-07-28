# 单独启动 MES API
# 用法: pwsh -File D:\Game\MES\scripts\run-api.ps1
$ErrorActionPreference = "Stop"
$proj = "D:\Game\MES\src\Mes.Api\Mes.Api.csproj"
if (-not (Test-Path $proj)) {
  $proj = Join-Path $PSScriptRoot "..\src\Mes.Api\Mes.Api.csproj"
}

sqllocaldb start MSSQLLocalDB 2>$null | Out-Null
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5101"

Write-Host "Starting MES API -> http://localhost:5101  (Ctrl+C 正常退出)" -ForegroundColor Cyan
Write-Host "若无 ApplicationStopping 日志就回到 PS 提示符，多半是进程被外部强杀。" -ForegroundColor DarkYellow
dotnet run --project $proj --no-launch-profile
