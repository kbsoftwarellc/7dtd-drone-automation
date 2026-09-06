#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
# Canonical copy: 7dtd-mods/shared/check_game_versions.py -- edit THERE, then re-sync the per-mod
# copies with tools/sync_shared.py. Each mod ships its own copy because each mod repo must verify
# itself with no checkout of any other repo present.
#
# CHECK_GAME_VERSIONS_VERSION: 2
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
KNOWN = ["2.6", "3.0.0", "3.0.1", "3.1", "3.2.0"]


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


def declared_in_source():
    """
    The same claim, spelled a second time in C#.

    GAME_VERSIONS names the zip; `Compat.SupportedGameVersions` is what the mod prints to the log on
    startup, which is the line a player actually reads when they are working out whether their game
    is supported. Two copies of one fact drift, and the drift is silent - so compare them here.
    Returns (path, value) or None when the mod has no such constant.
    """
    pattern = re.compile(r'SupportedGameVersions\s*=\s*"([^"]+)"')
    for cs in sorted(REPO.glob("*.cs")) + sorted(REPO.glob("*/*.cs")):
        if "/obj/" in str(cs) or "/bin/" in str(cs):
            continue
        m = pattern.search(cs.read_text(errors="ignore"))
        if m:
            return cs.relative_to(REPO), m.group(1)
    return None


def declared_in_assembly(dll):
    """
    The same claim a third time - as it exists in the DLL that actually ships.

    `SupportedGameVersions` is a `const string`, so its value lives in the assembly's Constant
    table. Reading it there is the only way to know what the ARTIFACT says, as opposed to what the
    source in front of you says.

    This exists because checking the source is not enough, and that is not hypothetical. SteadyFrame
    0.6.0 was prepared by editing Compat.cs and rebuilding - but its csproj wants
    `$(GameDir)/7DaysToDie_Data/Managed` and it was pointed at a server-layout rig, so every game
    reference failed to resolve, no new DLL was produced, and the build still exited 0. Source said
    V3.0.1-V3.2.0, GAME_VERSIONS said V3.0.1-V3.2.0, they agreed, and this script passed - while
    mod/SteadyFrame.dll, the file that ships, still said V3.0.1-V3.1. A page claiming 3.2 above a
    DLL announcing 3.1 is exactly the drift the source check was written to prevent.

    Returns the string, or None when the assembly carries no such constant.
    """
    try:
        import dnfile
    except ImportError:
        return None
    try:
        pe = dnfile.dnPE(str(dll), clr_lazy_load=True)
        tables = pe.net.mdtables
        if not tables.Constant:
            return None
        for row in tables.Constant.rows:
            parent = getattr(row.Parent, "row", None)
            if parent is None or type(parent).__name__ != "FieldRow":
                continue
            name = getattr(parent.Name, "value", parent.Name)
            if isinstance(name, (bytes, bytearray)):
                name = name.decode()
            if str(name) != "SupportedGameVersions":
                continue
            raw = getattr(row.Value, "value", b"") or b""
            return bytes(raw).decode("utf-16-le")
    except Exception:
        return None          # unreadable metadata is not this script's failure to report
    return None


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

    raw = (REPO / "GAME_VERSIONS").read_text().strip()
    claimed = parse_versions(raw)
    print(f"{dll.name}: GAME_VERSIONS claims {', '.join('V' + v for v in claimed)}")

    declared = declared_in_source()
    if declared is not None:
        where, value = declared
        if "".join(value.split()) != "".join(raw.split()):
            print(f"\nFAIL: {where} says SupportedGameVersions = \"{value}\", GAME_VERSIONS says "
                  f"\"{raw}\".")
            print("      The zip name and the line the mod prints at startup would disagree about")
            print("      the same fact. Update both.")
            return 1

    built = declared_in_assembly(dll)
    if built is not None and "".join(built.split()) != "".join(raw.split()):
        print(f"\nFAIL: the built DLL says SupportedGameVersions = \"{built}\", GAME_VERSIONS says "
              f"\"{raw}\".")
        print("      The SOURCE agrees with GAME_VERSIONS, so this is not a typo - it is a STALE")
        print("      BUILD. The DLL about to be zipped was compiled before the claim changed.")
        print("      Rebuild, and check the build actually produced a new DLL: a build whose game")
        print("      references fail to resolve still exits 0 and leaves the old file in place.")
        return 1

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
