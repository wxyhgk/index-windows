[CmdletBinding()]
param(
    [string]$InstallDirectory,
    [switch]$DesktopShortcut,
    [switch]$NoShortcuts,
    [switch]$NoRegistry,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$bundleDirectory = Split-Path -Parent $PSCommandPath
$sourceDirectory = Join-Path $bundleDirectory 'app'
$sourceExecutable = Join-Path $sourceDirectory 'Index.exe'
if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
    throw "The bundled application is incomplete: $sourceExecutable"
}

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = Join-Path $env:LOCALAPPDATA 'Programs\Index'
}
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$installParent = Split-Path -Parent $InstallDirectory
if ([string]::IsNullOrWhiteSpace($installParent) -or $InstallDirectory -eq [IO.Path]::GetPathRoot($InstallDirectory)) {
    throw "Refusing unsafe install directory: $InstallDirectory"
}

$stageDirectory = "$InstallDirectory.installing-$PID"
$backupDirectory = "$InstallDirectory.backup-$PID"
foreach ($path in @($stageDirectory, $backupDirectory)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $installParent -Force | Out-Null
New-Item -ItemType Directory -Path $stageDirectory | Out-Null

try {
    Copy-Item -Path (Join-Path $sourceDirectory '*') -Destination $stageDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $bundleDirectory 'Uninstall-Index.ps1') -Destination $stageDirectory -Force

    $installedProcesses = @(
        Get-Process -Name Index -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($InstallDirectory, [StringComparison]::OrdinalIgnoreCase) }
    )
    $installedProcesses | Stop-Process -Force
    foreach ($process in $installedProcesses) {
        Wait-Process -Id $process.Id -Timeout 15 -ErrorAction SilentlyContinue
    }
    $exitDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $stillRunning = @(
            Get-Process -Name Index -ErrorAction SilentlyContinue |
                Where-Object { $_.Path -and $_.Path.StartsWith($InstallDirectory, [StringComparison]::OrdinalIgnoreCase) }
        )
        if ($stillRunning.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $exitDeadline)
    if ($stillRunning.Count -gt 0) {
        throw "Index did not exit before the upgrade: $($stillRunning.Id -join ', ')"
    }
    Start-Sleep -Milliseconds 500

    if (Test-Path -LiteralPath $InstallDirectory) {
        Move-Item -LiteralPath $InstallDirectory -Destination $backupDirectory
    }
    Move-Item -LiteralPath $stageDirectory -Destination $InstallDirectory

    $installedExecutable = Join-Path $InstallDirectory 'Index.exe'
    if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
        throw 'The installed executable is missing after replacement.'
    }

    if (-not $NoShortcuts) {
        $shell = New-Object -ComObject WScript.Shell
        $startMenuDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'Index'
        New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
        $startMenuShortcut = $shell.CreateShortcut((Join-Path $startMenuDirectory 'Index.lnk'))
        $startMenuShortcut.TargetPath = $installedExecutable
        $startMenuShortcut.WorkingDirectory = $InstallDirectory
        $startMenuShortcut.IconLocation = "$installedExecutable,0"
        $startMenuShortcut.Save()

        if ($DesktopShortcut) {
            $desktopShortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Index.lnk'))
            $desktopShortcut.TargetPath = $installedExecutable
            $desktopShortcut.WorkingDirectory = $InstallDirectory
            $desktopShortcut.IconLocation = "$installedExecutable,0"
            $desktopShortcut.Save()
        }
    }

    if (-not $NoRegistry) {
        $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7E74AC57-9A61-4CE8-A2A8-9D9B60D80393}_is1'
        $uninstallScript = Join-Path $InstallDirectory 'Uninstall-Index.ps1'
        $powershell = Join-Path $PSHOME 'powershell.exe'
        $uninstallCommand = "`"$powershell`" -NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`""
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($installedExecutable).ProductVersion
        if ([string]::IsNullOrWhiteSpace($version)) { $version = '0.0.0' }
        New-Item -Path $uninstallKey -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'Index' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $version -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'Index' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $InstallDirectory -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "$installedExecutable,0" -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name QuietUninstallString -Value "$uninstallCommand -Quiet" -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
    }

    if (Test-Path -LiteralPath $backupDirectory) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }

    Write-Host "Index was installed to: $InstallDirectory"
    if (-not $NoLaunch) {
        Start-Process -FilePath $installedExecutable -WorkingDirectory $InstallDirectory
    }
} catch {
    if ((Test-Path -LiteralPath $backupDirectory) -and -not (Test-Path -LiteralPath $InstallDirectory)) {
        Move-Item -LiteralPath $backupDirectory -Destination $InstallDirectory
    }
    throw
} finally {
    if (Test-Path -LiteralPath $stageDirectory) {
        Remove-Item -LiteralPath $stageDirectory -Recurse -Force
    }
}
