[CmdletBinding()]
param(
    [string]$InstallDirectory,
    [switch]$NoShortcuts,
    [switch]$NoRegistry,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = Split-Path -Parent $PSCommandPath
}
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$installParent = Split-Path -Parent $InstallDirectory
if ([string]::IsNullOrWhiteSpace($installParent) -or $InstallDirectory -eq [IO.Path]::GetPathRoot($InstallDirectory)) {
    throw "Refusing unsafe install directory: $InstallDirectory"
}

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
    throw "Index did not exit before uninstalling: $($stillRunning.Id -join ', ')"
}
Start-Sleep -Milliseconds 500

if (-not $NoShortcuts) {
    $startMenuDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'Index'
    $startMenuShortcut = Join-Path $startMenuDirectory 'Index.lnk'
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Index.lnk'
    if (Test-Path -LiteralPath $startMenuShortcut) { Remove-Item -LiteralPath $startMenuShortcut -Force }
    if (Test-Path -LiteralPath $startMenuDirectory) { Remove-Item -LiteralPath $startMenuDirectory -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $desktopShortcut) { Remove-Item -LiteralPath $desktopShortcut -Force }
}

if (-not $NoRegistry) {
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7E74AC57-9A61-4CE8-A2A8-9D9B60D80393}_is1'
    if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey -Recurse -Force }
}

if (Test-Path -LiteralPath $InstallDirectory) {
    Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
}
if (-not $Quiet) {
    Write-Host 'Index was uninstalled. User data was preserved.'
}
