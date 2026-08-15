# Building Drone Automation Kit from source

This document is written for reviewers. It gives the exact steps to rebuild the released binary from
this repository and confirm, by hash, that the DLL distributed on Nexus Mods was produced from the
source you see here.

**Every released DLL is byte-for-byte reproducible from its commit.** The hashes in the table below
were verified by building clean clones at different paths on a machine other than the one that cut
the releases.

---

## 1. What this mod is

A **server-side** 7 Days to Die mod. It adds craftable modules for the game's junk drone (auto-loot,
auto-salvage, auto-harvest, auto-repair, auto-plant, auto-defense) plus two enhancement modules. It
consists of one C# assembly (`DroneAutomation.dll`) and a set of XML/CSV config files.

Relevant to a security review:

| Property | Status |
|---|---|
| NuGet / third-party packages | **None.** `PackageReference` count in the csproj is 0. |
| Bundled third-party DLLs | **None.** The release zip contains exactly one DLL, ours. |
| Harmony | Referenced from the **game's own copy** (`Mods/0_TFP_Harmony/0Harmony.dll`) with `<Private>false</Private>`, so it is compiled against but never copied or shipped. |
| Networking | **None.** No `System.Net`, no `HttpClient`, `WebClient` or `Socket`. No custom network packet. |
| Native interop | **None.** No `DllImport` / P/Invoke. |
| Process / registry | **None.** No `Process`, `ProcessStartInfo`, or `Registry`. |
| Cryptography, encoding | **None.** No `System.Security.Cryptography`, no `FromBase64String`. |
| Dynamic code | **None.** No `Reflection.Emit`, no `Assembly.Load`. The only reflection is `Assembly.GetExecutingAssembly()` passed to `harmony.PatchAll()` — the standard Harmony entry point. |
| Filesystem | **Read-only, two files, inside the mod's own folder.** `File.Exists` + `XmlDocument.Load` on `droneautomation.xml` and the optional `droneautomation.local.xml` (`DroneAutomationMod.cs:124` and `:198`). The mod writes no files and deletes none. |

The complete set of `using` directives across all source files is: `HarmonyLib`, `System`,
`System.Collections.Generic`, `System.IO`, `System.Reflection`, `System.Runtime.CompilerServices`,
`System.Text`, `System.Xml`, `UnityEngine`.

---

## 2. Prerequisites

1. **.NET SDK 8.0.422.** Any 8.0.x or later SDK will compile the project, but a **byte-identical**
   result requires 8.0.422 — Roslyn's output changes between compiler versions. `global.json` pins
   this version with `rollForward: latestMajor`, so a newer SDK builds rather than erroring; expect
   the hash to differ if you do not use 8.0.422 exactly.
2. **A 7 Days to Die installation, version V 3.0.0 (b259)** (Steam appid `251570`, buildid
   `23906531`). The project compiles against six assemblies from the game. **These are The Fun
   Pimps' property and cannot be redistributed in this repository**, so the build needs a real
   install to reference. Their SHA-256 hashes are listed in section 5 so you can confirm your copy
   matches the one used to produce the releases.

   **It has to be that version, and not merely "a supported one."** 3.0.0 is the oldest build in
   `GAME_VERSIONS`, and the build has to be made against the oldest version claimed — a reference
   emitted against a derived type still resolves on newer builds, but one emitted against a base
   type that a later build introduced resolves on nothing older. v0.7.3 was built against a newer
   game and could not run on 3.0.0 at all; see the 0.7.4 changelog entry. `package.sh` now checks
   this before it will produce a zip.
3. **git.** The build embeds the commit id in the assembly (see section 4) — build from a clone, not
   from a source tarball, or the hash will not match.

---

## 3. Build

```bash
git clone https://github.com/kbsoftwarellc/7dtd-drone-automation
cd 7dtd-drone-automation

# check out the exact commit for the release you are verifying (see section 4)
git checkout ccf67bc199d9365d5d64267f5bf801508963066b

dotnet build -c Release -p:SkipDeploy=true
```

The built assembly lands at `bin/Release/DroneAutomation.dll` and is also copied to
`mod/DroneAutomation.dll`, which is the folder layout that gets zipped for release.

Two flags worth explaining:

- **`-p:SkipDeploy=true` — use this.** Without it the build tries to copy the mod into a live
  `Mods/` folder of a local game install as a convenience for development. It is skipped
  automatically when no install is found, but passing the flag makes that explicit and is what you
  want on a review machine.
- **`-p:GameDir="/path/to/7 Days To Die"`** if the game is not at
  `~/.local/share/Steam/steamapps/common/7 Days To Die` or `~/.steam/steam/steamapps/common/7 Days To Die`.
  The environment variable `GAME_DIR` works equivalently.

To produce the distributable zip afterwards: `./package.sh`.

---

## 4. Verifying a released binary

The .NET 8 SDK stamps the git commit id into the assembly's `AssemblyInformationalVersion`. **Each
released DLL therefore states which commit built it**, and you can read it straight out of the
binary:

```bash
strings -n 8 DroneAutomation.dll | grep -oE '[0-9]+\.[0-9]+\.[0-9]+\+[0-9a-f]{40}'
```

This is also why two builds of identical source produce different bytes at different commits — a
docs-only commit changes the embedded id and therefore the hash. It is the only thing that varies;
the compiled code is unchanged.

**The game build is part of the recipe.** The same commit compiled against a different game version
produces a different DLL, so a row is only reproducible together with the build it was made against.
That column was missing until 0.7.4, and it is how v0.7.3 came to ship against an undocumented game
version without anyone noticing.

| Release | Commit | Game build | `DroneAutomation.dll` SHA-256 | Release zip SHA-256 |
|---|---|---|---|---|
| **v0.7.0** (the file under review) | `ccf67bc199d9365d5d64267f5bf801508963066b` | V 3.0.0 (b259) | `10d754bbb6e2c7e6b6832618062c366e5c01659720675b422ab3feed56bc36c2` | `7bd4075704a0477288852848b16b81bd7a2c883c7518ed58abe8326b937ced5f` |
| **v0.7.1** (repackage) | `b30f84485c04e385aeb98ccefa8326cf0a96174c` | V 3.0.0 (b259) | `4520775eb39fc43ef4e122da36f6fdd4b4fb38d93a3cfe72850e38bd91e9fd05` | `fe105f3c7b8a288390bdb305cc9d4d5faf2a652624eac69c7d041159abeb4047` |
| **v0.7.2** | `f0499f273b2e8273e86bb4277f384c2246f33641` | V 3.0.0 (b259) | `cb02049596c6e7a0256aef5b150bb9384005302f31fd92d390aec58e0759127d` | `4b2bf33efb376aa0b94e9b404bd0aa5bb88f2f99a4cb0b8a56329e37c33c4f11` |
| **v0.7.3** ⚠ withdrawn | `960d4d444b73f5299ac11dbd7908ae4766193492` | **3.0.1 or newer — not the documented 3.0.0** | `941f0fbc91f100c8fd73481a33bf792c532a203e4490c84d2bcfac9764942d8c` | `682bc107ffc8695f776f261143cfab76193197501405c4d5f09067e02043679e` |
| **v0.7.4** | `172e950f7ec0be28670492038a6035df07dc06df` | V 3.0.0 (b259) | _not recorded at release_ | _not recorded at release_ |
| **v0.7.5** | `4c83cc5` | V 3.0.0 (b259) | `020f1359ff3415461c4e428f44da96ffc0f0945058a181f6c3ea9372e29c38db` | `e0b062f1a41af5728895c5e28cc31959c5bf694f07c107a4586b4cbb629351f0` |

⚠ **v0.7.3 does not rebuild from its own instructions.** Section 2 names V 3.0.0 (b259), but that
DLL binds `AddItem` and `TryStackItem` on `InventoryBase`, a type 3.0.1 introduced — so it was built
against a later game than this document specifies, and it cannot run on 3.0.0. Building that commit
per section 2 yields a *different, working* DLL rather than the published hash. The row is kept for
the record; use v0.7.4. Which exact build produced it is not recoverable from the binary, only that
it was 3.0.1 or newer.

To verify end to end:

```bash
unzip -j DroneAutomation_v0.7.0_gameV3.0.0.zip 'DroneAutomation/DroneAutomation.dll' -d ./shipped
git checkout ccf67bc199d9365d5d64267f5bf801508963066b
dotnet build -c Release -p:SkipDeploy=true
sha256sum shipped/DroneAutomation.dll bin/Release/DroneAutomation.dll   # identical
```

**One honest caveat: the zip itself is not byte-reproducible, only the DLL is.** ZIP archives store
each member's modification timestamp, so repacking the same files at a later date yields a different
archive hash. Verify the DLL by hash, and the config files by content — they are plain text and
identical to the ones in this repository under `mod/`.

The release zip contains 16 entries (14 files plus 2 directory entries): the DLL, `ModInfo.xml`,
`droneautomation.xml`, `README.md`, `CHANGELOG.md`, `LICENSE`, and eight files under `Config/`
(`item_modifiers.xml`, `items.xml`, `loot.xml`, `progression.xml`, `recipes.xml`, `traders.xml`,
`ui_display.xml`, `Localization.csv`). Releases before v0.7.3 have 15 entries — `ui_display.xml` is
new in v0.7.3.

---

## 5. Referenced game assemblies

Taken from a V 3.0.0 (b259) install. Five live in `7DaysToDie_Data/Managed/`, one in
`Mods/0_TFP_Harmony/`. All are referenced with `<Private>false</Private>` — compiled against, never
copied into our output or the release zip.

| Assembly | Path (relative to game root) | SHA-256 |
|---|---|---|
| `0Harmony.dll` | `Mods/0_TFP_Harmony/` | `c349e1a3fd13fa5a9facc9805a5e160161b14489f46f6bdd38202b8e124f78df` |
| `Assembly-CSharp.dll` | `7DaysToDie_Data/Managed/` | `f75e590a48705b6f6964de14bbb5ae5d7099f5a57ab39eaa66c0cd79cc100f3f` |
| `Assembly-CSharp-firstpass.dll` | `7DaysToDie_Data/Managed/` | `7694c0bfdd87692a61644fb39fa40d27e857329d1c4e046917256edbb6b8375d` |
| `LogLibrary.dll` | `7DaysToDie_Data/Managed/` | `1afc0388ee1e769f6bfbb238e28892d9a51e15f21f2ef37979df65359f258191` |
| `UnityEngine.CoreModule.dll` | `7DaysToDie_Data/Managed/` | `44d613b2996334c7fc7afadbdd2020d769e70fee1f7e7060db9d51eb5e545e07` |
| `UnityEngine.dll` | `7DaysToDie_Data/Managed/` | `c8c7fcb038611eeb6b6293601d1fffef72bffebc317256f16cfbc33a333b10fa` |

---

## 6. How the mod works, in one paragraph

`DronePatch.cs` applies a single Harmony postfix to `EntityDrone.OnUpdateEntity`. On each drone tick
the patch runs whichever modules are installed in that drone. The drone already carries its owner id,
an inventory and a lock, so a module operates entirely through the game's existing server-side APIs
with nothing new synchronised over the network. Auto-Defense drives the machine gun and attack state
the vanilla drone already ships with but never uses, so the game's own hitscan and muzzle flash apply
rather than simulated damage. Each module's rate is paced against the owner's real action time. There
is no custom network packet, and clients install nothing.

---

## 7. Repository layout

```
*.cs                  source (one file per module + the patch and shared helpers)
DroneAutomation.csproj build definition, incl. the six game references
global.json            pinned .NET SDK version
mod/                   the deployable payload: ModInfo.xml, configs, and the built DLL
mod/Config/            XML/CSV the game merges into its own config
package.sh             builds the release zip from mod/
tools/validate_xml.py  checks every xpath in the configs resolves against the shipped XML
nexus/                 the Nexus Mods page text (BBCode), kept in version control
```

Built binaries are deliberately not committed — `.gitignore` excludes `bin/`, `obj/`, `*.zip` and
`mod/DroneAutomation.dll`, so everything in this repository is source.

---

## 8. Contact

Questions about anything here: kenny@kennylbrown.com, or via the Nexus Mods profile for **tehAon**.
