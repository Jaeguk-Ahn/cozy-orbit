#!/usr/bin/env bash
# Cozy Orbit (macOS) — 배포본 만들기. macOS 에서 실행 (sips·iconutil·codesign·ditto 사용).
#   scripts/publish-mac.sh               (테스트 → 빌드 → .app → zip)
#   scripts/publish-mac.sh --skip-tests
#
# 결과 (artifacts/, .gitignore 에 있음):
#   CozyOrbit-<버전>-osx-arm64.zip   Apple Silicon 용 Cozy Orbit.app (.NET 런타임 포함)
#   CozyOrbit-<버전>-osx-x64.zip     Intel 용
# 서명은 임시 서명(ad-hoc)뿐이라 처음 열 때 Gatekeeper 가 막는다 (Finder 에서 우클릭 → 열기).
# 버전은 src/CozyOrbit.App/CozyOrbit.App.csproj 의 <Version> (Windows 판 scripts/publish.ps1 과 같은 csproj — 릴리스 태그 release/v<버전> 하나로 둘 다 나간다).
set -euo pipefail
cd "$(dirname "$0")/.."

skip_tests=0
[[ "${1:-}" == "--skip-tests" ]] && skip_tests=1

csproj=src/CozyOrbit.App/CozyOrbit.App.csproj
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$csproj" | head -1)
[[ -n "$version" ]] || { echo "$csproj 에서 <Version> 을 찾지 못했어요" >&2; exit 1; }
app_name="Cozy Orbit"
exe_name=CozyOrbit
echo "Cozy Orbit (macOS) $version"

if [[ $skip_tests -eq 0 ]]; then
  dotnet test tests/CozyOrbit.Core.Tests -c Release --nologo
fi

out=artifacts
rm -rf "$out/publish-osx-"* "$out/stage-osx-"* "$out"/CozyOrbit-*-osx-*.zip
mkdir -p "$out"

# 앱 아이콘: Assets/app.png(256×256 도트) 를 .icns 로. 도트가 번지지 않게 최근접 배율이 되는 크기만 쓴다 (256 보다 크게 늘리지 않음)
icon_dir=$(mktemp -d)
trap 'rm -rf "$icon_dir"' EXIT
iconset="$icon_dir/CozyOrbit.iconset"
mkdir "$iconset"
icon_png=src/CozyOrbit.App/Assets/app.png
for size in 16 32 128 256; do
  sips -z $size $size "$icon_png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  if [[ $((size * 2)) -le 256 ]]; then
    sips -z $((size * 2)) $((size * 2)) "$icon_png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  fi
done
iconutil -c icns "$iconset" -o "$icon_dir/CozyOrbit.icns"

publish() {
  local rid=$1
  local dir="$out/publish-$rid"
  # CozyUpdateFlavor: 자동 업데이트가 받을 배포판 (매니페스트의 키). 없으면 개발 빌드로 보고 설치하지 않는다
  dotnet publish "$csproj" -c Release -r "$rid" --self-contained true --nologo -o "$dir" \
    -p:UseAppHost=true -p:DebugType=none -p:GenerateDocumentationFile=false \
    -p:CozyUpdateFlavor="$rid"

  local stage="$out/stage-$rid"
  local app="$stage/$app_name.app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$dir/." "$app/Contents/MacOS/"
  cp "$icon_dir/CozyOrbit.icns" "$app/Contents/Resources/"
  # LSUIElement false: 보통 앱처럼 독·Cmd+Tab 에 나온다 (창을 닫아도 트레이 아이콘으로 계속 항해)
  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>$app_name</string>
    <key>CFBundleDisplayName</key><string>$app_name</string>
    <key>CFBundleIdentifier</key><string>com.jaegukahn.cozyorbit</string>
    <key>CFBundleExecutable</key><string>$exe_name</string>
    <key>CFBundleIconFile</key><string>CozyOrbit</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>LSApplicationCategoryType</key><string>public.app-category.casual-games</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>LSUIElement</key><false/>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
  chmod +x "$app/Contents/MacOS/$exe_name"
  # Apple Silicon 은 서명 없는 실행 파일을 거부하므로 임시 서명(ad-hoc)은 꼭 한다
  codesign --force --deep --sign - "$app"

  local zip="$out/CozyOrbit-$version-$rid.zip"
  ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
  rm -rf "$stage"
  echo "$rid: $(du -sh "$zip" | cut -f1)  $(shasum -a 256 "$zip" | cut -d' ' -f1)"
}

publish osx-arm64
publish osx-x64

echo "완료: $out"
