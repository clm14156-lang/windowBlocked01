[CmdletBinding()]
param(
    [string]$ServerRoot = 'E:\FocusTime_Server',
    [ValidateRange(1024, 65535)][int]$ApiPort = 5080,
    [ValidateRange(1024, 65535)][int]$DatabasePort = 55432
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localRoot = Join-Path $repositoryRoot '.codex-build\local-login-server'
$dataDirectory = Join-Path $localRoot 'postgres'
$contentRoot = Join-Path $localRoot 'api'
$mailDirectory = Join-Path $contentRoot '.artifacts\mail'
$stateFile = Join-Path $localRoot 'process.json'
$passwordFile = Join-Path $localRoot 'database-password.dpapi'
$dotnet = Join-Path $ServerRoot '.tools\dotnet\dotnet.exe'
$pgBin = Join-Path $ServerRoot '.tools\pgsql\bin'
$apiDll = Join-Path $ServerRoot 'src\FocusTime.Api\bin\Release\net10.0\FocusTime.Api.dll'
$baseUrl = "http://localhost:$ApiPort/"
$lanSettingsFile = Join-Path $localRoot 'lan-settings.json'
$lan = $null
$lanBaseUrl = $null
$databaseListenAddresses = '127.0.0.1'
if (Test-Path -LiteralPath $lanSettingsFile) {
    $lan = Get-Content -Raw -LiteralPath $lanSettingsFile | ConvertFrom-Json
    $lanAddress = $null
    if (-not [Net.IPAddress]::TryParse([string]$lan.Address, [ref]$lanAddress) -or
        $lanAddress.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or
        [Net.IPAddress]::IsLoopback($lanAddress) -or [int]$lan.HttpsPort -notin 1024..65535 -or
        [string]$lan.CertificateSubject -notmatch '^FocusTime-LAN-[0-9.]+$') {
        throw 'Invalid lan-settings.json. Re-run the backend scripts/Enable-LanDevelopment.ps1.'
    }
    $localAddresses = [Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
        ForEach-Object { $_.GetIPProperties().UnicastAddresses } |
        ForEach-Object { $_.Address.ToString() }
    if ([string]$lan.Address -notin $localAddresses) {
        throw 'The saved LAN IP is no longer assigned to this computer. Re-run Enable-LanDevelopment.ps1.'
    }
    $databaseListenAddresses = '127.0.0.1,' + $lan.Address
    $lanBaseUrl = 'https://{0}:{1}/' -f $lan.Address, $lan.HttpsPort
}

foreach ($required in @($dotnet, $apiDll, "$pgBin\initdb.exe", "$pgBin\pg_ctl.exe")) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required file is missing: $required. See Docs/LocalLoginTesting.md."
    }
}
New-Item -ItemType Directory -Force -Path $localRoot, $mailDirectory | Out-Null

if (Test-Path -LiteralPath $stateFile) {
    $state = Get-Content -Raw -LiteralPath $stateFile | ConvertFrom-Json
    $existing = Get-Process -Id $state.ApiProcessId -ErrorAction SilentlyContinue
    if ($existing -and $existing.StartTime.ToUniversalTime().Ticks.ToString() -eq $state.ApiStartTicks) {
        if ([string]$state.LanBaseUrl -ne [string]$lanBaseUrl) {
            throw 'LAN settings changed. Run Stop-LocalLoginServer.ps1, then start again.'
        }
        Write-Output "Local login API is already running: $($state.BaseUrl)"
        if ($lanBaseUrl) { Write-Output "LAN API: $lanBaseUrl" }
        Write-Output "Readiness: $($state.BaseUrl)health/ready"
        Write-Output "Development mail: $mailDirectory"
        return
    }
}

# Refuse to replace a listener, including another development API or database.
foreach ($port in @($ApiPort, $DatabasePort)) {
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $port)
    try { $listener.Start() }
    catch { throw "Port $port is unavailable. Stop the existing service or choose a different port." }
    finally { $listener.Stop() }
}
if ($lan) {
    $listener = New-Object Net.Sockets.TcpListener($lanAddress, [int]$lan.HttpsPort)
    try { $listener.Start() }
    catch { throw 'The LAN HTTPS port is unavailable. Choose a different HttpsPort.' }
    finally { $listener.Stop() }
}

if (-not (Test-Path -LiteralPath $passwordFile)) {
    if (Test-Path -LiteralPath (Join-Path $dataDirectory 'PG_VERSION')) {
        throw 'The local database exists but its saved password is missing. Restore database-password.dpapi.'
    }
    $bytes = New-Object byte[] 24
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $password = [BitConverter]::ToString($bytes).Replace('-', '')
    ConvertTo-SecureString $password -AsPlainText -Force | ConvertFrom-SecureString |
        Set-Content -LiteralPath $passwordFile -Encoding ASCII
}
$securePassword = (Get-Content -Raw -LiteralPath $passwordFile).Trim() | ConvertTo-SecureString
$password = (New-Object Net.NetworkCredential('', $securePassword)).Password
$savedEnvironment = @{}
foreach ($name in @('DOTNET_ROOT', 'ASPNETCORE_ENVIRONMENT', 'ConnectionStrings__Database',
    'Email__Mode', 'Email__PickupDirectory', 'Authentication__Secret')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$databaseStarted = $false
$api = $null
$completed = $false
try {
    if (-not (Test-Path -LiteralPath (Join-Path $dataDirectory 'PG_VERSION'))) {
        $temporaryPassword = Join-Path $localRoot 'initdb-password.tmp'
        try {
            [IO.File]::WriteAllText($temporaryPassword, $password)
            & "$pgBin\initdb.exe" -D $dataDirectory -U postgres --encoding=UTF8 --locale=C --auth=scram-sha-256 "--pwfile=$temporaryPassword" *> (Join-Path $localRoot 'initdb.log')
            if ($LASTEXITCODE -ne 0) { throw 'Database initialization failed. See initdb.log.' }
        }
        finally {
            if (Test-Path -LiteralPath $temporaryPassword) { Remove-Item -LiteralPath $temporaryPassword }
        }
    }
    & "$pgBin\pg_ctl.exe" -D $dataDirectory -l (Join-Path $localRoot 'postgres.log') -o "-h $databaseListenAddresses -p $DatabasePort" -w start
    if ($LASTEXITCODE -ne 0) { throw 'Database startup failed. See postgres.log.' }
    $databaseStarted = $true

    Copy-Item -LiteralPath (Join-Path $ServerRoot 'src\FocusTime.Api\appsettings.json') -Destination $contentRoot -Force
    if ($lan) {
        $apiSettingsFile = Join-Path $contentRoot 'appsettings.json'
        $apiSettings = Get-Content -Raw -LiteralPath $apiSettingsFile | ConvertFrom-Json
        $kestrel = @{
            Endpoints = @{
                LocalHttp = @{ Url = $baseUrl }
                LanHttps = @{
                    Url = $lanBaseUrl
                    Certificate = @{
                        Subject = $lan.CertificateSubject
                        Store = 'My'
                        Location = 'CurrentUser'
                        AllowInvalid = $false
                    }
                }
            }
        }
        $apiSettings | Add-Member -NotePropertyName Kestrel -NotePropertyValue $kestrel -Force
        $apiSettings | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $apiSettingsFile -Encoding UTF8
    }
    $env:DOTNET_ROOT = Split-Path -Parent $dotnet
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__Database = "Host=127.0.0.1;Port=$DatabasePort;Database=postgres;Username=postgres;Password=$password;Timeout=3;Command Timeout=5"
    $env:Email__Mode = 'Pickup'
    $env:Email__PickupDirectory = $mailDirectory
    # Use the persistent development key under this isolated content root.
    Remove-Item Env:Authentication__Secret -ErrorAction SilentlyContinue
    & $dotnet $apiDll --contentRoot $contentRoot --migrate *> (Join-Path $localRoot 'migrate.log')
    if ($LASTEXITCODE -ne 0) { throw 'Database migration failed. See migrate.log.' }

    $arguments = '"{0}" --contentRoot "{1}" --urls "{2}"' -f $apiDll, $contentRoot, $baseUrl
    $api = Start-Process -FilePath $dotnet -ArgumentList $arguments -WorkingDirectory $contentRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $localRoot 'api.log') -RedirectStandardError (Join-Path $localRoot 'api-error.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($api.HasExited) { throw 'The API exited. See api.log and api-error.log.' }
        try {
            $health = Invoke-RestMethod "${baseUrl}health/ready" -TimeoutSec 1
            if ($health.status -eq 'Healthy') { $ready = $true; break }
        }
        catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $ready) { throw 'The API did not become ready. See api.log and api-error.log.' }
    @{
        ApiProcessId = $api.Id
        ApiStartTicks = $api.StartTime.ToUniversalTime().Ticks.ToString()
        DotnetPath = $dotnet
        PgBin = $pgBin
        BaseUrl = $baseUrl
        LanBaseUrl = $lanBaseUrl
        DatabasePort = $DatabasePort
    } | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
    $completed = $true
    Write-Output "Local login API ready: ${baseUrl}health/ready"
    if ($lanBaseUrl) {
        Write-Output "LAN API: $lanBaseUrl"
        Write-Output "LAN PostgreSQL: $($lan.Address):$DatabasePort"
    }
    Write-Output "Development mail: $mailDirectory"
    Write-Output "Client setting: `$env:FOCUSTIME_API_BASE_URL = '$baseUrl'"
    Write-Output 'Stop with: .\scripts\Stop-LocalLoginServer.ps1'
}
finally {
    if (-not $completed) {
        if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id }
        if ($databaseStarted) { & "$pgBin\pg_ctl.exe" -D $dataDirectory -m fast -w stop }
    }
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
}
