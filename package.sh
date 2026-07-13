#!/usr/bin/env bash
set -euo pipefail

MOD=DroneAutomation
VERSION=$(grep -oP '(?<=<Version value=")[^"]+' mod/ModInfo.xml)

if [ ! -f "mod/$MOD.dll" ]; then
	echo "mod/$MOD.dll missing - build first:" >&2
	echo "  DOTNET_ROOT=\$HOME/.dotnet ~/.dotnet/dotnet build -c Release" >&2
	exit 1
fi

# A patch whose xpath matches nothing is dropped silently by the game - that is how trader stock
# shipped broken for three versions. Never zip one. Set SKIP_XML_CHECK=1 to package without the
# game installed (and accept that you are shipping unverified xpaths).
if [ "${SKIP_XML_CHECK:-0}" != "1" ]; then
	python3 tools/validate_xml.py
fi

STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT

mkdir -p "$STAGE/$MOD"
cp -r mod/. "$STAGE/$MOD/"
cp README.md CHANGELOG.md LICENSE "$STAGE/$MOD/"

OUT="${MOD}_v${VERSION}.zip"
rm -f "$OUT"
(cd "$STAGE" && zip -qr - "$MOD") > "$OUT"

echo "Wrote $OUT"
