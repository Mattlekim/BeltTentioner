# Builds a portable zip of the Belt Tensioner app.
#
# Two flavors:
#   .\publish.ps1                 Self-contained (~66 MB zip). Works on any
#                                 64-bit Windows 10/11 PC with zero installs.
#   .\publish.ps1 -Small          Framework-dependent (~6 MB zip). Target PC
#                                 needs the .NET 8 Desktop Runtime (one-time,
#                                 free; Windows shows a download prompt with
#                                 a link if it is missing).
#
# Both bundle the MonoXR OpenXR layer; the app registers it under HKCU on
# first launch, so no layer install script is needed on target PCs.
# Requires (build machine only): .NET 8 SDK, CMake, VS2022 C++ tools.
param(
    [switch]$Small,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

$proj = Join-Path $PSScriptRoot 'BeltTensionTest.WPF\BeltTensionTest.WPF.csproj'
$out  = Join-Path $PSScriptRoot 'publish\BeltTensioner'
$zip  = Join-Path $PSScriptRoot $(if ($Small) { 'publish\BeltTensioner-win-x64-needs-dotnet8.zip' } else { 'publish\BeltTensioner-win-x64.zip' })
$selfContained = if ($Small) { 'false' } else { 'true' }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish $proj -c $Configuration -r win-x64 --self-contained $selfContained -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

foreach ($f in 'XR_APILAYER_NOVELTY_monoxr.dll', 'MonoXR.json') {
    if (-not (Test-Path (Join-Path $out $f))) { throw "Missing $f in publish output - MonoXR layer was not packaged." }
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out\*" -DestinationPath $zip
Write-Host "Portable zip ready: $zip"
if ($Small) { Write-Host "NOTE: this build requires the .NET 8 Desktop Runtime on the target PC." }
