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

# droneautomation.local.xml is the USER's file. It ships in no package -- that is the whole reason
# it survives an update -- so strip any copy a local test left in mod/ before the zip is built.
find "$STAGE" -name '*.local.xml' -delete

OUT="${MOD}_v${VERSION}.zip"
# zip now writes the archive itself from inside $STAGE (it used to stream to a redirect that was
# resolved in the repo dir), so it needs an absolute path to land in the same place as before.
OUT_ABS="$PWD/$OUT"
rm -f "$OUT"
# -X matters, and not for tidiness. Without it Info-ZIP stamps every entry with Unix extra fields
# (0x5455 timestamps + 0x7875 uid/gid); writing to stdout instead of a named file is the other half
# of the same habit. v0.7.0 was the only tehAon zip built that way and the only one Nexus put in
# automatic quarantine, while VirusTotal cleared the exact same bytes 0/67 -- so the extra fields
# were never proven to be the cause, but every zip Nexus accepts is built with -X to a real file.
# Keep this identical to the other mods' packagers; a "harmless" difference here costs a re-upload.
( cd "$STAGE" && zip -r -X "$OUT_ABS" "$MOD" -x '*.DS_Store' >/dev/null )

echo "Wrote $OUT"
