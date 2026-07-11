# Drone Automation Mods

A pack of installable junk-drone modules for 7 Days to Die **V 3.0** that automate chores while the drone follows you. Each module is a separate craftable drone attachment, unlocked by a looted schematic (like the vanilla drone mods), and each does the work **as if you did it yourself** — your loot stage, perks and tool bonuses apply, and each target takes about the time it would take you by hand.

Split out of the [Loot Vacuum](../LootVacuum) mod, whose placeable storage-vacuum block stays there.

## Modules

- **Auto-Loot** (`modRoboticDroneAutoLootMod`) — opens zombie loot bags and containers and picks up dropped items, into the drone's storage. A mobile version of the Loot Vacuum block: bags and dropped items within 15m, block containers within 8m, 6m vertical.
- **Auto-Salvage** (`modRoboticDroneAutoSalvageMod`) — wrenches nearby salvageable blocks (cars, sinks, machines) one downgrade step at a time into the drone bag. **Unclaimed ground only** — never inside anyone's land claim, so it can't wreck your base or a neighbour's. Reach is kept small by default because it destroys blocks.
- **Auto-Harvest** (`modRoboticDroneAutoHarvestMod`) — reaps grown crops in **your own land claim** and replants the seed, into the drone bag. A crop is only reaped if its replant stage can be resolved, so a plot is never left empty.
- **Auto-Repair** (`modRoboticDroneAutoRepairMod`) — repairs damaged blocks in **your own land claim**, paying the repair materials out of the drone's bag. Only repairs a block it can pay for in full.

Scope rules are deliberate: Auto-Salvage is destructive so it stays off claimed ground; Auto-Harvest and Auto-Repair only ever act inside your own claim.

Install a module in a junk drone like any other drone mod. Modules stack with each other and with the vanilla cargo mod.

## How it works

Everything runs server-side via one Harmony postfix on `EntityDrone.OnUpdateEntity`. The drone already carries an `OwnerID`, a `Bag` and a lock, and `World.cs` exempts drones from the chunk-loaded check, so a module ticks wherever the drone follows you, with nothing new synced. Each target is paced off the owner's real action time and banked against a `MaxCatchupSeconds` cap so a reload cannot burst.

## Unlocking

Each module recipe is `learnable` — hidden until you find and read its schematic, which drops from the same loot as the vanilla drone-mod schematics. Then craft it at a workbench.

## Multiplayer

Works in single-player, on a host, and on a dedicated server, and it is **server-side only**. The drone's bag and loot containers are client-authoritative, so every module pauses while the owner has the drone's storage open, and refuses locked or other-player targets. There is no custom network packet.

If a module is not acting, set `Debug="1"` on the `<droneautomation>` line in `mod/droneautomation.xml` (on by default in current builds) and watch the log for `[DroneAutomation][drone <id>]` — it says, throttled, exactly which check is stopping it.

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

Builds the DLL, refreshes `mod/`, and syncs into the game's `Mods/DroneAutomation`. Then `./package.sh` for a release zip.

## Licence

MIT.
