[CmdletBinding()]
param(
    [ValidateSet('Menu', 'Logout', 'Login', 'Repair', 'Status')]
    [string]$Action = 'Menu'
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Get-CodexRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:CODEX_HOME)) {
        return [System.IO.Path]::GetFullPath($env:CODEX_HOME)
    }

    if ([string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        throw '无法确定用户目录：USERPROFILE 和 CODEX_HOME 都为空。'
    }

    return Join-Path $env:USERPROFILE '.codex'
}

function Find-CodexExecutable {
    $command = Get-Command 'codex.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $command = Get-Command 'codex' -ErrorAction SilentlyContinue
    if ($command -and $command.CommandType -eq 'Application') {
        return $command.Source
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $binRoot = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
        if (Test-Path -LiteralPath $binRoot) {
            $candidate = Get-ChildItem -LiteralPath $binRoot -Filter 'codex.exe' -File -Recurse -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1
            if ($candidate) {
                return $candidate.FullName
            }
        }
    }

    throw '找不到 codex.exe。请确认 Codex Desktop 或 Codex CLI 已安装。'
}

function Set-LegacyProviderCompatibility {
    param([string]$CodexRoot)

    $configPath = Join-Path $CodexRoot 'config.toml'
    if (-not (Test-Path -LiteralPath $CodexRoot)) {
        New-Item -ItemType Directory -Path $CodexRoot -Force | Out-Null
    }

    $original = if (Test-Path -LiteralPath $configPath) {
        [System.IO.File]::ReadAllText($configPath)
    } else {
        ''
    }

    $lineEnding = if ($original.Contains("`r`n")) { "`r`n" } else { "`n" }
    $updated = $original

    # New sessions must use the built-in OpenAI provider. Only change a root-level
    # legacy value; provider-specific tables are left alone until the custom block
    # is replaced below.
    $firstTable = [regex]::Match($updated, '(?m)^[ \t]*\[')
    $rootLength = if ($firstTable.Success) { $firstTable.Index } else { $updated.Length }
    $rootPart = $updated.Substring(0, $rootLength)
    $tablePart = $updated.Substring($rootLength)
    $rootPart = [regex]::Replace(
        $rootPart,
        '(?m)^[ \t]*model_provider[ \t]*=[ \t]*["'']custom["''][ \t]*$',
        'model_provider = "openai"'
    )
    $updated = $rootPart + $tablePart

    # Remove any stale custom provider definition, especially old third-party URLs
    # or bearer tokens, and replace it with a safe alias backed by current ChatGPT auth.
    $managedCommentPattern = '(?m)^[ \t]*# (?:Compatibility alias for legacy sessions saved with model_provider = "custom"\.|New sessions continue to use the built-in OpenAI provider\.)[ \t]*\r?\n?'
    $updated = [regex]::Replace($updated, $managedCommentPattern, '')
    $customBlockPattern = '(?ms)^[ \t]*\[model_providers\.custom\][ \t]*\r?\n.*?(?=^[ \t]*\[|\z)'
    $updated = [regex]::Replace($updated, $customBlockPattern, '')
    $updated = $updated.TrimEnd("`r", "`n")

    $compatibilityBlock = @(
        ''
        ''
        '# Compatibility alias for legacy sessions saved with model_provider = "custom".'
        '# New sessions continue to use the built-in OpenAI provider.'
        '[model_providers.custom]'
        'name = "OpenAI (legacy session compatibility)"'
        'base_url = "https://chatgpt.com/backend-api/codex"'
        'wire_api = "responses"'
        'requires_openai_auth = true'
        'supports_websockets = true'
        ''
    ) -join $lineEnding
    $updated += $compatibilityBlock

    if ($updated -eq $original) {
        Write-Host "兼容配置已存在：$configPath" -ForegroundColor Green
        return $configPath
    }

    if (Test-Path -LiteralPath $configPath) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $backupPath = Join-Path $CodexRoot "config.toml.auth-helper-$stamp.bak"
        Copy-Item -LiteralPath $configPath -Destination $backupPath
        Write-Host "已备份配置：$backupPath" -ForegroundColor DarkGray
    }

    $tempPath = Join-Path $CodexRoot ("config.toml.tmp-" + [guid]::NewGuid().ToString('N'))
    try {
        $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText($tempPath, $updated, $utf8NoBom)
        Move-Item -LiteralPath $tempPath -Destination $configPath -Force
    } finally {
        if (Test-Path -LiteralPath $tempPath) {
            Remove-Item -LiteralPath $tempPath -Force
        }
    }

    Write-Host "已修复旧会话兼容配置：$configPath" -ForegroundColor Green
    return $configPath
}

function Show-LoginStatus {
    param([string]$CodexExe)
    Write-Step '当前登录状态'
    & $CodexExe login status
    return $LASTEXITCODE
}

function Test-LegacyProvider {
    param([string]$CodexExe)

    Write-Step '验证旧会话 provider'
    $rawReport = & $CodexExe doctor --json -c 'model_provider="custom"' 2>$null
    try {
        $report = ($rawReport -join "`n") | ConvertFrom-Json
        $configOk = $report.checks.'config.load'.status -eq 'ok'
        $reachable = $report.checks.'network.provider_reachability'.status -eq 'ok'
        $websocket = $report.checks.'network.websocket_reachability'.status -in @('ok', 'warning')
        if ($configOk -and $reachable -and $websocket) {
            Write-Host '验证通过：旧会话 provider 可加载，OpenAI 连接正常。' -ForegroundColor Green
            return $true
        }

        Write-Warning '配置已修复，但联网验证未完全通过。请检查网络/VPN 后重试 Repair。'
        return $false
    } catch {
        Write-Warning "无法解析 Codex 诊断结果：$($_.Exception.Message)"
        return $false
    }
}

function Invoke-Repair {
    param(
        [string]$CodexRoot,
        [string]$CodexExe,
        [switch]$Verify
    )

    Write-Step '保护新旧会话配置'
    $null = Set-LegacyProviderCompatibility -CodexRoot $CodexRoot
    if ($Verify) {
        $null = Test-LegacyProvider -CodexExe $CodexExe
    }
}

function Select-Action {
    Write-Host ''
    Write-Host 'Codex / ChatGPT 登录助手' -ForegroundColor Yellow
    Write-Host '  1. 退出登录（默认）'
    Write-Host '  2. 重新登录'
    Write-Host '  3. 修复并验证旧会话'
    Write-Host '  4. 查看登录状态'
    $choice = Read-Host '请选择 [1-4]'
    switch ($choice) {
        '2' { return 'Login' }
        '3' { return 'Repair' }
        '4' { return 'Status' }
        default { return 'Logout' }
    }
}

try {
    $codexRoot = Get-CodexRoot
    $codexExe = Find-CodexExecutable
    if ($Action -eq 'Menu') {
        $Action = Select-Action
    }

    switch ($Action) {
        'Logout' {
            Invoke-Repair -CodexRoot $codexRoot -CodexExe $codexExe
            Write-Step '退出 ChatGPT 登录'
            & $codexExe logout
            if ($LASTEXITCODE -ne 0) {
                throw "codex logout 失败，退出码：$LASTEXITCODE"
            }
            Write-Host '已退出登录。历史会话和兼容配置均已保留。' -ForegroundColor Green
            Write-Host '重新登录：再次运行脚本并选择 2，或使用 -Action Login。'
        }
        'Login' {
            Invoke-Repair -CodexRoot $codexRoot -CodexExe $codexExe
            Write-Step '登录 ChatGPT'
            & $codexExe login
            if ($LASTEXITCODE -ne 0) {
                throw "codex login 失败或被取消，退出码：$LASTEXITCODE"
            }
            Invoke-Repair -CodexRoot $codexRoot -CodexExe $codexExe -Verify
            Write-Host '登录完成。若 Codex Desktop 仍显示旧状态，请完全退出并重新打开应用。' -ForegroundColor Green
        }
        'Repair' {
            Invoke-Repair -CodexRoot $codexRoot -CodexExe $codexExe -Verify
        }
        'Status' {
            $null = Show-LoginStatus -CodexExe $codexExe
        }
    }
} catch {
    Write-Host "`n操作失败：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
