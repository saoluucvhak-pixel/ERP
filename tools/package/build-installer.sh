#!/usr/bin/env bash
# Dung bo cai Windows (HAK-Accounting-Setup-<phien ban>.exe) tren Linux/macOS: publish win-x64 + build web + NSIS.
# Can: dotnet SDK 10, node/npm, makensis (apt install nsis). Usage: tools/package/build-installer.sh [phien-ban] [thu-muc-dich]
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
ver="${1:-$(date +%Y.%m.%d)}"
out="${2:-$root/artifacts}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$out"

echo "1/4 Build giao dien web"
( cd "$root/web" && { [ -d node_modules ] || npm ci; } && npm run build )

echo "2/4 Publish API (win-x64, tu chua .NET, mot tep)"
dotnet publish "$root/src/Hak.Api" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o "$work/app"
mv "$work/app/Hak.Api.exe" "$work/app/HAK.exe"
rm -f "$work/app"/*.pdb

echo "3/4 Chep giao dien va tep khoi dong"
cp -r "$root/web/dist" "$work/app/wwwroot"
cp "$root"/tools/package/*.cmd "$root/tools/package/DOC-TRUOC.txt" "$work/app/"

echo "4/4 Dong goi NSIS"
makensis -V2 -DAPPDIR="$work/app" -DVERSION="$ver" -DOUTFILE="$out/HAK-Accounting-Setup-$ver.exe" "$root/tools/package/installer.nsi"
( cd "$out" && sha256sum "HAK-Accounting-Setup-$ver.exe" | tee "HAK-Accounting-Setup-$ver.sha256" )
