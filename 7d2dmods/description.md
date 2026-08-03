**Drone Automation Mods**\
*A pack of junk-drone modules for 7 Days to Die V3.0 that automate your chores while the drone follows you.*

**Description**

Your junk drone already tags along everywhere — so put it to work. This is a pack of installable drone modules that do your grunt work as you explore: looting, salvaging, farming and repairs, all handled by the drone at your side.

Each module does the work **as if you did it yourself** — your loot stage, your perks and your tool bonuses all apply, and each target takes about the time it would take you by hand. Nothing is free or instant; the drone just saves you the clicks.

Every module is a separate craftable drone attachment, unlocked by a looted schematic (just like the vanilla drone mods), and carries a **Quality 1–6** level that scales its reach and speed. Crafting always makes the Quality 1 baseline — stronger, faster, longer-reach copies come from loot or the trader.

Best of all, it is **100% server-side**. Install it on the server (or your single-player game) and every client connects with nothing to download.

**Main Features**

**Automation modules** — the drone does the chore:

**Enhancement modules** — no chore of their own; they **make the other modules better** (each takes one of the drone's limited mod slots, so slotting one trades against fitting another automation core — breadth vs power, by design):

**Built to be fair and safe:**

**Requirements**

**Installation**

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

**Bugs, help & updates**\
Hit a bug or want update news? Post it on the **Bugs** tab here, or join the tehAon modding Discord — the hub for all my 7 Days to Die mods:\
[**discord.gg/DYCzCPSvwa**](https://discord.gg/DYCzCPSvwa)

By **tehAon** — [more of my 7 Days to Die mods](https://next.nexusmods.com/profile/tehAon) • MIT licensed
