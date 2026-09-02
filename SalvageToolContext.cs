using HarmonyLib;

namespace DroneAutomation
{
    /// <summary>
    /// Makes Auto-Salvage count as "holding a salvage tool" for the drop-count maths, and only for
    /// that.
    ///
    /// Vanilla splits the salvage bonuses in two. The Salvage Operations perk raises HarvestCount
    /// with no strings attached, so the drone has always had it. But Hacker's candy (+20%) and the
    /// Scavenger Gloves (a tier-scaled chance of one extra drop) hang their HarvestCount off
    /// &lt;requirement name="HoldingItemHasTags"/&gt;, and that requirement reads the OWNER's held
    /// item - so the drone only got them in the accident where you happened to be holding your
    /// wrench when its tick landed, and never at all if you were holding a rifle.
    ///
    /// The requirement cannot be satisfied honestly: the only way to change what it reads is to put
    /// a wrench in the player's hands, which is their inventory, not ours. So the check itself is
    /// patched, inside a window one call wide - armed immediately before the drone banks a block's
    /// salvage and disarmed in a finally straight after. The game then does its own maths with its
    /// own numbers, which is the point: the candy, the gloves, anything TFP adds later and anything
    /// an overhaul defines all land without this mod knowing they exist.
    ///
    /// Three things keep the window honest. The tick loop is single-threaded, so nothing else can
    /// be asking during it. An inverted requirement ("while NOT holding one") is left alone, since
    /// pretending would flip an effect on that is meant to be off. And the requirement's tags have
    /// to be the ones a salvage tool actually carries - see <see cref="ToolTags"/> - so a bonus
    /// gated on an axe or a mining tool cannot ride along on a salvage query.
    /// </summary>
    public static class SalvageToolContext
    {
        /// <summary>
        /// The tags vanilla's wrench, ratchet and impact driver share and their bonuses ask for:
        /// perkSalvageOperations (Hacker's candy), salvagingSkill (Scavenger Gloves), salvageTool.
        /// Overridable from droneautomation.xml for an overhaul that renames them - a name this mod
        /// does not know simply misses the bonus, rather than granting the wrong one.
        /// </summary>
        public const string DefaultToolTags = "perkSalvageOperations,salvagingSkill,salvageTool";

        private static string tagSource;
        private static FastTags<TagGroup.Global> tags;

        /// <summary>True only while a drone is banking one block's salvage. See the class remarks.</summary>
        public static bool Armed { get; private set; }

        /// <summary>
        /// Arms the pretence for the drop-count call that follows. ALWAYS pair with
        /// <see cref="Disarm"/> in a finally: leaving it armed would hand every held-item
        /// requirement on the server a free pass.
        /// </summary>
        public static void Arm(string _toolTags)
        {
            if (tagSource != _toolTags)
            {
                tagSource = _toolTags;
                tags = FastTags<TagGroup.Global>.Parse(string.IsNullOrEmpty(_toolTags) ? DefaultToolTags : _toolTags);
            }
            Armed = true;
        }

        public static void Disarm()
        {
            Armed = false;
        }

        /// <summary>
        /// True when a salvage tool would satisfy this requirement - matched with the requirement's
        /// own all-of/any-of semantics against the tags a salvage tool carries.
        /// </summary>
        public static bool SalvageToolWouldSatisfy(FastTags<TagGroup.Global> _required, bool _needsAll)
        {
            return _needsAll ? tags.Test_AllSet(_required) : tags.Test_AnySet(_required);
        }
    }

    /// <summary>
    /// The one-call-wide patch described in <see cref="SalvageToolContext"/>. Only ever turns a
    /// "no" into a "yes", never the reverse, and only while <see cref="SalvageToolContext.Armed"/>.
    /// </summary>
    [HarmonyPatch(typeof(HoldingItemHasTags), nameof(HoldingItemHasTags.IsValid))]
    public static class HoldingItemHasTagsPatch
    {
        public static void Postfix(HoldingItemHasTags __instance, ref bool __result)
        {
            if (__result || !SalvageToolContext.Armed || __instance == null) return;

            // "While NOT holding one" is not ours to satisfy - pretending would switch on an effect
            // that is meant to be off while you hold a wrench.
            if (__instance.invert) return;

            if (!SalvageToolContext.SalvageToolWouldSatisfy(__instance.holdingItemTags, __instance.hasAllTags)) return;

            __result = true;
        }
    }
}
