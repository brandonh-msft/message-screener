[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "[Info] === Message Screener WorkIQ Auth ===" -ForegroundColor Cyan
Write-Host "[Info] Backend-managed M365 OAuth is disabled in this project." -ForegroundColor Yellow
Write-Host "[Info] WorkIQ access is expected to run in user-approved Copilot/Teams context." -ForegroundColor Yellow
Write-Host "[Info] No local auth bootstrap step is required for this service." -ForegroundColor Green
