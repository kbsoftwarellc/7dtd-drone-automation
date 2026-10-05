using System;
using System.Linq.Expressions;
using System.Reflection;

namespace DroneAutomation
{
    /// <summary>
    /// The mods fitted to an item, read the way the running game stores them.
    ///
    /// Up to game 3.2 they are the public array ItemValue.Modifications. Game 3.3 made that array
    /// private and reads it through ItemValue.ModificationCount and ItemValue.GetModification(i).
    /// A DLL holding a compiled reference to either form cannot run on the other game - and Mono
    /// refuses to compile the whole METHOD that holds it, so the failure surfaces at the call site
    /// and a try/catch inside that method never runs. So neither is referenced: both accessors are
    /// resolved once, by name, to whichever the running game has.
    ///
    /// Neither accessor is ever null. If the game has neither form, calling one throws
    /// MissingMemberException and <see cref="Missing"/> names what was looked for; InitMod reads it
    /// and switches the mod off rather than let every drone tick throw.
    /// </summary>
    internal static class ModSlots
    {
        /// <summary>How many mod slots the item has (0 for an item that takes none).</summary>
        internal static readonly Func<ItemValue, int> Count;

        /// <summary>The mod in slot i; an empty ItemValue or null for a free slot.</summary>
        internal static readonly Func<ItemValue, int, ItemValue> At;

        /// <summary>"" when both accessors resolved, else what the running game does not have.</summary>
        internal static readonly string Missing = "";

        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static ModSlots()
        {
            try
            {
                ParameterExpression item = Expression.Parameter(typeof(ItemValue), "item");
                ParameterExpression index = Expression.Parameter(typeof(int), "index");

                PropertyInfo count = typeof(ItemValue).GetProperty("ModificationCount", INSTANCE);
                MethodInfo at = typeof(ItemValue).GetMethod("GetModification", INSTANCE, null, new[] { typeof(int) }, null);
                FieldInfo array = typeof(ItemValue).GetField("Modifications", INSTANCE);

                if (count != null && at != null && at.ReturnType == typeof(ItemValue))
                {
                    Count = Expression.Lambda<Func<ItemValue, int>>(Expression.Property(item, count), item).Compile();
                    At = Expression.Lambda<Func<ItemValue, int, ItemValue>>(Expression.Call(item, at, index), item, index).Compile();
                    return;
                }

                if (array != null && array.FieldType == typeof(ItemValue[]))
                {
                    Expression mods = Expression.Field(item, array);
                    Count = Expression.Lambda<Func<ItemValue, int>>(
                        Expression.Condition(Expression.Equal(mods, Expression.Constant(null, typeof(ItemValue[]))),
                                             Expression.Constant(0), Expression.ArrayLength(mods)), item).Compile();
                    At = Expression.Lambda<Func<ItemValue, int, ItemValue>>(Expression.ArrayIndex(mods, index), item, index).Compile();
                    return;
                }
            }
            catch (Exception) { /* reported below */ }

            Missing = "ItemValue.Modifications / ItemValue.GetModification()";
            Count = _ => throw new MissingMemberException(Missing + " cannot be read on this game build");
            At = (_, __) => throw new MissingMemberException(Missing + " cannot be read on this game build");
        }
    }
}
