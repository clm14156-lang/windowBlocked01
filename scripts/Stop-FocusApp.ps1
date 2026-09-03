[CmdletBinding()]
param()

$projectPattern = 'FocusApp\.(Desktop|Service|Agent)\.csproj'
$applicationNames = @('FocusApp.Desktop.exe', 'FocusApp.Service.exe', 'FocusApp.Agent.exe')

function Get-FocusAppProcesses
{
    Get-CimInstance Win32_Process | Where-Object {
        $isFocusAppApplication = $_.Name -in $applicationNames
        $isFocusAppHost = $_.Name -in @('dotnet.exe', 'powershell.exe') -and
            $_.CommandLine -match $projectPattern

        $isFocusAppApplication -or $isFocusAppHost
    }
}

$desktopProcesses = Get-FocusAppProcesses | Where-Object { $_.Name -eq 'FocusApp.Desktop.exe' }
foreach ($desktop in $desktopProcesses)
{
    $process = Get-Process -Id $desktop.ProcessId -ErrorAction SilentlyContinue
    if ($null -ne $process)
    {
        [void]$process.CloseMainWindow()
    }
}

Start-Sleep -Seconds 1

# Stop Service first so Agent can run its proxy recovery path after the disconnect.
$serviceProcesses = Get-FocusAppProcesses | Where-Object {
    $_.Name -eq 'FocusApp.Service.exe' -or
    ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -match 'FocusApp\.Service\.csproj')
}
foreach ($service in $serviceProcesses)
{
    Stop-Process -Id $service.ProcessId -Force -ErrorAction SilentlyContinue
}

Start-Sleep -Seconds 2

$remainingProcesses = Get-FocusAppProcesses
foreach ($process in $remainingProcesses)
{
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
}

Write-Host 'FocusApp Desktop, Service, and Agent have been closed.'
