[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('x64')]
    [string]$Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $PSCommandPath
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..'))
$projectPath = Join-Path $repositoryRoot 'src\Index\Index.csproj'
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\win-$Architecture"
$installerDirectory = Join-Path $repositoryRoot 'artifacts\installer'
$installerScript = Join-Path $repositoryRoot 'installer\Index.iss'

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $Version = [string]$project.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use major.minor.patch format; received '$Version'."
}

function Assert-WorkspacePath([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPrefix = $repositoryRoot.TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $fullPath"
    }
    return $fullPath
}

$publishDirectory = Assert-WorkspacePath $publishDirectory
$installerDirectory = Assert-WorkspacePath $installerDirectory
foreach ($directory in @($publishDirectory, $installerDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory | Out-Null
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime "win-$Architecture" `
    --self-contained true `
    --output $publishDirectory `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$compilerCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$compiler = $compilerCandidates | Select-Object -First 1
if (-not $compiler) {
    throw 'Inno Setup 6 is required. Install it with: winget install --id JRSoftware.InnoSetup --exact'
}

function Invoke-InnoSetup([string]$OutputDirectory) {
    $arguments = @(
        "/DAppVersion=$Version"
        "/DSourceDir=$publishDirectory"
        "/DOutputDir=$OutputDirectory"
    )
    $arguments += $installerScript

    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }
}

function Write-Sha256File([System.IO.FileInfo]$File) {
    $hash = Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256
    $checksumPath = "$($File.FullName).sha256"
    Set-Content -LiteralPath $checksumPath -Encoding ascii -NoNewline `
        -Value "$($hash.Hash.ToLowerInvariant())  $($File.Name)"
    return $hash.Hash.ToLowerInvariant()
}

$singleFileName = "Index-Setup-$Version-win-$Architecture.exe"
$singleFilePath = Join-Path $installerDirectory $singleFileName
Invoke-InnoSetup -OutputDirectory $installerDirectory
if (-not (Test-Path -LiteralPath $singleFilePath -PathType Leaf)) {
    throw "Single-file installer was not produced: $singleFilePath"
}
$singleFile = Get-Item -LiteralPath $singleFilePath
$singleFileHash = Write-Sha256File -File $singleFile

$noTempPackageName = "Index-Setup-$Version-win-$Architecture-no-temp"
$noTempDirectory = Assert-WorkspacePath (Join-Path $installerDirectory $noTempPackageName)
New-Item -ItemType Directory -Path $noTempDirectory | Out-Null
$noTempAppDirectory = Join-Path $noTempDirectory 'app'
New-Item -ItemType Directory -Path $noTempAppDirectory | Out-Null
Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $noTempAppDirectory -Recurse -Force
Copy-Item -Path (Join-Path $repositoryRoot 'installer\no-temp\*') -Destination $noTempDirectory -Force

$noTempReadme = @'
Index no-TEMP installer

Use this package if the normal single-file installer cannot create an is-*.tmp directory.

1. Extract every file from this ZIP to the same directory.
2. Run Install-Index.cmd. Do not run it from inside the ZIP preview.
3. Index is installed for the current user and added to the Start menu.

This package copies Index directly from the extracted directory and does not use Inno Setup or
create an is-*.tmp directory under %TEMP%. User data is preserved when Index is uninstalled.
'@
Set-Content -LiteralPath (Join-Path $noTempDirectory 'README.txt') -Encoding utf8 -Value $noTempReadme

$noTempPayloadFiles = @(Get-ChildItem -LiteralPath $noTempDirectory -File | Sort-Object Name)
$noTempChecksums = foreach ($file in $noTempPayloadFiles) {
    $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($file.Name)"
}
Set-Content -LiteralPath (Join-Path $noTempDirectory 'SHA256SUMS.txt') `
    -Encoding ascii -Value $noTempChecksums

$noTempZipPath = Join-Path $installerDirectory "$noTempPackageName.zip"
Compress-Archive -Path (Join-Path $noTempDirectory '*') -DestinationPath $noTempZipPath -CompressionLevel Optimal
$noTempZip = Get-Item -LiteralPath $noTempZipPath
$noTempZipHash = Write-Sha256File -File $noTempZip

Write-Host "Single-file installer: $($singleFile.FullName)"
Write-Host "SHA256:               $singleFileHash"
Write-Host "No-TEMP install ZIP:  $($noTempZip.FullName)"
Write-Host "SHA256:               $noTempZipHash"
