#requires -Version 5.1
<#
.SYNOPSIS
  Publishes DiskLore for one or more runtime identifiers and packages the results into dist/.

.PARAMETER Rid
  Runtime identifiers to build. Default: win-x64, linux-x64, osx-x64, osx-arm64.

.PARAMETER Dotnet
  Path to the dotnet executable. Default: $env:USERPROFILE\.dotnet\dotnet.exe if it exists, else "dotnet".

.NOTES
  A macOS .app zip made on Windows cannot carry the Unix executable bit; build the macOS packages
  with scripts/publish.sh on macOS or Linux (the release workflow does).
#>
[CmdletBinding()]
param(
    [string[]]$Rid = @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64'),
    [string]$Dotnet
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not $Dotnet) {
    $local = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if ($env:USERPROFILE -and (Test-Path $local)) { $Dotnet = $local } else { $Dotnet = 'dotnet' }
}

$props = [xml](Get-Content (Join-Path $root 'Directory.Build.props'))
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'Version not found in Directory.Build.props' }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$project = Join-Path $root 'src\DiskLore.App\DiskLore.App.csproj'
$icns = Join-Path $root 'src\DiskLore.App\Assets\disklore.icns'

foreach ($r in $Rid) {
    Write-Host "== Publishing $r ($version)"
    $out = Join-Path $dist $r
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    & $Dotnet publish $project -c Release -r $r -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $r" }
    Get-ChildItem $out -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

    $stage = Join-Path $dist "stage-$r"
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory $stage | Out-Null

    if ($r -like 'win-*') {
        Copy-Item (Join-Path $out '*') $stage -Recurse
        Copy-Item (Join-Path $root 'LICENSE') $stage
        $zip = Join-Path $dist "DiskLore-$version-$r.zip"
        if (Test-Path $zip) { Remove-Item $zip }
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    }
    elseif ($r -like 'linux-*') {
        $dir = Join-Path $stage 'DiskLore'
        New-Item -ItemType Directory $dir | Out-Null
        Copy-Item (Join-Path $out '*') $dir -Recurse
        Copy-Item (Join-Path $root 'LICENSE') $dir
        $tar = Join-Path $dist "DiskLore-$version-$r.tar.gz"
        if (Test-Path $tar) { Remove-Item $tar }
        & tar -czf $tar -C $stage DiskLore
        if ($LASTEXITCODE -ne 0) { throw "tar failed for $r" }
    }
    elseif ($r -like 'osx-*') {
        $app = Join-Path $stage 'DiskLore.app'
        $macos = Join-Path $app 'Contents\MacOS'
        $res = Join-Path $app 'Contents\Resources'
        New-Item -ItemType Directory $macos, $res | Out-Null
        Copy-Item (Join-Path $out '*') $macos -Recurse
        $iconKey = ''
        if (Test-Path $icns) {
            Copy-Item $icns (Join-Path $res 'DiskLore.icns')
            $iconKey = "  <key>CFBundleIconFile</key><string>DiskLore.icns</string>`n"
        }
        $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>DiskLore</string>
  <key>CFBundleIdentifier</key><string>com.disklore.app</string>
  <key>CFBundleName</key><string>DiskLore</string>
  <key>CFBundleDisplayName</key><string>DiskLore</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
$iconKey</dict>
</plist>
"@
        [System.IO.File]::WriteAllText((Join-Path $app 'Contents\Info.plist'), $plist, (New-Object System.Text.UTF8Encoding($false)))
        Copy-Item (Join-Path $root 'LICENSE') $stage
        $zip = Join-Path $dist "DiskLore-$version-$r.zip"
        if (Test-Path $zip) { Remove-Item $zip }
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    }
    else { throw "Unsupported RID: $r" }

    Remove-Item -Recurse -Force $stage
}

$sums = Get-ChildItem $dist -File | Where-Object { $_.Name -match '^DiskLore-.*\.(zip|tar\.gz)$' } | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
}
[System.IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Done. Packages are in $dist"
Get-ChildItem $dist -File | Format-Table Name, Length -AutoSize
