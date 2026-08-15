# Changelog

## 0.8.0 — 2026-08-15

- **Switch a module off without pulling it out of the drone.** Hold **E** on the drone → **Talk** →
  **Automation modules…** and every automation function gets a row reading `Auto-Loot: ON - switch it
  off` (or `OFF - switch it on`). Clicking it flips the switch; the module stays in its slot and keeps
  its quality, it just stops acting. A **Switch every module back on** row clears the lot. The setting
  survives relogs and server restarts.

  Asked for as a hotkey, which this mod cannot do. Key bindings are client config and the radial wheel
  is client code — `EntityDrone.InitLocalActivationCommands`, `AllowActivationCommand` and
  `OnEntityActivated` all take an `EntityPlayerLocal` and only ever run on the machine holding the
  camera. Either one would mean shipping a client DLL and ending the mod's "install it on the server,
  players install nothing" rule.

  The wheel's own **Talk** entry is the way in without breaking that rule. It opens the XML dialog
  `junkDrone`, and `WorldStaticData` registers both `dialogs.xml` and `buffs.xml` with
  `_sendToClients: true` — the server pushes its patched copies to every vanilla client, which parses
  them locally. So a server-side modlet can put rows in that menu, one click deeper than the wheel.

  Each row is half of a matched pair guarded by opposite `CheckCVar` requirements, so exactly one is
  ever visible and the visible one is both the readout and the switch (`XUiC_DialogResponseEntry`
  drops a row whose `requirementtype="Hide"` requirement fails). Clicking it adds a hidden permanent
  buff that writes one CVar — `AddBuff` is the only write the dialog system exposes that isn't quest
  or trader plumbing. That buff net-syncs, so the client runs the effect immediately (the row flips
  under the cursor) and `NetPackageAddRemoveBuff.ProcessPackage` re-applies it on the server, where
  `DronePatch` reads the result. No polling, no custom network packet, nothing for a vanilla client
  to be kicked over.

  The switch buffs are **permanent and fire on `onSelfBuffStack`, not `onSelfBuffStart`**, and
  `DronePatch.PrimeToggleBuffs` keeps all thirteen seated on the owner. That is not tidiness, it is
  the difference between the menu working and the menu appearing to eat every other click.
  `EntityBuffs.AddBuff`, handed a buff the entity does not already have, only appends a `BuffValue`
  and fires nothing — `onSelfBuffStart` is raised later, on the buff's first tick. But the dialog
  redraws its rows exactly once per click (`XUiC_DialogResponseList.Update` rebuilds only while
  `IsDirty`, and `OnPressResponse` is the only thing that sets it) and a row's requirement is
  evaluated only during that rebuild. A CVar written on buff start therefore lands *after* the only
  redraw, so every row shows the previous click's state: click a row and nothing happens, click again
  and you see the first click's result — including on a different row, which is why switching Harvest
  then Repair looked like Repair was ignored. `AddBuff`'s stacking branch instead calls
  `FireEvent(onSelfBuffStack, …)` inline before returning, so a buff that is already present writes
  its CVar before `Dialog.SelectResponse` switches the statement. Priming is safe only because these
  buffs have no `onSelfBuffStart` effect at all; the `HasBuff` guard stops the drone tick from
  stacking them, and a buff lost to death cleanup or an old save is re-seated on the next tick.

  A function with no module fitted reads **`Auto-Loot: no module installed`** instead of offering a
  switch. Without that the menu looks like a settings screen — six functions you can simply turn on —
  when they are hardware you have to find the schematic for, craft and fit. No dialog requirement can
  see an entity's mod slots (they only test *player* state, and nothing in the dialog even carries the
  respondent's entity id), so the drone publishes what it is carrying onto its owner as a CVar and the
  menu gates on that. The nearest drone within 8m claims the menu and keeps it until something is
  strictly nearer, so parking a second drone next to the first can't make the rows flicker.

  One limit worth knowing: the switches are **per player, not per drone** — `AddBuff` applies to the
  talking player and CVars live on the player, so running two drones switches a function off on both.
  The "no module installed" rows *are* per drone, since the server publishes them for whichever drone
  you're standing at, so those rows and the switches answer subtly different questions.

  The CVar means *disabled* rather than enabled on purpose: `EntityBuffs.GetCustomVar` returns 0 for a
  name it has never seen and `EntityBuffs.Write` skips any CVar sitting at 0, so an "enabled" flag
  would read as off for every existing player on update.

- **Internal: the drone publishes its fitted modules with `EntityAlive.SetCVar`, not
  `EntityBuffs.SetCustomVar`.** `SetCustomVar` gained a fifth parameter (`_forceSendToClients`) in
  V3.1, so a call compiled against 3.0.0 — which is what this mod builds against — emits a
  four-argument signature that does not exist on 3.1 and would throw `MissingMethodException` every
  tick there. `SetCVar` is a two-argument wrapper, identical in both, that forwards with the same
  net-sync default. Worth recording because it runs *against* the usual rule: compiling against the
  oldest supported version protects you from types that only exist later, but an added **optional
  parameter** is source-compatible and *not* binary-compatible, so it breaks the old build on the new
  game. `tools/check_game_versions.py` is what caught it.

## 0.7.5 — 2026-08-14

- **Fixed: Auto-Harvest minted a free seed every time it reaped a crop.** A crop's harvest drops
  include its own young stage — `plantedCorn3HarvestPlayer` lists
  `<drop event="Harvest" name="plantedCorn1" tag="farmerBonusSeed"/>` — and the module banked that
  seed in the drone's bag *and* separately planted a fresh one in the empty plot. Two seeds where the
  crop produced one.

  That is not what harvesting by hand does. Vanilla leaves a reaped plot **bare**: crops ship with
  `DowngradeBlock` commented out, and a block with no `DowngradeBlock` resolves to air
  (`Block.cs:1789`, and the downgrade path at `Block.cs:2461` only runs when it is not air). The seed
  the crop hands you is the one you are meant to spend putting the plot back. So a drone farm quietly
  produced a spare seed per crop per cycle — on a 27-plot field, 27 seeds a cycle, compounding into
  more plots.

  Auto-Harvest now pays for the replant with the seed it just reaped. `EmitDrops` takes an optional
  `_payOne`, withholding a **single unit** of that one item from the payout, so perk-boosted seed
  drops still hand over the surplus — only the one seed the drone plants on your behalf is consumed.
  Produce is untouched.

- **Auto-Harvest now says why it reaped nothing.** With `Debug="1"` a pass that harvests nothing
  prints the numbers behind the decision instead of a flat "nothing in range":

  ```
  harvest: anchor (666,61,907) r=9.6/6.4 q6: 2201 blocks, 27 grown crops,
           0 outside your claim, 27 with no replant
  ```

  Each figure points at a different cause — out of reach, wrong claim, or no replant — and the anchor
  and post-quality radius are printed because for a *following* drone the scan is centred on the
  **owner**, not the drone, which is its own easy misread. "Nothing in range" is not a useful thing to
  tell someone standing in a field of corn.

  This came out of a playtest where Auto-Harvest appeared dead. It was not: the field had been spawned
  with the **wild** crop blocks (`plantedCorn3Harvest`), which carry produce drops only. A player-grown
  crop becomes `plantedCorn3HarvestPlayer`, which also lists
  `<drop event="Harvest" name="plantedCorn1" tag="farmerBonusSeed"/>` — the young stage the module
  replants with. Only the Player variant sits on the growth chain
  (`plantedCorn1 -> plantedCorn2 -> plantedCorn3HarvestPlayer`), so only it is what the module meets on
  a real farm. Refusing a wild crop is deliberate: reaping one would leave a bare plot, since there is
  nothing to put back.

  No behaviour changed. `TryGetReplant` still reads `EnumDropEvent.Harvest`, which was always correct;
  the comment above it now records the wild-vs-Player distinction so the next person testing by hand
  does not spawn the wrong block and conclude the module is broken.

## 0.7.4 — 2026-08-03

- **Fixed: v0.7.3 could not run on game 3.0.0, which it claimed to support.** If you are on 3.0.0,
  Auto-Loot — the module most people install this for — threw the first time the drone tried to put
  something in a bag, and kept throwing. Every other module that stores an item was in the same
  boat. 3.0.1 and 3.1 were unaffected.

  Nothing in the source was wrong. The released DLL was compiled against a newer game than the
  oldest one on its label, and that is enough on its own. 3.0.1 introduced `InventoryBase` as a base
  class above the inventory types and moved `AddItem` and `TryStackItem` up onto it, so a build made
  against 3.0.1 or later points those calls at `InventoryBase` — a type 3.0.0 has never heard of.

  The direction matters and it is worth stating plainly, because it is the part that is easy to get
  backwards. A reference made against a *derived* type still resolves on newer builds, because the
  runtime walks up the hierarchy to find the member. A reference made against a *base* type that was
  introduced later resolves on nothing older, because the type itself is missing. So the build has
  to be made against the **oldest** version claimed, not the newest.

  What makes this worth a release note rather than a one-line fix is that every check passed. The
  build succeeded. `refcheck.py` against 3.1 passed — it was only ever run against the newest build.
  A live 3.1 dedicated server booted the mod cleanly. None of it could see the version that broke.

- **`package.sh` now refuses to build a zip whose label it cannot back up.** New
  `tools/check_game_versions.py` reads `GAME_VERSIONS`, locates a game build for each version named,
  and resolves every external reference in the DLL against all of them. A claimed version with no
  build available fails too, rather than quietly passing — an unverified claim is the whole bug.
  `GAME_BUILDS` says where the builds live (default `~/7dtd-servers`); `SKIP_REF_CHECK=1` skips it
  for anyone packaging without them.

- **`BUILD.md` now records which game build each release was compiled against.** Without it the
  hash table could not be reproduced even from the right commit, which made the reproducibility
  claim thinner than it looked.

## 0.7.3 — 2026-08-02

- **Fixed: every module you bought or spawned was secretly Quality 1.** Only *crafted* modules ever
  had a real level. A module bought from a trader, or taken from the creative menu, came out at
  Quality 0 — which the creative menu shows as `*` instead of a number, and which the mod then
  treated as Quality 1. So the shortest reach and slowest actions, no matter what you paid or which
  trader you bought from. This is the answer to "I can buy them from traders but never get anything
  above level 1".

  The cause is a piece of vanilla plumbing that is easy to miss: the game only ever *rolls* a quality
  for an item whose effects are tiered (`ItemClass.HasQuality` is `Effects.IsOwnerTiered()`), and
  these modules deliberately had no effect groups at all, because their reach and speed scaling lives
  in the mod's own code rather than in item effects. Each module now carries one tiered `effect_group`
  holding a display value and no passive effects, which switches quality rolling on without changing
  any behaviour. Auto-Defense keeps its flat `tiered="false"` damage group and gained a second, tiered
  one alongside it.

- **Modules now appear in the creative menu.** They inherited `CreativeMode="None"` from vanilla's
  `modGeneralMaster`, and the game filters those out unconditionally — the show-hidden toggle does not
  bring them back. All eight are now listed, at a real Quality 1-6. Tip: typing `#6` in the creative
  search box forces everything it shows to Quality 6.

- **Modules now have an effects tab.** Like a vanilla mod, the item info window gains a second tab
  next to the description showing what the level actually buys you: working range and action speed
  as a percentage of that module's maximum (Overclock and the Antenna show the multiplier they apply
  to every other module). Both halves were missing — the numbers have to live on the item as
  `display_value` *and* the labels in `ui_display.xml`, and a value nothing references is silently
  never drawn. The percentages are derived from the real scaling curve, so they stay true whatever
  reach and timing you set in `droneautomation.xml`.

- **Verified on game 3.1.** `GAME_VERSIONS` is now `V3.0.0-V3.1`; the previous `V3.0.0` only meant
  "last version anyone checked", not that 3.1 was unsupported.

## 0.7.2 — 2026-08-02

- **Auto-Loot no longer catches things you throw.** A thrown rock, molotov, grenade, pipe bomb or
  stick of dynamite is not a special projectile — the game spawns each one as an ordinary dropped
  item that happens to be moving — so the drone was picking them out of the air, and could pocket a
  molotov that was already burning. Auto-Loot now applies the same rule the game applies to you: it
  only takes an item that has come to rest and that you would be allowed to pick up by hand, which
  leaves live explosives alone.
- Anything you threw is also left where it lands for a few seconds, so a rock still works as a decoy
  instead of going straight into the drone. Tune with `ThrownGraceSeconds` in the `autoLoot` section
  (default 5, 0 disables the wait). Items still in flight and armed explosives are skipped whatever
  it is set to.
- Loot you did not throw is untouched by any of this: bags, harvest yields, backpacks and dropped
  stacks are spawned without motion, so the drone treats them exactly as it did before.

## 0.7.1 — 2026-08-01

Repackaging only — the mod itself is unchanged from 0.7.0. Every file inside the zip is
byte-identical except the DLL, which differs solely by the build's embedded commit id.

- Nexus put the 0.7.0 download into automatic quarantine, which removed its download button.
  VirusTotal cleared the exact same bytes **0 / 67** — no vendor flagged anything — and the archive
  passed every other check: plain ZIP, no nested archives, no password, not self-extracting,
  `unzip -t` clean, and Nexus's own file-manifest generated fine. So this was a false positive, not
  a finding.
- **What triggered it is still unknown, and this release does not claim to fix it.** The packaging
  was the obvious suspect — this was the only tehAon mod built with `zip -qr -` streamed to stdout,
  which leaves Unix extra fields (timestamps + uid/gid) on every entry, where every mod Nexus marks
  "Safe to use" is built `zip -r -X` to a real file. That suspicion is now **ruled out**: v0.4.0 and
  v0.4.2 were built exactly the same way and Nexus accepted both. Clearing a quarantine is a
  moderator action; a re-upload is not guaranteed to do it.
- The packaging is aligned with the other mods anyway, because there is no reason for this one to
  be built differently.
- `package.sh` also strips any stray `*.local.xml` before zipping, so a personal config can never
  ship (it already could not reach the zip, but now it cannot even by accident).

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
