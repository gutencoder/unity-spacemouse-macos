#!/bin/sh
# Builds the native SpaceMouse bridge into the editor plugin next door.
#
# Universal on purpose: the editor runs arm64 on Apple silicon, but an Intel
# machine has to be able to open the same project without rebuilding.
set -e
here=$(cd "$(dirname "$0")" && pwd)
bundle="$here/../Plugins/SpaceMouse.bundle"

rm -rf "$bundle"
mkdir -p "$bundle/Contents/MacOS"

cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>SpaceMouse</string>
  <key>CFBundleIdentifier</key><string>com.gutenbrook.spacemouse</string>
  <key>CFBundleExecutable</key><string>SpaceMouse</string>
  <key>CFBundlePackageType</key><string>BNDL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
</dict></plist>
PLIST

clang++ -std=c++17 -ObjC++ -bundle -O2 \
  -arch arm64 -arch x86_64 \
  -mmacosx-version-min=12.0 \
  -o "$bundle/Contents/MacOS/SpaceMouse" \
  "$here/SpaceMouse.mm" \
  -F/Library/Frameworks \
  -framework 3DconnexionNavlib -framework 3DconnexionClient -framework Foundation

echo "gebaut: $bundle"
lipo -archs "$bundle/Contents/MacOS/SpaceMouse"
