#!/usr/bin/env bash
# 서명된 업데이트 매니페스트 만들기 — release.yml 의 release 작업이 부른다.
#   scripts/make-update-manifest.sh <태그> <버전> <폴더>             폴더의 zip 세 개(win-x64·osx-arm64·osx-x64)로 만들기
#   scripts/make-update-manifest.sh <태그> <버전> <폴더> --partial   있는 zip 만으로 (로컬 확인용)
#   태그는 git 태그 release/v<버전> — 매니페스트의 "tag" 에도 그대로 적는다 (앱이 GitHub 릴리스의 tag_name 과 견준다).
# 서명 키: 환경 변수 UPDATE_SIGNING_KEY (PEM 내용) 또는 UPDATE_SIGNING_KEY_FILE (PEM 파일 경로). 출력·커밋하지 않는다.
#
# 결과 (같은 폴더):
#   update-manifest.json      {"format":1,"version":"…","tag":"release/v…","assets":{"win-x64":{"name","size","sha256"},…}}
#   update-manifest.json.sig  위 파일 바이트의 ECDSA P-256 / SHA-256 서명 (DER, base64 한 줄)
# 앱(src/CozyOrbit.Core/Infra/UpdateManifest.cs)은 이 서명을 scripts/update-signing.pub.pem 과 같은 공개 키로 검사하고,
# 맞을 때만 zip 의 크기·sha256 을 믿는다.
set -euo pipefail
cd "$(dirname "$0")/.."

tag=${1:?태그 (예: release/v0.2.0)}
version=${2:?버전 (예: 0.2.0)}
dir=${3:?zip 이 있는 폴더}
partial=0
[[ "${4:-}" == "--partial" ]] && partial=1
[[ "$version" =~ ^[0-9]{1,9}\.[0-9]{1,9}\.[0-9]{1,9}$ ]] || { echo "버전 $version 이 X.Y.Z 모양이 아니에요" >&2; exit 1; }
[[ "$tag" == "release/v$version" ]] || { echo "태그 $tag 와 버전 $version 이 맞지 않아요 (release/v$version 이어야 해요)" >&2; exit 1; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
key="$work/key.pem"
umask 077
if [[ -n "${UPDATE_SIGNING_KEY:-}" ]]; then printf '%s\n' "$UPDATE_SIGNING_KEY" > "$key"
elif [[ -n "${UPDATE_SIGNING_KEY_FILE:-}" ]]; then cp "$UPDATE_SIGNING_KEY_FILE" "$key"
else echo "UPDATE_SIGNING_KEY(또는 UPDATE_SIGNING_KEY_FILE)가 없어요" >&2; exit 1; fi

# 배포판 이름: CozyOrbit-<버전>-<배포판>.zip — 앱은 빌드 때 넣은 자기 배포판(CozyUpdateFlavor)과 같은 키만 받는다
flavors=(win-x64 osx-arm64 osx-x64)
assets=""
count=0
for flavor in "${flavors[@]}"; do
  name="CozyOrbit-$version-$flavor.zip"
  f="$dir/$name"
  if [[ ! -f "$f" ]]; then
    [[ $partial -eq 1 ]] && continue
    echo "$name 이 없어요 — 세 배포판이 모두 있어야 매니페스트를 만들어요" >&2; exit 1
  fi
  sha=$(sha256sum < "$f" | cut -d' ' -f1)  # 표준 입력으로 — 경로에 \ 가 있으면 sha256sum 이 줄 앞에 \ 를 붙인다
  [[ "$sha" =~ ^[0-9a-f]{64}$ ]] || { echo "$name 의 sha256 을 구하지 못했어요" >&2; exit 1; }
  size=$(stat -c%s "$f" 2>/dev/null || stat -f%z "$f")
  [[ "$size" -gt 0 ]] || { echo "$name 이 비어 있어요" >&2; exit 1; }
  [[ $count -gt 0 ]] && assets+=","
  assets+=$(printf '\n    "%s": { "name": "%s", "size": %s, "sha256": "%s" }' "$flavor" "$name" "$size" "$sha")
  count=$((count + 1))
done
[[ $count -gt 0 ]] || { echo "$dir 에 버전 $version 의 zip 이 없어요" >&2; exit 1; }
# 모르는 zip 이 섞여 있으면 멈춘다 (이름을 잘못 지은 배포판이 조용히 빠지지 않게)
for f in "$dir"/*.zip; do
  case "$(basename "$f")" in
    "CozyOrbit-$version-win-x64.zip" | "CozyOrbit-$version-osx-arm64.zip" | "CozyOrbit-$version-osx-x64.zip") ;;
    *) echo "모르는 zip 이에요: $(basename "$f")" >&2; exit 1 ;;
  esac
done

manifest="$dir/update-manifest.json"
printf '{\n  "format": 1,\n  "version": "%s",\n  "tag": "%s",\n  "assets": {%s\n  }\n}\n' "$version" "$tag" "$assets" > "$manifest"

openssl dgst -sha256 -sign "$key" -out "$work/sig.bin" "$manifest"
base64 < "$work/sig.bin" | tr -d '\r\n' > "$manifest.sig"
# 앱에 들어간 공개 키로 바로 검사 — 키를 잘못 넣었으면 여기서 멈춘다 (붙이고 나서 모든 앱이 거절하는 일 없게)
openssl dgst -sha256 -verify scripts/update-signing.pub.pem -signature "$work/sig.bin" "$manifest" >/dev/null \
  || { echo "서명이 scripts/update-signing.pub.pem 과 맞지 않아요 — UPDATE_SIGNING_KEY 를 확인해 주세요" >&2; exit 1; }

cat "$manifest"
echo "완료: $manifest (+ .sig)"
