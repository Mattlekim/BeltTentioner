# Builds the installer and publishes it to the website, where the app's updater finds it.
#
#   $env:BBT_RELEASE_KEY = '<the BBT_RELEASE_KEY secret>'
#   .\publish-release.ps1 -Notes "Fixes the overlay recenter key."
#   .\publish-release.ps1 -NotesFile release-notes.txt
#   .\publish-release.ps1 -SkipBuild        Publish the installer already in publish\
#
# The version comes from <Version> in BeltTensionTest.WPF.csproj - bump it before publishing.
# Every version stays on the server (riddlersoftgames.co.uk/belttentioner.html#downloads); the
# newest one is what installed copies are offered on startup, every two days and from
# Help > Check for Updates. Publishing a version that already exists replaces its installer.
param(
    [string]$Notes = '',
    [string]$NotesFile,
    [switch]$SkipBuild,
    [string]$Site = 'https://riddlersoftgames.co.uk',
    [int]$ChunkMB = 4
)
$ErrorActionPreference = 'Stop'

$key = $env:BBT_RELEASE_KEY
if (-not $key) { throw 'Set $env:BBT_RELEASE_KEY to the release key first (the BBT_RELEASE_KEY GitHub secret).' }
if ($NotesFile) { $Notes = (Get-Content $NotesFile -Raw).Trim() }

$proj = Join-Path $PSScriptRoot 'BeltTensionTest.WPF\BeltTensionTest.WPF.csproj'
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $proj" }

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-installer.ps1')
    if ($LASTEXITCODE) { throw "build-installer.ps1 failed ($LASTEXITCODE)" }
}

$setup = Join-Path $PSScriptRoot "publish\BeltTensionerSetup-$version.exe"
if (-not (Test-Path $setup)) { throw "Installer not found: $setup" }
$sha  = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $setup).Length
Write-Host "Publishing $version ($([math]::Round($size / 1MB, 1)) MB, sha256 $sha)"

$api     = "$Site/api/belttentioner/upload.php"
$headers = @{ Authorization = "Bearer $key" }
$id      = -join ((48..57) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
$chunk   = $ChunkMB * 1MB

$fs = [System.IO.File]::OpenRead($setup)
try {
    $buf = New-Object byte[] $chunk
    $index = 0
    while (($n = $fs.Read($buf, 0, $chunk)) -gt 0) {
        $body = if ($n -eq $chunk) { $buf } else { $buf[0..($n - 1)] }
        for ($try = 1; ; $try++) {
            try {
                Invoke-RestMethod -Method Post -Uri "${api}?step=chunk&upload=$id&index=$index" -Headers $headers `
                    -ContentType 'application/octet-stream' -Body $body | Out-Null
                break
            } catch { if ($try -ge 4) { throw }; Start-Sleep -Seconds (2 * $try) }
        }
        $index++
        Write-Progress -Activity "Uploading $version" -PercentComplete ([math]::Min(100, 100 * $fs.Position / $size))
    }
} finally { $fs.Dispose() }

$commit = @{ upload = $id; chunks = $index; version = $version; file = (Split-Path $setup -Leaf); sha256 = $sha; notes = $Notes } | ConvertTo-Json
$res = Invoke-RestMethod -Method Post -Uri "${api}?step=commit" -Headers $headers -ContentType 'application/json' -Body $commit
if (-not $res.ok) { throw "Publish failed: $($res.error)" }
Write-Host "Published $($res.release.version): $($res.release.url)"
