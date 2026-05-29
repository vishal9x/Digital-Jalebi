# Copies Addressables build output into Firebase public folder, then deploys hosting.
# Run after: Window > Asset Management > Addressables > Build > New Build > Default Build Script

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$platform = "Android"   # change to iOS if you build for iPhone

$source = Join-Path $root "ServerData\$platform"
$dest = Join-Path $root "public\AssetBundles\$platform"

if (-not (Test-Path $source)) {
    Write-Error "Missing build output: $source`nBuild Addressables for $platform first."
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path "$source\*" -Destination $dest -Force

Write-Host "Copied $(@(Get-ChildItem $dest).Count) files to public/AssetBundles/$platform/"
Write-Host "Deploying to Firebase..."
Set-Location $root
firebase deploy --only hosting
Write-Host ""
Write-Host "Verify in browser:"
Write-Host "  https://digitaljalebiarworkv.web.app/AssetBundles/$platform/catalog_1.0.0.json"
