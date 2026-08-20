# Builds a Windows setup.exe for the Belt Tensioner app (Inno Setup).
#
#   .\build-installer.ps1              Publish (self-contained) + compile installer
#   .\build-installer.ps1 -SkipPublish Reuse existing publish\BeltTensioner output
#
# Output: publish\BeltTensionerSetup-<version>.exe
# Requires (build machine only): .NET 8 SDK, CMake, VS2022 C++ tools, Inno Setup 6.
param(
    [switch]$SkipPublish,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

$proj = Join-Path $PSScriptRoot 'BeltTensionTest.WPF\BeltTensionTest.WPF.csproj'
$out  = Join-Path $PSScriptRoot 'publish\BeltTensioner'
$iss  = Join-Path $PSScriptRoot 'installer\BeltTensioner.iss'

# Version comes from the csproj so installer and app never drift.
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $proj" }

if (-not $SkipPublish) {
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    dotnet publish $proj -c $Configuration -r win-x64 --self-contained true -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

foreach ($f in 'BeltTensionTest.WPF.exe', 'XR_APILAYER_NOVELTY_monoxr.dll', 'MonoXR.json') {
    if (-not (Test-Path (Join-Path $out $f))) { throw "Missing $f in publish output at $out" }
}

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
          "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
          "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it: winget install JRSoftware.InnoSetup' }

& $iscc "/DAppVersion=$version" "/DSourceDir=$out" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Join-Path $PSScriptRoot "publish\BeltTensionerSetup-$version.exe"
Write-Host "Installer ready: $setup"
