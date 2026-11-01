#!/usr/bin/env python3
"""Writes mod/Config/dialogs.xml - the file is GENERATED, edit this and rerun it instead of editing
the XML by hand. Run from the repo root: python3 tools/gen_dialogs.py

The Automation modules list shows each module "ON/OFF - change settings" and leads to one page per
module (0.12.0); the drone's first page also gets the Patrol pair (0.13.0)."""

MODULES = ["Loot", "Salvage", "Harvest", "Repair", "Plant", "Defense"]
PAGE = {m: f"da{m}Page" for m in MODULES}
PAGE["Salvage"] = "daSalvTargets"          # the Auto-Salvage page keeps its id
GROUPS = ["Vehicles", "Appliances", "Lights", "Plumbing", "Furniture", "Containers", "Other"]

HEADER = """<?xml version="1.0" encoding="UTF-8"?>
<config>
	<!--
	  The drone's "Automation modules" talk menu: switch a module off without pulling it out of the
	  drone, and change what it does.

	  WHY THE TALK MENU AND NOT A HOTKEY OR THE RADIAL WHEEL. Both of those are client-side.
	  EntityDrone.InitLocalActivationCommands, AllowActivationCommand and OnEntityActivated all take
	  an EntityPlayerLocal and run only on the machine holding the camera, and key bindings are
	  client config - so adding a wheel entry or a keybind would mean shipping a client DLL and
	  ending this mod's "server installs it, players install nothing" contract.

	  The wheel's existing "talk" entry is the way in. It opens the XML dialog junkDrone, and
	  WorldStaticData registers both dialogs.xml and buffs.xml with _sendToClients: true - the server
	  pushes its patched copy to every vanilla client, which parses it locally. So a server-side
	  modlet can put rows in that menu, one click deeper than the wheel itself.

	  SHAPE (0.12.0). The list shows one row per module - "Auto-Loot: ON - change settings" - and
	  each leads to that module's own page: its ON/OFF switch, then whatever settings it has (the
	  salvage groups on Auto-Salvage's, the ammo state on Auto-Defense's). Until 0.12.0 the switches
	  sat on the list itself with a "switch every module back on" row, and the list ran out of room
	  the moment Auto-Salvage needed settings. Ammo low / empty is ALSO shown on the list, because a
	  drone that has stopped shooting is not something to find out about on a sub-page.

	  HOW A ROW KNOWS ITS STATE. XUiC_DialogResponseEntry evaluates each response's requirements
	  against the LOCAL player and, on a failed requirement marked requirementtype="Hide", drops the
	  row entirely. Requirements on one response are ANDed - the loop stops at the first failure - so
	  each module gets THREE mutually exclusive rows on the list and exactly one is ever visible:

	    daXxxOpenOn    fitted, running      -> "ON - change settings"  (opens the page)
	    daXxxOpenOff   fitted, switched off -> "OFF - change settings" (opens the page)
	    daXxxMissing   no module fitted     -> "no module installed"   (inert)

	  and its page has the switch pair daXxxDisable / daXxxEnable. Clicking a switch adds the buff
	  that writes the CVar (see buffs.xml), and nextstatementid brings you back to the same page
	  with the row already flipped.

	  WHY A "no module installed" ROW EXISTS. Without it the menu offers a switch for every function
	  whether or not the drone carries that module, which reads as "these are settings" rather than
	  "these are modules you have to find, craft and fit" - a new player could reasonably conclude the
	  hardware is optional. It cannot be done in XML alone: a dialog requirement can only test PLAYER
	  state, and nothing in the dialog carries the respondent drone's entity id, so DronePatch has to
	  publish daXxxMissing onto the player for the drone you are standing at (PublishFittedModules).

	  POLARITY, both flags. The CVar means the ABNORMAL state - Off means switched off, Missing means
	  not fitted - because EntityBuffs.GetCustomVar returns 0 for a name it has never seen and
	  EntityBuffs.Write drops any CVar sitting at 0. So an unwritten flag has to mean "fitted and
	  running", which is what a player sees before the drone has ever reported in. A "daXxxHas" flag
	  would invert that and hide every switch until the server got a word in.

	  TEXT IS A LOCALIZATION KEY (daDialog*), AND THE ROWS LIVE IN Config/Localization.csv. Until
	  0.10.4 the English sentence was written here in place of a key, on the reasoning that
	  Localization.csv is not in the list of files pushed to clients. That is true and beside the
	  point: it is not an XML and travels on its own channel. GameManager.RequestToEnterGame sends
	  NetPackageLocalization - every mod-added row, every language column - immediately BEFORE
	  WorldStaticData.SendXmlsToClient, and DialogFromXml resolves text="..." through
	  Localization.Get when the client parses this file, so the keys exist by then. A language with
	  an empty cell falls back to the english column. Add a language by adding a column to the CSV;
	  nothing here changes. The same fact is why a row can never NAME a block: its text is fixed at
	  parse time. See the More page.

	  THE SWITCHES ARE PER PLAYER, NOT PER DRONE. DialogActionAddBuff applies to the talking player
	  and CVars live on the player, and nothing in the dialog carries the drone's entity id, so a
	  player running two drones switches a function off on both. That is the honest limit of the
	  no-client-install route; per-drone state would need the server to guess which drone you meant.
	  The "no module installed" rows ARE per drone, because the server publishes them for whichever
	  drone you are standing at - so those rows and the switches answer subtly different questions.

	  ROW BUDGET: windowResponses is a 10-row grid with no scrollbar, and an eleventh row is silently
	  invisible. junkDrone's start statement shows 6-7 rows and this adds two (Patrol, Automation
	  modules) = 9 at most. The list: 6 module rows + at most one ammo warning + back = 8. Auto-Salvage's
	  page: switch + 7 groups + More + back = 10, FULL. Auto-Defense's: switch + ammo + back = 3. The
	  others: switch + back = 2. More: 6.
	-->

	<!-- Sits directly above "Never mind", so the vanilla drone commands keep their order. -->
	<insertBefore xpath="/dialogs/dialog[@id='junkDrone']/statement[@id='start']/response_entry[@id='done']">
		<response_entry id="daAutomation"/>
	</insertBefore>

	<!-- Patrol (PatrolCore), right under vanilla's Follow me / Stay here, because it is an order of
	     the same kind. Shown only while the drone is following: patrol only ever acts then, and a
	     parked drone has a Follow me row to press first. daPatrol is per player, like the switches. -->
	<insertBefore xpath="/dialogs/dialog[@id='junkDrone']/statement[@id='start']/response_entry[@id='dont_heal_allies']">
		<response_entry id="daPatrolStart"/>
		<response_entry id="daPatrolStop"/>
	</insertBefore>

	<append xpath="/dialogs/dialog[@id='junkDrone']">
"""

NAMES = {"Loot": "Auto-Loot", "Salvage": "Auto-Salvage", "Harvest": "Auto-Harvest",
         "Repair": "Auto-Repair", "Plant": "Auto-Plant", "Defense": "Auto-Defense"}

o = [HEADER]
def w(s=""): o.append(s + "\n")

# --- the list ---
w('\t\t<statement id="daAutomation" text="daDialogPrompt">')
for m in MODULES:
    for r in ("OpenOn", "OpenOff", "Missing"):
        w(f'\t\t\t<response_entry id="da{m}{r}"/>')
w('\t\t\t<response_entry id="daDefenseAmmoLowWarn"/>')
w('\t\t\t<response_entry id="daDefenseAmmoEmptyWarn"/>')
w('\t\t\t<response_entry id="daBack"/>')
w('\t\t</statement>')
w()
w('\t\t<response id="daAutomation" text="daDialogOpen" nextstatementid="daAutomation"/>')
w()
w('\t\t<!-- Patrol: the pair on the first page. Back to the same page, the row already flipped. -->')
for r, v in (("Start", 0), ("Stop", 1)):
    w(f'\t\t<response id="daPatrol{r}" text="daDialogPatrol{r}" nextstatementid="start">')
    w('\t\t\t<requirement type="DroneState" value="Follow" requirementtype="Hide"/>')
    w(f'\t\t\t<requirement type="CheckCVar" id="daPatrol" value="{v}" requirementtype="Hide"/>')
    w(f'\t\t\t<action type="AddBuff" id="buffDaPatrol{r}"/>')
    w('\t\t</response>')
w()
for m in MODULES:
    w(f'\t\t<!-- {NAMES[m]}: list rows -->')
    w(f'\t\t<response id="da{m}OpenOn" text="daDialog{m}OpenOn" nextstatementid="{PAGE[m]}">')
    w(f'\t\t\t<requirement type="CheckCVar" id="da{m}Missing" value="0" requirementtype="Hide"/>')
    w(f'\t\t\t<requirement type="CheckCVar" id="da{m}Off" value="0" requirementtype="Hide"/>')
    w('\t\t</response>')
    w(f'\t\t<response id="da{m}OpenOff" text="daDialog{m}OpenOff" nextstatementid="{PAGE[m]}">')
    w(f'\t\t\t<requirement type="CheckCVar" id="da{m}Missing" value="0" requirementtype="Hide"/>')
    w(f'\t\t\t<requirement type="CheckCVar" id="da{m}Off" value="1" requirementtype="Hide"/>')
    w('\t\t</response>')
    w(f'\t\t<response id="da{m}Missing" text="daDialog{m}Missing" nextstatementid="daAutomation">')
    w(f'\t\t\t<requirement type="CheckCVar" id="da{m}Missing" value="1" requirementtype="Hide"/>')
    w('\t\t</response>')
w('\t\t<!-- Ammo low / empty, published by the drone as daDefenseAmmo (2 low, 3 empty), shown on the')
w('\t\t     list too so a drone that has stopped shooting is never news on a sub-page. Opens the page. -->')
w('\t\t<response id="daDefenseAmmoLowWarn" text="daDialogDefenseAmmoLow" nextstatementid="daDefensePage">')
w('\t\t\t<requirement type="CheckCVar" id="daDefenseAmmo" value="2" requirementtype="Hide"/>')
w('\t\t</response>')
w('\t\t<response id="daDefenseAmmoEmptyWarn" text="daDialogDefenseAmmoEmpty" nextstatementid="daDefensePage">')
w('\t\t\t<requirement type="CheckCVar" id="daDefenseAmmo" value="3" requirementtype="Hide"/>')
w('\t\t</response>')
w()
w('\t\t<response id="daBack" text="daDialogBack" nextstatementid="start"/>')
w('\t\t<response id="daModuleBack" text="daDialogBack" nextstatementid="daAutomation"/>')
w()

# --- simple pages ---
for m in ("Loot", "Harvest", "Repair", "Plant", "Defense"):
    w(f'\t\t<!-- {NAMES[m]}: its page -->')
    w(f'\t\t<statement id="{PAGE[m]}" text="daDialog{m}About">')
    w(f'\t\t\t<response_entry id="da{m}Disable"/>')
    w(f'\t\t\t<response_entry id="da{m}Enable"/>')
    if m == "Defense":
        for a in ("Plenty", "Low", "Empty"):
            w(f'\t\t\t<response_entry id="daDefenseAmmo{a}"/>')
    w('\t\t\t<response_entry id="daModuleBack"/>')
    w('\t\t</statement>')
    w()

# --- the switches (every module, Salvage's included) ---
for m in MODULES:
    w(f'\t\t<!-- {NAMES[m]}: switch -->')
    w(f'\t\t<response id="da{m}Disable" text="daDialog{m}On" nextstatementid="{PAGE[m]}">')
    w(f'\t\t\t<requirement type="CheckCVar" id="da{m}Missing" value="0" requirementtype="Hide"/>')
    w(f'\t\t\t<requirement type="CheckCVar" id="da{m}Off" value="0" requirementtype="Hide"/>')
    w(f'\t\t\t<action type="AddBuff" id="buffDa{m}Disable"/>')
    w('\t\t</response>')
    w(f'\t\t<response id="da{m}Enable" text="daDialog{m}Off" nextstatementid="{PAGE[m]}">')
    w(f'\t\t\t<requirement type="CheckCVar" id="da{m}Missing" value="0" requirementtype="Hide"/>')
    w(f'\t\t\t<requirement type="CheckCVar" id="da{m}Off" value="1" requirementtype="Hide"/>')
    w(f'\t\t\t<action type="AddBuff" id="buffDa{m}Enable"/>')
    w('\t\t</response>')
w('\t\t<!-- Ammo, published by the drone as daDefenseAmmo: 1 plenty, 2 low, 3 empty. 0 (ammo off,')
w('\t\t     or no module) shows none of them. Picking one just redraws the page. -->')
for v, a in ((1, "Plenty"), (2, "Low"), (3, "Empty")):
    w(f'\t\t<response id="daDefenseAmmo{a}" text="daDialogDefenseAmmo{a}" nextstatementid="daDefensePage">')
    w(f'\t\t\t<requirement type="CheckCVar" id="daDefenseAmmo" value="{v}" requirementtype="Hide"/>')
    w('\t\t</response>')
w()

# --- Auto-Salvage's page ---
w('\t\t<!-- Auto-Salvage: its page. The switch, then which kinds of thing it may take')
w('\t\t     (SalvageCategories): one CVar per group, 1 = take, so an unwritten CVar - every player on')
w('\t\t     update - reads as "leave it" and the module starts empty. Each group is a pair of rows')
w('\t\t     guarded by opposite values; exactly one shows. 10 rows: FULL. -->')
w('\t\t<statement id="daSalvTargets" text="daDialogSalvPrompt">')
w('\t\t\t<response_entry id="daSalvageDisable"/>')
w('\t\t\t<response_entry id="daSalvageEnable"/>')
for g in GROUPS:
    w(f'\t\t\t<response_entry id="daSalv{g}Leave"/>')
    w(f'\t\t\t<response_entry id="daSalv{g}Take"/>')
w('\t\t\t<response_entry id="daSalvMore"/>')
w('\t\t\t<response_entry id="daModuleBack"/>')
w('\t\t</statement>')
w()
w("""		<!-- The More page: every group at once, and the per-item rows. A talk-menu row's text is
		     fixed when the client parses this file, so the menu can never NAME a block - it cannot
		     list what the drone took, and it cannot offer a search. What it can do is act on "the
		     last thing it took", which the server knows (SalvageLedger), and say the name in chat.
		     So these three rows each set a one-shot request CVar; SalvageMenu acts on it on the
		     drone's next tick, answers in chat, and sets it back to 0. Same as /das skip 1, /das last
		     and /das undo, without typing. 6 rows. -->
		<statement id="daSalvMore" text="daDialogSalvMorePrompt">
			<response_entry id="daSalvAllTake"/>
			<response_entry id="daSalvAllLeave"/>
			<response_entry id="daSalvSkipLast"/>
			<response_entry id="daSalvListLast"/>
			<response_entry id="daSalvUndoSkip"/>
			<response_entry id="daSalvMoreBack"/>
		</statement>

		<response id="daSalvMore" text="daDialogSalvMore" nextstatementid="daSalvMore"/>
		<response id="daSalvSkipLast" text="daDialogSalvSkipLast" nextstatementid="daSalvMore">
			<action type="AddBuff" id="buffDaSalvCmdSkipLast"/>
		</response>
		<response id="daSalvListLast" text="daDialogSalvListLast" nextstatementid="daSalvMore">
			<action type="AddBuff" id="buffDaSalvCmdListLast"/>
		</response>
		<response id="daSalvUndoSkip" text="daDialogSalvUndoSkip" nextstatementid="daSalvMore">
			<action type="AddBuff" id="buffDaSalvCmdUndoSkip"/>
		</response>
		<response id="daSalvMoreBack" text="daDialogBack" nextstatementid="daSalvTargets"/>
""")
for g in GROUPS:
    w(f'\t\t<response id="daSalv{g}Leave" text="daDialogSalv{g}On" nextstatementid="daSalvTargets">')
    w(f'\t\t\t<requirement type="CheckCVar" id="daSalv{g}" value="1" requirementtype="Hide"/>')
    w(f'\t\t\t<action type="AddBuff" id="buffDaSalv{g}Leave"/>')
    w('\t\t</response>')
    w(f'\t\t<response id="daSalv{g}Take" text="daDialogSalv{g}Off" nextstatementid="daSalvTargets">')
    w(f'\t\t\t<requirement type="CheckCVar" id="daSalv{g}" value="0" requirementtype="Hide"/>')
    w(f'\t\t\t<action type="AddBuff" id="buffDaSalv{g}Take"/>')
    w('\t\t</response>')
w('\t\t<response id="daSalvAllTake" text="daDialogSalvAllOn" nextstatementid="daSalvTargets">')
w('\t\t\t<action type="AddBuff" id="buffDaSalvAllTake"/>')
w('\t\t</response>')
w('\t\t<response id="daSalvAllLeave" text="daDialogSalvAllOff" nextstatementid="daSalvTargets">')
w('\t\t\t<action type="AddBuff" id="buffDaSalvAllLeave"/>')
w('\t\t</response>')
w()
w('\t</append>')
w('</config>')

open("mod/Config/dialogs.xml", "w", encoding="utf-8").write("".join(o))
print("wrote dialogs.xml")
