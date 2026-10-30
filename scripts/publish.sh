#!/usr/bin/env bash
# Publishes DiskLore for one or more runtime identifiers and packages the results into dist/.
# Usage: scripts/publish.sh [rid ...]    (default: win-x64 linux-x64 osx-x64 osx-arm64)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dist="$root/dist"
project="$root/src/DiskLore.App/DiskLore.App.csproj"
icns="$root/src/DiskLore.App/Assets/disklore.icns"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n1)"
[ -n "$version" ] || { echo "Version not found in Directory.Build.props" >&2; exit 1; }

if [ "$#" -gt 0 ]; then rids=("$@"); else rids=(win-x64 linux-x64 osx-x64 osx-arm64); fi
mkdir -p "$dist"

sha256() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$@"; else shasum -a 256 "$@"; fi
}

for rid in "${rids[@]}"; do
  echo "== Publishing $rid ($version)"
  out="$dist/$rid"
  stage="$dist/stage-$rid"
  rm -rf "$out" "$stage"
  dotnet publish "$project" -c Release -r "$rid" -o "$out"
  rm -f "$out"/*.pdb
  mkdir -p "$stage"

  case "$rid" in
    win-*)
      cp -R "$out"/. "$stage"/
      cp "$root/LICENSE" "$stage"/
      rm -f "$dist/DiskLore-$version-$rid.zip"
      (cd "$stage" && zip -qr "$dist/DiskLore-$version-$rid.zip" .)
      ;;
    linux-*)
      mkdir -p "$stage/DiskLore"
      cp -R "$out"/. "$stage/DiskLore"/
      cp "$root/LICENSE" "$stage/DiskLore"/
      chmod +x "$stage/DiskLore/DiskLore"
      tar -czf "$dist/DiskLore-$version-$rid.tar.gz" -C "$stage" DiskLore
      ;;
    osx-*)
      app="$stage/DiskLore.app"
      mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
      cp -R "$out"/. "$app/Contents/MacOS"/
      chmod +x "$app/Contents/MacOS/DiskLore"
      icon_key=""
      if [ -f "$icns" ]; then
        cp "$icns" "$app/Contents/Resources/DiskLore.icns"
        icon_key="  <key>CFBundleIconFile</key><string>DiskLore.icns</string>"
      fi
      cat > "$app/Contents/Info.plist" <<PLIST
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
$icon_key
</dict>
</plist>
PLIST
      cp "$root/LICENSE" "$stage"/
      rm -f "$dist/DiskLore-$version-$rid.zip"
      # zip keeps the executable bit; run from the stage dir so the archive root is DiskLore.app
      (cd "$stage" && zip -qry "$dist/DiskLore-$version-$rid.zip" .)
      ;;
    *)
      echo "Unsupported RID: $rid" >&2; exit 1 ;;
  esac
  rm -rf "$stage"
done

(cd "$dist" && sha256 DiskLore-*.zip DiskLore-*.tar.gz 2>/dev/null > SHA256SUMS.txt || true)
echo "Done. Packages are in $dist"
ls -l "$dist"
