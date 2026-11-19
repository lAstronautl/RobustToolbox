#!/usr/bin/env bash
# Packages the managed engine (Robust.Client + dependencies + Resources/ + runtimeconfig) for the Android launcher.
# The .NET runtime itself is not included: the app downloads the Mono runtime of the version in runtimeconfig.
# The launcher downloads this zip from GitHub releases (tag android-engine-v<version>).
#
# Usage: package-engine.sh [output-dir]
# Env:   KEEP_NATIVES=linux-x64  keep desktop natives in the zip root (for desktop testing only)
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
OUT="${1:-$ROOT/release}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/MSBuild/Robust.Engine.Version.props")
echo "Engine version: $VERSION"

dotnet publish "$ROOT/Robust.Client/Robust.Client.csproj" -c Release \
  -p:FullRelease=True -p:RobustILLink=false -p:PublishTrimmed=false \
  --no-self-contained -o "$STAGE/publish" -nologo -v quiet

mkdir -p "$STAGE/pkg"
# Managed assemblies only: Android natives ship inside the APK.
find "$STAGE/publish" -maxdepth 1 -name '*.dll' -exec cp {} "$STAGE/pkg/" \;
if [ -n "${KEEP_NATIVES:-}" ] && [ -d "$STAGE/publish/runtimes/$KEEP_NATIVES/native" ]; then
  cp "$STAGE/publish/runtimes/$KEEP_NATIVES/native/"* "$STAGE/pkg/"
fi
# Desktop-only assemblies that can never work on Android.
rm -f "$STAGE/pkg/Robust.Client.WebView.dll" "$STAGE/pkg/Xilium.CefGlue.dll"
cp -r "$ROOT/Resources" "$STAGE/pkg/Resources"

# Tells the game process which .NET runtime version to start for this engine.
cp "$STAGE/publish/Robust.Client.runtimeconfig.json" "$STAGE/pkg/"
echo "$VERSION" > "$STAGE/pkg/engine-version.txt"

mkdir -p "$OUT"
ZIP="$OUT/Robust.Client_android.zip"
rm -f "$ZIP"
(cd "$STAGE/pkg" && zip -qr -9 "$ZIP" .)
sha256sum "$ZIP" | awk '{print toupper($1)}' > "$ZIP.sha256"
echo "$VERSION" > "$OUT/engine-version.txt"
ls -la "$OUT"
