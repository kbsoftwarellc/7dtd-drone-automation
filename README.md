# Drone Automation Mods

A pack of installable junk-drone modules for 7 Days to Die **V 3.0** that automate chores while the drone follows you. Each module is a separate craftable drone attachment, unlocked by a looted schematic (like the vanilla drone mods), and each does the work **as if you did it yourself** — your loot stage, perks and tool bonuses apply, and each target takes about the time it would take you by hand.

Split out of the [Loot Vacuum](../LootVacuum) mod, whose placeable storage-vacuum block stays there.

## Modules

- **Auto-Loot** (`modRoboticDroneAutoLootMod`) — opens zombie loot bags and containers and picks up dropped items, into the drone's storage. A mobile version of the Loot Vacuum block: bags and dropped items within 15m, block containers within 8m, 6m vertical.
- **Auto-Salvage** (`modRoboticDroneAutoSalvageMod`) — wrenches nearby salvageable blocks (cars, sinks, machines) one downgrade step at a time into the drone bag. **Unclaimed ground only** — never inside anyone's land claim, so it can't wreck your base or a neighbour's — and **never inside a trader area** (the game reports one as unclaimed, so that has to be its own rule). It also leaves **workstations** alone by default: a forge or workbench is something you use, and may still hold your materials. Reach is kept small by default because it destroys blocks. Configurable via `SalvageWorkstations`, `SalvageInPOIs` and an `<exclude block="..."/>` deny-list.
- **Auto-Harvest** (`modRoboticDroneAutoHarvestMod`) — reaps grown crops in **your own land claim** and replants the seed, into the drone bag. A crop is only reaped if its replant stage can be resolved, so a plot is never left empty.
- **Auto-Repair** (`modRoboticDroneAutoRepairMod`) — repairs damaged blocks in **your own land claim**, paying the repair materials out of the drone's bag. Only repairs a block it can pay for in full.
- **Auto-Plant** (`modRoboticDroneAutoPlantMod`) — sows young crops from the drone bag onto empty farm plots in **your own land claim**. A "seed" is any bag item that resolves to a growing-plant block (e.g. `plantedCorn1`) — the same block Auto-Harvest deposits when it reaps, so the pair runs a self-sustaining farm. Only plants on the empty cell above a farm plot, never on invalid ground.
- **Auto-Defense** (`modRoboticDroneAutoDefenseMod`) — turns the drone into a **bodyguard**: it fires its own machine gun at the nearest hostile as it follows you, or stands **sentry** over the spot you park it on. Kills are credited to you, and it never targets you, your allies, party members or traders. This drives the combat rig the junk drone has always carried but never used — the vanilla drone ships a `MachineGunWeapon` and an `Attack` state that The Fun Pimps left disconnected — so the drone's own hitscan and muzzle flash apply. Higher-quality modules fire faster and reach farther; per-shot damage is a flat bonus set on the module.

### Enhancement modules

These have no automation of their own — they **improve the other modules**. Each uses one of the drone's limited mod slots, so slotting one trades against fitting another automation core.

- **Overclock** (`modRoboticDroneOverclockMod`) — speeds up every installed automation module (cuts each core's per-action time). Boost scales with its own Quality 1-6.
- **Wide-Band Antenna** (`modRoboticDroneAntennaMod`) — widens the working range (horizontal + vertical) of every installed automation module. Boost scales with its own Quality 1-6.

Scope rules are deliberate: Auto-Salvage is destructive so it stays off claimed ground; Auto-Harvest, Auto-Repair and Auto-Plant only ever act inside your own claim.

## Parking the drone

Tell the drone to **hold position** with the vanilla "stay" command and the block modules work the ground around the spot you parked it on, instead of around you. Its owner still has to be online, but no longer has to be standing there — so a drone parked in the farm keeps reaping and replanting while you're out looting, and one parked in the base keeps repairing it. Send it back to following and it goes back to working around you. Auto-Loot always works around the drone itself.

A drone that is **following** you only works blocks while it's actually with you (`MaxOwnerDistance`, 25m by default) — otherwise a drone left behind somewhere would keep working around you from across the map.

**Auto-Defense** ignores all of this on purpose: it works around the drone itself and fights whether the drone is following or parked, and even while you have its storage open — a bodyguard that stops shooting when you step away or open a container isn't much of one. A parked drone becomes a stationary turret guarding that spot.

Install a module in a junk drone like any other drone mod. Modules stack with each other and with the vanilla cargo mod.

## How it works

Everything runs server-side via one Harmony postfix on `EntityDrone.OnUpdateEntity`. The drone already carries an `OwnerID`, a `Bag` and a lock, and `World.cs` exempts drones from the chunk-loaded check, so a module ticks wherever the drone follows you, with nothing new synced. Each target is paced off the owner's real action time and banked against a `MaxCatchupSeconds` cap so a reload cannot burst.

## Unlocking

Each module recipe is `learnable` — hidden until you find and read its schematic, which drops from the same loot as the vanilla drone-mod schematics. Then craft it at a workbench.

## Levels

A module's Quality (1-6) scales its reach and speed — and for the enhancement modules, how big a boost they give. Quality comes from three places:

- **Crafting**, scaled by your **Robotics** crafting skill, on the same curve the junk drone itself uses: below Robotics 80 you get the Q1 baseline, from 80 it climbs, and at Robotics 100 you craft Q6. (The perk only changes the *quality* of what you craft — you still need the schematic to unlock the recipe at all.)
- **Loot**, rolled by the container.
- **Traders**, rolled by their `modsTier3` stage.

## Multiplayer

Works in single-player, on a host, and on a dedicated server, and it is **server-side only**. The drone's bag and loot containers are client-authoritative, so every module pauses while the owner has the drone's storage open, and refuses locked or other-player targets. There is no custom network packet.

If a module is not acting, set `Debug="1"` on the `<droneautomation>` line in `mod/droneautomation.xml` (off by default) and watch the log for `[DroneAutomation][drone <id>]` — it says, throttled, exactly which check is stopping it.

## Install

**Server-side only.** Copy the `DroneAutomation` folder into the server's `Mods/` and launch with **EasyAntiCheat off** (DLL mod). Clients install nothing and can keep EAC on — `item_modifiers`, `items`, `recipes` and `loot` are all `_sendToClients: true`, so the server ships the patched copies.

Single-player and hosted games install it the same way.

One wart, inherited from Loot Vacuum: `Localization.csv` is not synced, so on a dedicated server clients see raw key names until a client-side, XML-only CSV modlet is added.

## Config

`mod/droneautomation.xml`, one section per module. Same pacing knobs as the Loot Vacuum block.

## Build

```
DOTNET_ROOT=$HOME/.dotnet ~/.dotnet/dotnet build -c Release
```

Builds the DLL, refreshes `mod/`, and syncs into the game's `Mods/DroneAutomation`. Set `GAME_DIR` (or `-p:GameDir=...`) if the game isn't in one of the usual Steam paths; the deploy step is skipped when it isn't found. Then `./package.sh` for a release zip.

### XML validation

```
python3 tools/validate_xml.py
```

Resolves every xpath in `mod/Config/` against the game's shipped `Data/Config` and fails if one matches nothing. `package.sh` runs it before zipping.

This exists because **the game does not fail a bad patch** — it drops it and logs one `WRN XML patch ... did not apply` line that nobody reads. That's how trader stock shipped broken for three versions. Run it before every release.

## Licence

MIT.
