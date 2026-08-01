# Changelog

## 0.7.0 — 2026-08-01

- **Your settings now live in `droneautomation.local.xml`.** Drop a file of that name beside
  `droneautomation.xml` holding only the values you want different, and the mod folds it over its own
  config at load. Attributes are matched by element name plus `name=`, so overriding the second of
  several same-named blocks cannot silently edit the first. Nothing is written back to the file the
  mod ships, so a mod update or a Nexus re-extract can no longer wipe what you changed. No local file
  is shipped in the release zip — it is yours, and it stays yours.
- Build-only: `SkipDeploy=true` stops the post-build copy into the live `Mods` folder, so a release
  can be cut while the game is running. Mono keeps the deployed assembly mapped for as long as the
  client is up, and overwriting it under a live game risks a SIGBUS.
- The release zip now carries the verified game version in its name (`_gameV3.0.0`), matching the
  other tehAon mods — `ModInfo.xml` has no field for it, so the file name is the only place it fits.

## 0.6.0

- **New module — Auto-Defense (`modRoboticDroneAutoDefenseMod`): the drone becomes a bodyguard.** It
  fires its own machine gun at the nearest hostile as it follows you, or stands sentry over the spot
  where you park it (the vanilla "stay" command). Kills are credited to you (XP and quests), and it
  never targets you, your allies, party members or traders. Server-side only, EAC-safe, and vanilla
  clients see the muzzle flash with nothing installed.
  - This reuses combat the junk drone has always carried but never used: `EntityDrone` ships a full
    weapon rig (`MachineGunWeapon`, an `Attack` state, an `attackMode`) that The Fun Pimps left
    disconnected — `attackState()` is empty and the gun is never instantiated. The module drives that
    real gun, so the drone's own hitscan, damage and muzzle FX apply, rather than faking damage.
  - **Quality scales fire rate and reach** (via the DLL, like every other module). Per-shot damage is
    a flat bonus set on the module (15/shot: the drone gun's base 5 plus 10). A weapon *mod's* quality
    tiers don't reliably drive an in-XML damage curve the way a base weapon's do, so the module scales
    only what can be guaranteed and leaves damage flat — worth confirming at a workbench before relying
    on the exact numbers.
  - Config knobs under `<autoDefense>` in `droneautomation.xml`: `Range`, `SecondsPerShot`,
    `MaxCatchupSeconds`, the two quality knobs, and `RequireLineOfSight` (on by default — the drone gun
    does no block damage, so this only avoids wasting shots into cover). Crafted from a looted
    schematic, sold by traders, and drops in the same pools as the other module schematics.
  - Auto-Defense is exempt from the parked / near-owner rules that bound the block modules (a sentry is
    meant to fight even while you're away) and keeps firing while you have the drone's storage open.
    Note: firing wears the drone slightly, the same durability path any drone weapon uses.

## 0.5.0

- **Crafted modules are no longer stuck at Quality 1.** Module quality scales reach and speed, so it
  is the mod's whole progression axis — but drone mods sit outside the game's CraftingTier system, so
  crafting one always produced the Q1 baseline and the only route to a Q6 module was a trader roll you
  couldn't influence. Module recipes now follow a CraftingTier curve on the **Robotics** crafting
  skill, the same one the junk drone itself uses: from Robotics 80 a crafted module comes out above
  Q1, reaching Q6 at Robotics 100. Loot and traders still supply high-quality modules for anyone who
  hasn't invested in the perk.
  - The curve deliberately hangs on the mod's own tag rather than reusing the drone's
    `gunBotT3JunkDrone` tag: that tag also carries a `RecipeTagUnlocked` effect at Robotics 76, so
    borrowing it would have unlocked every module recipe at level 76 and quietly bypassed the looted
    schematics that gate them. **The schematics still do the unlocking** — the perk only affects the
    quality of what you craft.
- **The crafting UI now tells you where a module comes from.** Every vanilla drone mod carries an
  `UnlockedBy` property, which is what makes the UI show *"Unlocked by: …Schematic"*. These seven
  never set it, so a player who hadn't found the schematic had no in-game pointer to where it drops.

- **Park the drone and it works where you left it.** Tell the drone to hold position with the vanilla
  "stay" command and the block modules (Auto-Salvage, Auto-Harvest, Auto-Repair, Auto-Plant) now work
  the ground around the spot you parked it on, instead of around you. This is what the base modules
  were always missing: Auto-Harvest, Auto-Plant and Auto-Repair tend a *base*, but they only ever
  fired while you were standing in it — exactly when you don't need the help. Park the drone in the
  farm and it keeps reaping and replanting while you're out looting; park it in the base and it keeps
  repairing. Its owner still has to be online, but no longer has to be there. Set
  `WorkWhileParked="0"` for the old behaviour. Auto-Loot is unchanged — it always works around the
  drone itself.
- **Debug logging is off by default.** It shipped **on**, so every install was writing throttled
  diagnostics to the server log out of the box. Set `Debug="1"` when you need to know why a module
  isn't acting.
- **A module can no longer take the server down.** The tick ran with no error handling, off
  `EntityDrone.OnUpdateEntity` — so one malformed block, from this mod or any other, would have thrown
  once per drone *per tick, forever*. Failures are now caught and logged (throttled), and the drone
  tries again next tick.
- **XML patches are validated before release** (`tools/validate_xml.py`, run by `package.sh`). Every
  xpath is resolved against the game's shipped config, and packaging fails if one matches nothing.
  The game doesn't fail a bad patch — it silently drops it and logs a single `WRN XML patch … did not
  apply` line — which is exactly how trader stock shipped broken for three versions. That class of
  bug is now impossible to ship.
- **The project builds on someone else's machine.** `GameDir` was hard-coded to one absolute path;
  it's now auto-detected, overridable with `GAME_DIR`, and the deploy step is skipped when the game
  isn't there.

- **A drone left behind no longer works for you from across the map.** Since 0.4.1 the block modules
  scanned around the *owner* while depositing into the *drone's* bag, and drones are exempt from the
  chunk-loaded check — so a following drone abandoned anywhere kept harvesting and salvaging around
  the player from any distance. A following drone must now actually be with its owner
  (`MaxOwnerDistance`, 25m; 0 disables). Parked drones are exempt, since working away from you is the
  entire point of parking one.

## 0.4.3

- **Fixed: Auto-Salvage stripped the trader's workstations.** The module's one scope rule was
  "unclaimed ground only", on the assumption that a land claim marks everything worth protecting. It
  doesn't: the game reports a **trader area as unclaimed** (`GetLandClaimOwner` returns `None` inside
  one), which is the exact value Auto-Salvage reads as *safe to wrench*. So the land-claim rule wasn't
  merely failing to cover traders — it was green-lighting them. Trader areas are now rejected
  explicitly and cannot be re-enabled. Reported by players who watched their drone dismantle the
  trader.
- **Auto-Salvage no longer scraps workstations.** A forge, workbench, campfire, chemistry station or
  cement mixer is something you *use* — and may still hold your materials — but each is salvageable,
  unclaimed and therefore fair game to the old rules, which is how POI (and player-placed)
  workstations were getting eaten. They're now skipped wherever they stand. Set
  `SalvageWorkstations="1"` in `droneautomation.xml` if you really did want them scrapped. Detection
  keys off the workstation's tile entity rather than its block class, so it covers modded stations
  too.
- **New Auto-Salvage config: a block deny-list and a POI switch.** Add `<exclude block="..."/>` under
  `<autoSalvage>` to protect any block by name, and set `SalvageInPOIs="0"` to keep the drone from
  wrenching anything inside a POI's footprint. POIs stay allowed by default — stripping cars and sinks
  as you clear a building is the module's whole point.

## 0.4.2

- **Fixed: traders never actually sold the drone modules.** The `traders.xml` patch pointed at `/traders/trader_item_group[...]`, but the game nests its groups one level deeper (`/traders/trader_item_groups/trader_item_group[...]`), so the patch matched nothing and was silently dropped — which is what produced the `WRN XML patch for "traders.xml" from mod "DroneAutomation" did not apply` line in the server log on every load. The warning is gone, and trader stock now works for the first time: the modules have been broken out of the trader pool ever since trader support was added in 0.3.0, so until now they were only obtainable by crafting (always Quality 1) or from loot. Traders are the intended source of the high-quality copies crafting can't make, so this is worth updating for. Thanks to the player who reported it with the exact fix.

## 0.4.1

- **Modules now work around YOU, not the drone.** Every block module (Auto-Salvage, Auto-Harvest, Auto-Repair, Auto-Plant) centred its scan on the drone, which hovers off to your side and drifts — so a target you were standing right next to could sit outside the drone's bubble. This was most visible on Auto-Salvage: a roadside vehicle wreck is a single block cell hidden under a big model, so at Q1 reach it only salvaged once the drone happened to drift over the middle. The scan is now anchored to the player, so it reliably acts on what you're standing next to. (Auto-Loot is unchanged — its reach was already wide.)
- **Auto-Salvage no longer destroys uncollected loot.** Some salvageable objects (e.g. tilt trucks, cabinets) are also loot containers, and many world containers generate their loot only when first opened — so an untouched one can read empty yet still pay out. Auto-Salvage now skips any container that is untouched, still holding items, or player-owned, and only wrenches it once you've emptied it.

## 0.4.0

- **Auto-Plant** (`modRoboticDroneAutoPlantMod`) — a new automation module: the drone sows young crop blocks from its own bag onto empty farm plots in your land claim. A "plantable" bag item is any that resolves to a growing-plant block (e.g. `plantedCorn1`) — the same block Auto-Harvest deposits when it reaps, so the two modules form a self-running farm: harvest fills the bag with seed crops, plant spends them refilling empty plots. Only ever plants on the air cell above a farm plot inside your own claim, so it can't sow on invalid ground or a neighbour's farm.
- **Overclock** (`modRoboticDroneOverclockMod`) — the first *enhancement* module: it does no work of its own, but speeds up **every** other automation module installed in the drone (cuts each core's per-action time). The boost scales with its own Quality 1-6.
- **Wide-Band Antenna** (`modRoboticDroneAntennaMod`) — an enhancement module that widens the working range of every other automation module (horizontal and vertical reach). The boost scales with its own Quality 1-6.
- Enhancement modules use one of the drone's limited mod slots, so slotting one trades against fitting another automation core — breadth vs power, by design. Both are quality-scaled, craftable from a looted schematic, and sold by traders, like every other module.
- **No longer bundles `0Harmony.dll`** — the mod now compiles against and runs on the game's own Harmony (The Fun Pimps ship it in `Mods/0_TFP_Harmony`, loaded before every other mod), so the exact runtime version is always used and the release zip is smaller. If updating from an earlier build, delete the stale `0Harmony.dll` from your `Mods/DroneAutomation` folder.

## 0.3.0

- **Module levels (Quality 1-6)** — each module now carries a native quality bar (`ShowQuality`). The installed quality scales its reach and per-action speed, from a Q1 floor to the configured Q6 ceiling via `LowQualityReach` / `LowQualityTimeMult` knobs per section in `droneautomation.xml` (see `QualityScale`). Vanilla behaviour: crafting always yields Quality 1; higher-quality (faster, longer-reach) modules come from loot or the trader.
- **Trader stock** (`traders.xml`) — the four modules are now sold by traders (appended to `groupModsAll`), with quality + price rolled by their `modsTier3` stage.

## 0.2.0

- **Auto-Salvage** (`modRoboticDroneAutoSalvageMod`) — the drone wrenches nearby salvageable blocks (cars, sinks, machines) one downgrade step at a time, into its bag. Unclaimed ground only, so it never wrecks a base.
- **Auto-Harvest** (`modRoboticDroneAutoHarvestMod`) — reaps grown crops in your own land claim and replants the seed, into its bag.
- **Auto-Repair** (`modRoboticDroneAutoRepairMod`) — repairs damaged blocks in your own land claim, paying repair materials out of the drone's bag; only repairs a block it can pay for in full.
- Shared `Pacer` + `DroneWorld` helpers: cylinder block scan with multiblock-parent resolve, the vanilla drop-count formula (owner `HarvestCount` perk), and dupe-safe bag deposits.

## 0.1.0

- New mod, split out of Loot Vacuum. A pack of junk-drone automation modules, each a separate craftable attachment unlocked by a looted schematic.
- **Auto-Loot** (`modRoboticDroneAutoLootMod`) — the drone opens zombie loot bags and containers and picks up dropped items as it follows you, at your loot speed, into its own storage. This is the old Loot Vacuum "Drone Salvage Module", moved here and renamed for clarity.
- More modules to come: Auto-Salvage, Auto-Harvest, Auto-Repair.
