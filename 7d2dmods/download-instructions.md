Download

File: `DroneAutomation_v0.8.1_gameV3.0.0-V3.1.zip`\
Built and verified for **7 Days to Die V3.0.0, V3.0.1 and V3.1**.

The zip contains one folder, `DroneAutomation`, holding `DroneAutomation.dll`, `ModInfo.xml`, `droneautomation.xml`, a `Config` folder, plus README, CHANGELOG and LICENSE.

This is a **server-side** mod. Only the server — or your own game, if you play single-player — needs the download. Other players connect with nothing to install.

Install

Requirements

- **7 Days to Die V3.0.0 through V3.1**
- **A dedicated server keeps EasyAntiCheat ON.** A dedicated server does not gate mod DLLs on anti-cheat, so both it and its clients stay protected. Only single-player and client-hosted games need EAC off, because there the server runs inside your own EAC-protected game process.
- No other mods needed. It uses the Harmony that The Fun Pimps ship with the game in `Mods/0_TFP_Harmony`, which loads before every other mod automatically.

Steps

1. Unzip the download. You should get one folder named **DroneAutomation** with `ModInfo.xml` directly inside it.

2. Copy that folder into your **Mods** folder, creating `Mods` if it isn't there. The game reads either of these:

   - **Single-player / client (Windows)** — `C:\Users\<YourName>\AppData\Roaming\7DaysToDie\Mods`, or the game folder itself, `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods`
   - **Dedicated server** — the `Mods` folder next to `7DaysToDieServer.exe` (`7DaysToDieServer.x86_64` on Linux)

   You want `Mods\DroneAutomation\ModInfo.xml` — **not** `Mods\DroneAutomation\DroneAutomation\ModInfo.xml`. One folder too deep is the most common install mistake.

3. Start it. A **dedicated** server needs no EAC change; **single-player or a hosted game** must be launched with **EasyAntiCheat OFF**, since the server is your own game process.

4. Confirm it loaded. The log should contain:

   `[DroneAutomation] InitMod complete — N method(s) patched, server-side only.`

   No line means the folder is nested one level too deep — or, if you are in single-player, that EAC is still on.

**Updating:** delete the old `DroneAutomation` folder and drop the new one in. Keep your own settings in `droneautomation.local.xml` (see Settings below) and an update never touches them.

**Uninstalling:** delete the `DroneAutomation` folder and restart. Modules already fitted to a drone disappear with the mod; nothing else in your world is affected.

Usage

Getting a module

1. Find and read the module's **schematic**. They drop from the same loot as the vanilla drone-mod schematics.
2. Craft the module at a **workbench**.
3. Install it in a junk drone exactly like any other drone mod.

Modules stack with each other and with the vanilla cargo mod, but the drone has a limited number of mod slots, so fitting one is a trade against the other mods you could have fitted. Traders also sell the finished modules.

What each module does

**Automation modules** — the drone does the chore, using the drone's own storage:

- **Auto-Loot** — opens zombie loot bags and containers and picks up dropped items. Bags and dropped items within 15m, block containers within 8m. It leaves anything you throw alone — a rock, a molotov, a grenade — while it is in the air, while it is burning or ticking, and for a few seconds after it lands, so a thrown rock still works as a decoy.
- **Auto-Salvage** — wrenches salvageable blocks (cars, sinks, machines) apart one step at a time. **Unclaimed ground only**, never inside anyone's land claim — yours included — and never inside a trader area. Leaves workstations alone by default, and never touches the switches, buttons, relays or pressure plates a POI needs to work.
- **Auto-Harvest** — reaps grown crops in **your own land claim** and replants the seed. A crop is only reaped if it can be replanted, so a plot is never left empty.
- **Auto-Repair** — repairs damaged blocks in **your own land claim**, paying the materials out of the drone's bag. Only repairs what it can pay for.
- **Auto-Plant** — sows young crops from the drone's bag onto empty farm plots in **your own land claim**. Pairs with Auto-Harvest — what one reaps, the other sows — for a self-running farm.
- **Auto-Defense** — turns the drone into a **bodyguard**. It fires its own machine gun at the nearest hostile while following you, or stands sentry over the spot you park it on. Kills are credited to you, and it never targets you, your allies, party members or traders.

**Enhancement modules** — no chore of their own, they improve the others:

- **Overclock** — speeds up every installed automation module.
- **Wide-Band Antenna** — widens the reach of every installed automation module.

Each module works **as if you did the job**: your loot stage, perks and tool bonuses apply, and each target takes about as long as it would by hand. Nothing is instant.

Parking the drone

Give the drone the vanilla **stay** command and the block modules work the ground around the spot you parked it on instead of around you. Park one in the farm and it keeps reaping and replanting while you are out looting; park one in the base and it keeps repairing. Send it back to following and it works around\
you again. Auto-Loot always works around the drone itself.

You still have to be online, but no longer nearby. A *following* drone only works blocks while it is actually with you (25m by default), so a drone left behind won't keep working from across the map.

Auto-Defense ignores all of that on purpose — it fights whether the drone is following or parked, and even while you have its storage open. A parked drone with Auto-Defense fitted is a stationary turret.

Module levels (Quality 1–6)

Quality scales a module's reach and speed, and for the enhancement modules how big a boost they give. It comes from **two** places:

- **Crafting** — follows your **Robotics crafting skill**. Below Robotics 80 you get the Quality 1 baseline; then Q2 at 80, Q3 at 85, Q4 at 90, Q5 at 95 and Q6 at 100. Robotics is a *crafting* skill, so it rises by reading **Tech Planet** magazines rather than by spending perk points — the Electrocutioner and Turrets perks both make those magazines drop more often.
- **Traders** — rolled by the trader's stage: Q2 from stage 45, up to Q6 from stage 65. Early game a trader only ever offers Quality 1.

**Loot is not a source.** Drone mods cannot drop as loot in V3 — only the schematics do.

The ranges quoted above are the Quality 6 ceilings; a Quality 1 module reaches less and works slower.

Settings

Every knob is in `droneautomation.xml` inside the mod folder: reach, action times, and the safety rules for each module. Each one is commented in place.

To keep your tuning through updates, put your changes in `droneautomation.local.xml` in the same folder instead. List only the values you want to change; they are read on top of the shipped ones and are never overwritten by an update:

`<droneautomation MaxOwnerDistance="60">`\
` <autoLoot Radius="12" />`\
`</droneautomation>`

Troubleshooting

If a module isn't acting, set `Debug="1"` on the `<droneautomation>` line in `droneautomation.xml` and watch the log for the `[DroneAutomation][drone]` lines, which say what it scanned and which check stopped it.

*Note for dedicated servers: localization text is not auto-synced, so clients may see raw key names on the new items until a client-side localization mod is installed. The mod itself functions normally.*
