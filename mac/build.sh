#!/bin/bash
# Builds mac/build/YourPetKalo.app (Apple Silicon + Intel). Needs Xcode or the Command Line Tools:
#   xcode-select --install
set -euo pipefail
cd "$(dirname "$0")"

APP=build/YourPetKalo.app
rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

for arch in arm64 x86_64; do
  swiftc -O -swift-version 5 -target "$arch-apple-macos12" \
    -framework AppKit -o "build/YourPetKalo-$arch" Sources/*.swift
done
lipo -create build/YourPetKalo-arm64 build/YourPetKalo-x86_64 -output "$APP/Contents/MacOS/YourPetKalo"
rm build/YourPetKalo-arm64 build/YourPetKalo-x86_64

cp Info.plist "$APP/Contents/Info.plist"
cp ../sounds/meow.wav ../sounds/sad.wav ../sounds/mrrp.wav "$APP/Contents/Resources/" 2>/dev/null || true

# ad-hoc signature so it runs on Apple Silicon
codesign --force --deep -s - "$APP"
echo "Built $APP"
