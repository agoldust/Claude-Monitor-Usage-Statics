# Publishes Claude Usage Monitor as a self-contained win-x64 app and builds the Inno Setup installer.
# Output: publish\installer\ClaudeUsageMonitor-Setup-<version>.exe
# Needs the .NET 8 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'ClaudeUsageMonitor\ClaudeUsageMonitor.csproj'
$publishDir = Join-Path $root 'publish\win-x64'
$script = Join-Path $PSScriptRoot 'ClaudeUsageMonitor.iss'

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $project." }

$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup' }

Write-Host "Publishing $version to $publishDir"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:SatelliteResourceLanguages=en -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

Write-Host "Building installer with $iscc"
& $iscc "/DAppVersion=$version" "/DPublishDir=$publishDir" $script
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)." }
