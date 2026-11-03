[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localRoot = Join-Path $repositoryRoot '.codex-build\local-login-server'
$stateFile = Join-Path $localRoot 'process.json'
if (-not (Test-Path -LiteralPath $stateFile)) {
    Write-Output 'No local login server started by Start-LocalLoginServer.ps1 was found.'
    return
}
$state = Get-Content -Raw -LiteralPath $stateFile | ConvertFrom-Json
$api = Get-Process -Id $state.ApiProcessId -ErrorAction SilentlyContinue
if ($api -and $api.StartTime.ToUniversalTime().Ticks.ToString() -eq $state.ApiStartTicks -and
    [string]::Equals($api.Path, $state.DotnetPath, [StringComparison]::OrdinalIgnoreCase)) {
    Stop-Process -Id $api.Id
    $api.WaitForExit()
}
$dataDirectory = Join-Path $localRoot 'postgres'
& (Join-Path $state.PgBin 'pg_ctl.exe') -D $dataDirectory status *> $null
if ($LASTEXITCODE -eq 0) {
    & (Join-Path $state.PgBin 'pg_ctl.exe') -D $dataDirectory -m fast -w stop
    if ($LASTEXITCODE -ne 0) { throw 'The local database could not be stopped.' }
}
Remove-Item -LiteralPath $stateFile
Write-Output 'Local login API and database stopped. Test accounts and mail are preserved.'
