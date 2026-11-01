**Drone Automation Mods**\
*A pack of junk-drone modules for 7 Days to Die V3.0.0 through V3.4.0 that automate your chores while the drone follows you.*

**Description**

Your junk drone already tags along everywhere — so put it to work. This is a pack of installable drone modules that do your grunt work as you explore: looting, salvaging, farming and repairs, all handled by the drone at your side.

Each module does the work **as if you did it yourself** — your loot stage, your perks and your tool bonuses all apply, and each target takes about the time it would take you by hand. Nothing is free or instant; the drone just saves you the clicks.

Every module is a separate craftable drone attachment, unlocked by a looted schematic (just like the vanilla drone mods), and carries a **Quality 1–6** level that scales its reach and speed. Crafted quality follows your **Robotics** crafting skill — the Q1 baseline below Robotics 80, climbing to Q6 at 100 — and traders sell copies rolled by their own stage, so a high-stage trader can beat what you can build.

Best of all, it is **100% server-side**. Install it on the server (or your single-player game) and every client connects with nothing to download.

**Main Features**

**Automation modules** — the drone does the chore:
- **Auto-Loot** — opens zombie loot bags and containers and picks up dropped items into the drone's storage. A mobile Loot Vacuum: bags and dropped items within 15m, block containers within 8m.
- **Auto-Salvage** — wrenches nearby salvageable blocks (cars, sinks, machines) apart one step at a time into the drone's bag. **You choose what it takes:** it starts with nothing picked, and its page in the talk menu lists the kinds of thing it may wrench — vehicles, appliances and electrics, lights, sinks toilets and pipes, furniture decor and signs, emptied containers, everything else. **Unclaimed ground only** — never inside any land claim, so it can't wreck your base or a neighbour's.
- **Auto-Harvest** — reaps grown crops in **your own land claim** and replants the seed. A plot is only reaped if it can be replanted, so it is never left empty.
- **Auto-Repair** — repairs damaged blocks in **your own land claim**, paying the repair materials out of the drone's bag. Only repairs a block it can pay for in full.
- **Auto-Plant** — sows young crops from the drone's bag onto empty farm plots in **your own land claim**. Pairs with Auto-Harvest — the crops it reaps become the seed Auto-Plant sows, for a self-running farm. Only ever plants on a real farm plot, never on bad ground.
- **Auto-Defense** — the drone becomes a **bodyguard**: it turns to face the nearest hostile it has a clear line to, in any direction, and fires its own machine gun as it follows you, or stands **sentry** over the spot you park it on. Kills are credited to you (XP and quests), and it never targets you, your allies, party members or traders. This drives the combat rig the junk drone has always carried but never used — the vanilla drone ships a machine gun and an attack state that The Fun Pimps left disconnected — so the drone's own hitscan and muzzle flash apply, rather than faked damage. Vanilla clients see the muzzle flash with nothing installed. **It needs ammo:** put **Robotic Turret Ammo** in the drone's bag and it loads its own magazine (62–92 rounds), tops up between fights, and tells you in chat when it is on its last magazine or dry. Its shots draw screamers like a turret's. Server owners can hand out the free gun instead (*RequireAmmo="0"*).

**Enhancement modules** — no chore of their own; they **make the other modules better** (each takes one of the drone's limited mod slots, so slotting one trades against fitting another automation core — breadth vs power, by design):
- **Overclock** — speeds up **every** installed automation module (cuts each action's time). Boost scales with its own Quality 1–6.
- **Wide-Band Antenna** — widens the working range (horizontal + vertical) of every installed automation module. Boost scales with its own Quality 1–6.

**Patrol** — hold **E** on the drone, pick **Talk**, then **Patrol around me**. The drone roams up to 20 m around you, facing where it is going. With Auto-Defense fitted and loaded it also **hunts**: anything hostile it can see within 28 m of you, it flies at, stops about 6 m off and shoots, then goes back to roaming. Without Auto-Defense it only roams, and comes back to you when a hostile shows up. It waits while you look straight at it, so you can walk up and talk to it, holds still while its menu or bag is open, and doesn't start fights while you crouch. **Stop patrolling** on the same page turns it off. Any drone can do it: it is an order, not a module.

**One page per module** — hold **E** on the drone, pick **Talk**, then **Automation modules…**. Every function gets a row saying whether it is running:

- *Auto-Loot: ON - change settings* — opens that module's own page: its ON/OFF switch first, then whatever settings it has. A module switched off stays in its slot and keeps its quality, so putting it back costs nothing.
- *Auto-Loot: no module installed* — that function has no module fitted in the drone you're standing at.

Auto-Salvage's page has its groups and a **More** page: salvage all of it or nothing, never take the last thing it took again, tell me what it took, undo my last skip. If Auto-Defense is low on or out of ammo the list says so.

The settings survive relogs and server restarts. They work on a **vanilla client with nothing installed**: the server pushes the menu itself. (A hotkey or a radial-wheel entry would have needed a client-side download, which this mod doesn't do.) The switches are per player rather than per drone, so running two drones switches a function off on both.

**Updating from 0.11.0 or older?** Auto-Salvage stops until you pick what it takes. **Salvage all of it** on its More page brings the old behaviour back in one click, and server owners can start everyone on everything with *StartTargets="all"* on the *autoSalvage* line.

**Built to be fair and safe:**
- Scope rules are deliberate — the destructive module (Auto-Salvage) stays off claimed ground; Auto-Harvest, Auto-Repair and Auto-Plant only ever act inside *your own* claim.
- Paced off your real action time and capped, so a reload can't burst a backlog of work.
- Modules stack with each other and with the vanilla cargo mod.
- Sold by traders and dropped as schematics, rolled by the same loot the vanilla drone mods use.

**Requirements**

- **7 Days to Die V3.0.0 through V3.4.0**
- **Server-side install** — only the server (or single-player host) needs the mod; clients install nothing.
- **A dedicated server keeps EasyAntiCheat ON** — a dedicated server does not gate mod DLLs on anti-cheat. Clients keep EAC on too. **Single-player and hosted games need EAC off**, because there the server runs inside your own EAC-protected game process.
- No other mods required. Uses the game's built-in Harmony (The Fun Pimps ship it in *Mods/0_TFP_Harmony*, loaded automatically before every other mod).

**Installation**

- Download and unzip.
- Copy the **DroneAutomation** folder into your server's **Mods/** directory (create **Mods/** if it doesn't exist).
- Leave **EasyAntiCheat** as it is — a dedicated server loads this with EACEnabled="true".
- That's it. Single-player and hosted games install the exact same way — just drop it in your own **Mods/** folder — but launch those with **EAC off**, because there the server runs inside your own game process.

**Clients:** install nothing and can keep EAC on. The server ships the patched item, recipe and loot data automatically.

*Note: on a dedicated server, localization text is not auto-synced, so clients may briefly see raw key names on the new items until a client-side text modlet is added. Functionality is unaffected.*

**Settings**

Every knob lives in *droneautomation.xml* — ranges, action times, the quality curves, and per-module toggles.

That file is **ours**: updating the mod overwrites it, which is how new settings and their docs reach you. So put your own tuning in *droneautomation.local.xml* beside it instead — list only the values you want different and they win over the shipped ones:

```xml
<droneautomation MaxOwnerDistance="60">
  <autoLoot Radius="12" />
</droneautomation>
```

No local file ships in the download, so nothing ever writes over yours. A malformed one is logged and skipped, never fatal.

**How It Works**

Everything runs server-side through a single Harmony patch on the drone's update loop. The drone already carries its owner, a bag and a lock, so a module ticks wherever the drone follows you with nothing new synced across the network. Loot containers and the drone's bag stay client-authoritative, so every module politely pauses while you have the drone's storage open, and refuses locked or other-player targets. There is no custom network packet.

**Credits**

- **Author:** tehAon
- **Built with:** Harmony — uses the game's own copy (The Fun Pimps' 0_TFP_Harmony); nothing extra bundled
- **Languages:** English, Russian, Simplified Chinese
- **Simplified Chinese translation:** [catgodcx](https://forums.nexusmods.com/profile/194393089-catgodcx/)
- Split out of the Loot Vacuum mod, whose placeable storage-vacuum block stays there.
- **Licence:** MIT

**Bugs, help & updates**\
Hit a bug or want update news? Post it on the **Bugs** tab here, or join the tehAon modding Discord — the hub for all my 7 Days to Die mods:\
[**discord.gg/DYCzCPSvwa**](https://discord.gg/DYCzCPSvwa)

By **tehAon** — [more of my 7 Days to Die mods](https://next.nexusmods.com/profile/tehAon) • MIT licensed
