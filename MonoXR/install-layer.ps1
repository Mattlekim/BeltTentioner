# Registers (or removes) MonoXR as a per-user implicit OpenXR API layer.
# No admin rights needed — writes under HKCU. Re-run after rebuilding the layer
# if the DLL path changes.
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'

$manifest = Join-Path $PSScriptRoot 'layer\build\Release\MonoXR.json'
$key = 'HKCU:\Software\Khronos\OpenXR\1\ApiLayers\Implicit'

if ($Uninstall) {
    if (Test-Path $key) {
        Remove-ItemProperty -Path $key -Name $manifest -ErrorAction SilentlyContinue
    }
    Write-Host "MonoXR layer unregistered."
    return
}

if (-not (Test-Path $manifest)) {
    throw "Layer manifest not found. Build the layer first (see README):`n  $manifest"
}

New-Item -Path $key -Force | Out-Null
# Data 0 = enabled (nonzero = disabled) per the OpenXR loader spec.
New-ItemProperty -Path $key -Name $manifest -PropertyType DWord -Value 0 -Force | Out-Null
Write-Host "MonoXR registered as an implicit OpenXR API layer:`n  $manifest"
Write-Host "It will now load into every OpenXR app. Run install-layer.ps1 -Uninstall to remove."
