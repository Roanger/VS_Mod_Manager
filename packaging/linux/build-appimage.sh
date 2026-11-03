#!/usr/bin/env bash
# Builds VSModManager-x86_64.AppImage and VSModManager-linux-x64.tar.gz into ./dist.
# Run from the repository root on Linux (the GitHub workflow does this on every tag).
set -euo pipefail

VERSION="${1:-dev}"
ROOT="$(pwd)"
DIST="$ROOT/dist"
WORK="$ROOT/build-linux"
APPDIR="$WORK/AppDir"

rm -rf "$WORK"
mkdir -p "$DIST" "$APPDIR/usr/lib/vsmodmanager" "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/256x256/apps"

# 1. AppImage: a normal self-contained build (no single-file extraction needed).
dotnet publish VSModManager.csproj -c Release -r linux-x64 --self-contained true \
  -p:DebugType=none -o "$APPDIR/usr/lib/vsmodmanager"

cp packaging/linux/AppRun "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/lib/vsmodmanager/VSModManager"
cp packaging/linux/vsmodmanager.desktop "$APPDIR/vsmodmanager.desktop"
cp packaging/linux/vsmodmanager.desktop "$APPDIR/usr/share/applications/"
cp Assets/icon.png "$APPDIR/vsmodmanager.png"
cp Assets/icon.png "$APPDIR/usr/share/icons/hicolor/256x256/apps/vsmodmanager.png"
ln -sf vsmodmanager.png "$APPDIR/.DirIcon"

TOOL="$WORK/appimagetool"
curl -fsSL -o "$TOOL" \
  https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x "$TOOL"
# Extract-and-run so the build machine doesn't need FUSE.
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" --no-appstream "$APPDIR" "$DIST/VSModManager-x86_64.AppImage"

# 2. tar.gz: one self-contained executable plus the icon and launcher.
TARDIR="$WORK/VSModManager-$VERSION-linux-x64"
mkdir -p "$TARDIR"
dotnet publish VSModManager.csproj -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$TARDIR"
chmod +x "$TARDIR/VSModManager"
cp Assets/icon.png "$TARDIR/vsmodmanager.png"
cp packaging/linux/vsmodmanager.desktop "$TARDIR/"
cp LICENSE "$TARDIR/"
tar -C "$WORK" -czf "$DIST/VSModManager-linux-x64.tar.gz" "$(basename "$TARDIR")"

ls -la "$DIST"
