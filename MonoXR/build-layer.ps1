# Configures and builds the native OpenXR API layer (Release, x64).
# Requires CMake and Visual Studio 2022 (MSVC).
$ErrorActionPreference = 'Stop'
$layer = Join-Path $PSScriptRoot 'layer'
cmake -S $layer -B (Join-Path $layer 'build') -G "Visual Studio 17 2022" -A x64
cmake --build (Join-Path $layer 'build') --config Release
Write-Host "`nBuilt: $layer\build\Release\XR_APILAYER_NOVELTY_monoxr.dll"
Write-Host "Manifest: $layer\build\Release\MonoXR.json"
