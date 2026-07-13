#!/usr/bin/env python3
"""
Resolve every xpath in mod/Config/*.xml against the game's shipped Data/Config, and fail if one
matches nothing.

This exists because a non-applying XML patch is invisible. The traders.xml xpath was wrong from
0.3.0 to 0.4.1 - trader stock, an advertised feature, never worked for three versions - and the only
symptom was a single line in the server log:

    WRN XML patch for "traders.xml" from mod "DroneAutomation" did not apply

Nobody reads that. A player eventually reported it, with the fix. The game does not fail a bad patch,
it shrugs and carries on, so the check has to happen before release.

Usage:  GAME_DIR="/path/to/7 Days To Die" python3 tools/validate_xml.py
        (GAME_DIR is optional; common install paths are tried.)
"""

import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

PATCH_TAGS = {
    "append", "prepend", "insertAfter", "insertBefore",
    "set", "setattribute", "remove", "removeattribute", "csv",
}

# xpath features ElementTree cannot faithfully evaluate. If a patch ever uses one, this script must
# say so loudly rather than quietly "pass" it - a validator you can't trust is worse than none.
UNSUPPORTED = re.compile(r"//|\.\.|\b(contains|starts-with|not|text)\s*\(|\|")

CANDIDATE_GAME_DIRS = [
    os.environ.get("GAME_DIR", ""),
    os.path.expanduser("~/.local/share/Steam/steamapps/common/7 Days To Die"),
    os.path.expanduser("~/.steam/steam/steamapps/common/7 Days To Die"),
]


def find_game_config() -> Path:
    for d in CANDIDATE_GAME_DIRS:
        if not d:
            continue
        cfg = Path(d) / "Data" / "Config"
        if cfg.is_dir():
            return cfg
    sys.exit(
        "Could not find the game's Data/Config. Set GAME_DIR, e.g.\n"
        '  GAME_DIR="/path/to/7 Days To Die" python3 tools/validate_xml.py'
    )


def resolve(root: ET.Element, xpath: str) -> list:
    """
    Evaluate the absolute xpath forms the mod actually uses (/root, /root/child[@name='x']/...)
    against a parsed vanilla config. ElementTree only understands paths relative to the root, so the
    leading root segment is matched by hand and the rest is handed to findall.
    """
    if not xpath.startswith("/"):
        raise ValueError(f"not an absolute xpath: {xpath}")
    if UNSUPPORTED.search(xpath):
        raise ValueError(f"xpath uses syntax this validator cannot verify: {xpath}")

    segments = xpath.strip("/").split("/")
    root_name = segments[0].split("[")[0]
    if root.tag != root_name:
        return []
    if len(segments) == 1:
        return [root]
    return root.findall("./" + "/".join(segments[1:]))


def main() -> int:
    repo = Path(__file__).resolve().parent.parent
    game_config = find_game_config()
    mod_config = repo / "mod" / "Config"

    print(f"vanilla config: {game_config}")

    checked = failures = 0

    for patch_file in sorted(mod_config.glob("*.xml")):
        target = game_config / patch_file.name
        if not target.is_file():
            print(f"FAIL  {patch_file.name}: no such file in the game's Data/Config")
            failures += 1
            continue

        vanilla_root = ET.parse(target).getroot()

        for element in ET.parse(patch_file).getroot().iter():
            if element.tag not in PATCH_TAGS:
                continue
            xpath = element.get("xpath")
            if not xpath:
                continue

            checked += 1
            try:
                matches = resolve(vanilla_root, xpath)
            except ValueError as e:
                print(f"FAIL  {patch_file.name}: {e}")
                failures += 1
                continue

            if matches:
                print(f"ok    {patch_file.name}: <{element.tag}> {xpath} -> {len(matches)} node(s)")
            else:
                print(f"FAIL  {patch_file.name}: <{element.tag}> {xpath} -> MATCHES NOTHING "
                      f"(the game will silently drop this patch)")
                failures += 1

    if not checked:
        print("FAIL  no xpaths found to check - is mod/Config/ empty?")
        return 1

    print()
    if failures:
        print(f"{failures} of {checked} xpath(s) would not apply.")
        return 1

    print(f"All {checked} xpath(s) resolve against the shipped config.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
