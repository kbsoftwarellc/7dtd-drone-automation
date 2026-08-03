#!/usr/bin/env python3
"""
Prove the built DLL resolves against EVERY game build GAME_VERSIONS claims - especially the oldest.

This exists because DroneAutomation v0.7.3 shipped labelled `V3.0.0-V3.1` and could not run on 3.0.0
at all. Nothing in its source changed; the DLL was simply compiled against a newer game. 3.0.1
introduced `InventoryBase` as a base class of the inventory types and moved `AddItem` and
`TryStackItem` up onto it, so a build made against 3.0.1+ emits those member references on
`InventoryBase` - a type that does not exist in 3.0.0. The mod loads, then throws the first time
Mono JITs the affected path.

The asymmetry is the whole point, and it decides which build you must compile against:

    reference on a DERIVED type  -> resolves on newer builds too (the runtime walks up to the base)
    reference on a BASE type     -> resolves on NOTHING older than the build that introduced it

So compile against the OLDEST version you claim to support. Building against the newest silently
drops the oldest, and nothing in the build output says so - `dotnet build` succeeds, a refcheck
against the newest build passes, the mod boots fine on the machine you tested. It is only broken for
the players you never hear from.

Checking the newest build is not optional either - that is what catches a member the game removed.
Both ends matter; only the oldest end is easy to get wrong without noticing.

Usage:  python3 tools/check_game_versions.py [path/to/Mod.dll]

Where the builds live:  $GAME_BUILDS (default ~/7dtd-servers), one directory per version, named
exactly as it appears in GAME_VERSIONS without the leading V:

    ~/7dtd-servers/3.0.0/7DaysToDieServer_Data/Managed/Assembly-CSharp.dll
    ~/7dtd-servers/3.1/7DaysToDieServer_Data/Managed/Assembly-CSharp.dll

A client install (7DaysToDie_Data) is accepted in the same place. Point $GAME_BUILDS elsewhere if
you keep them somewhere else.

Exit 0 = every claimed version was located AND every reference resolved in it.
Exit 1 = a reference does not resolve somewhere, or a claimed version could not be checked at all.
         The second is a failure on purpose: an unchecked claim is the bug this script exists for.
         SKIP_MISSING_BUILDS=1 downgrades only the "could not be located" half to a warning.
"""

import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent

# Every game version this project knows how to order, oldest first. A range in GAME_VERSIONS is
# expanded across this list, so a new game build has to be added here before it can be claimed.
KNOWN = ["3.0.0", "3.0.1", "3.1"]


def parse_versions(text):
    """`V3.0.0-V3.1` -> every KNOWN version in that range. `V3.0.0V3.0.1` -> just those two."""
    found = re.findall(r"V(\d+(?:\.\d+)*)", text)
    if not found:
        raise SystemExit(f"GAME_VERSIONS is not in the expected form: {text!r}")

    unknown = [v for v in found if v not in KNOWN]
    if unknown:
        raise SystemExit(
            f"GAME_VERSIONS names {', '.join(unknown)}, which tools/check_game_versions.py has "
            f"never heard of. Add it to KNOWN (in release order) and put the build under "
            f"$GAME_BUILDS."
        )

    if "-" in text and len(found) == 2:
        lo, hi = KNOWN.index(found[0]), KNOWN.index(found[1])
        if lo > hi:
            raise SystemExit(f"GAME_VERSIONS range runs backwards: {text!r}")
        return KNOWN[lo:hi + 1]
    return sorted(set(found), key=KNOWN.index)


def find_dll():
    """The mod's own DLL. Named from ModInfo.xml, because in a git worktree the directory is not."""
    mod_dir = REPO / "mod"
    try:
        name = ET.parse(mod_dir / "ModInfo.xml").getroot().find("Name").get("value")
        candidate = mod_dir / f"{name}.dll"
        if candidate.is_file():
            return candidate
    except Exception:
        pass
    ours = [p for p in sorted(mod_dir.glob("*.dll")) if p.name != "0Harmony.dll"]
    if len(ours) == 1:
        return ours[0]
    raise SystemExit(
        f"cannot tell which DLL is ours in {mod_dir} ({len(ours)} candidates) - pass it as an "
        f"argument, or build first"
    )


def assembly_for(version):
    """Locate Assembly-CSharp.dll for one version, server layout or client layout."""
    root = Path(os.environ.get("GAME_BUILDS", Path.home() / "7dtd-servers")) / version
    for data in ("7DaysToDieServer_Data", "7DaysToDie_Data"):
        dll = root / data / "Managed" / "Assembly-CSharp.dll"
        if dll.is_file():
            return dll
    return None


def main():
    dll = Path(sys.argv[1]) if len(sys.argv) > 1 else find_dll()
    if not dll.is_file():
        raise SystemExit(f"no DLL to check at {dll} - build first")

    claimed = parse_versions((REPO / "GAME_VERSIONS").read_text().strip())
    print(f"{dll.name}: GAME_VERSIONS claims {', '.join('V' + v for v in claimed)}")

    refcheck = HERE / "refcheck.py"
    broke, unchecked = [], []

    for version in claimed:
        asm = assembly_for(version)
        if asm is None:
            unchecked.append(version)
            print(f"  V{version:<8} NOT FOUND under $GAME_BUILDS - claim unverified")
            continue
        proc = subprocess.run(
            [sys.executable, str(refcheck), str(dll), str(asm)],
            capture_output=True, text=True,
        )
        if proc.returncode == 0:
            print(f"  V{version:<8} ok")
        else:
            broke.append(version)
            detail = [l.strip() for l in proc.stdout.splitlines() if "missing" in l]
            print(f"  V{version:<8} BREAKS: {'; '.join(detail) or proc.stdout.strip()}")

    if broke:
        oldest = claimed[0]
        print()
        print(f"FAIL: this DLL cannot run on V{', V'.join(broke)}, which GAME_VERSIONS claims.")
        if oldest in broke:
            print(
                f"      V{oldest} is the OLDEST version claimed, which is the usual cause: the DLL "
                f"was\n      compiled against a newer game. Rebuild against V{oldest} -"
                f"\n          GAME_DIR=<a V{oldest} install> dotnet build -c Release -p:SkipDeploy=true"
                f"\n      References made against the oldest build still resolve on every newer one."
            )
        return 1

    if unchecked:
        msg = (f"{len(unchecked)} claimed version(s) could not be checked: "
               f"V{', V'.join(unchecked)}")
        if os.environ.get("SKIP_MISSING_BUILDS") == "1":
            print(f"\nWARNING: {msg} (SKIP_MISSING_BUILDS=1)")
            return 0
        print(f"\nFAIL: {msg}.")
        print("      Put those builds under $GAME_BUILDS, narrow GAME_VERSIONS to what you can")
        print("      actually verify, or set SKIP_MISSING_BUILDS=1 to ship the claim unproven.")
        return 1

    print(f"\nAll {len(claimed)} claimed version(s) verified.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
