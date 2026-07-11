# Changelog

## 0.4.0

- **Auto-Plant** (`modRoboticDroneAutoPlantMod`) — a new automation module: the drone sows young crop blocks from its own bag onto empty farm plots in your land claim. A "plantable" bag item is any that resolves to a growing-plant block (e.g. `plantedCorn1`) — the same block Auto-Harvest deposits when it reaps, so the two modules form a self-running farm: harvest fills the bag with seed crops, plant spends them refilling empty plots. Only ever plants on the air cell above a farm plot inside your own claim, so it can't sow on invalid ground or a neighbour's farm.
- **Overclock** (`modRoboticDroneOverclockMod`) — the first *enhancement* module: it does no work of its own, but speeds up **every** other automation module installed in the drone (cuts each core's per-action time). The boost scales with its own Quality 1-6.
- **Wide-Band Antenna** (`modRoboticDroneAntennaMod`) — an enhancement module that widens the working range of every other automation module (horizontal and vertical reach). The boost scales with its own Quality 1-6.
- Enhancement modules use one of the drone's limited mod slots, so slotting one trades against fitting another automation core — breadth vs power, by design. Both are quality-scaled, craftable from a looted schematic, and sold by traders, like every other module.

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
