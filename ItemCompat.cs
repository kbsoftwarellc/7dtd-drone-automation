using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace TehAon.Compat
{
    /// <summary>
    /// SHARED-CANONICAL: ItemCompat. Edit shared/ItemCompat.cs in the toolchain, then run
    /// tools/sync_shared.py --write; a fix made in one mod's copy is lost at the next sync.
    ///
    /// ITEMCOMPAT_VERSION: 1
    ///
    /// Item members whose KIND differs between the game builds one DLL has to run on.
    ///
    /// Game 3.3 turned ItemValue.type / Quality / Seed / UseTimes, ItemStack.itemValue / count and
    /// ItemInventoryData.holdingEntity / item / world from public fields into properties. The C#
    /// reads the same either way; the IL does not. A DLL compiled against 3.2 emits
    /// `ldfld ItemStack::count` and throws MissingFieldException on 3.3; one compiled against 3.3
    /// emits `callvirt get_count()` and throws MissingMethodException on 3.2. No single compiled
    /// reference satisfies both, so none is emitted: each accessor below is resolved once, by
    /// name, to whichever kind the running game has, and compiled to a delegate.
    ///
    /// WHY EVERY READ HAS TO GO THROUGH HERE, EVEN ONE INSIDE A try/catch: Mono resolves a method's
    /// member references when it JITs the METHOD. A method holding one unresolvable reference
    /// cannot be compiled at all, so it throws at its CALL SITE and its own catch never runs. In a
    /// Harmony prefix or postfix that means the exception leaves through the patched game method.
    ///
    /// An accessor is never null. One the running game cannot supply throws MissingMemberException
    /// naming the member when it is called, and is listed in <see cref="Missing"/>, which is empty
    /// on every build from V3.0.0 to V3.3.0.
    /// </summary>
    internal static class ItemCompat
    {
        private static readonly List<string> missing = new List<string>();

        // ItemValue
        internal static readonly Func<ItemValue, int> Type = Get<ItemValue, int>("type");
        internal static readonly Action<ItemValue, int> SetType = Set<ItemValue, int>("type");
        internal static readonly Func<ItemValue, ushort> Quality = Get<ItemValue, ushort>("Quality");
        internal static readonly Func<ItemValue, ushort> Seed = Get<ItemValue, ushort>("Seed");
        internal static readonly Func<ItemValue, float> UseTimes = Get<ItemValue, float>("UseTimes");
        internal static readonly Action<ItemValue, float> SetUseTimes = Set<ItemValue, float>("UseTimes");

        // ItemStack
        internal static readonly Func<ItemStack, ItemValue> Value = Get<ItemStack, ItemValue>("itemValue");
        internal static readonly Action<ItemStack, ItemValue> SetValue = Set<ItemStack, ItemValue>("itemValue");
        internal static readonly Func<ItemStack, int> Count = Get<ItemStack, int>("count");
        internal static readonly Action<ItemStack, int> SetCount = Set<ItemStack, int>("count");

        // ItemInventoryData
        internal static readonly Func<ItemInventoryData, EntityAlive> HoldingEntity = Get<ItemInventoryData, EntityAlive>("holdingEntity");
        internal static readonly Func<ItemInventoryData, ItemClass> Item = Get<ItemInventoryData, ItemClass>("item");
        internal static readonly Func<ItemInventoryData, World> World = Get<ItemInventoryData, World>("world");

        /// <summary>Every accessor above the running game could not supply, comma separated; "" when
        /// all resolved. Log it from InitMod: it is the line a bug report needs.</summary>
        internal static string Missing => string.Join(", ", missing);

        /// <summary>The item type of a stack, or 0 for a null or empty one.</summary>
        internal static int TypeOf(ItemStack _stack)
        {
            if (_stack == null) return 0;
            ItemValue v = Value(_stack);
            return v == null ? 0 : Type(v);
        }

        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.DeclaredOnly;

        /// <summary>The property of that name if the type (or a base) has a readable one, else the field.</summary>
        private static MemberInfo Find(Type _type, string _name, bool _write)
        {
            for (Type t = _type; t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(_name, INSTANCE);
                if (p != null && p.GetIndexParameters().Length == 0 && (_write ? p.GetSetMethod(true) : p.GetGetMethod(true)) != null)
                    return p;
                FieldInfo f = t.GetField(_name, INSTANCE);
                if (f != null && !(_write && f.IsInitOnly)) return f;
            }
            return null;
        }

        private static Func<TObj, TVal> Get<TObj, TVal>(string _name)
        {
            string label = typeof(TObj).Name + "." + _name;
            try
            {
                MemberInfo m = Find(typeof(TObj), _name, false);
                if (m != null)
                {
                    ParameterExpression o = Expression.Parameter(typeof(TObj), "o");
                    Expression read = Expression.MakeMemberAccess(o, m);
                    if (read.Type != typeof(TVal)) read = Expression.Convert(read, typeof(TVal));
                    return Expression.Lambda<Func<TObj, TVal>>(read, o).Compile();
                }
            }
            catch (Exception) { /* reported below, with the member's name */ }

            missing.Add(label);
            return _ => throw new MissingMemberException(label + " cannot be read on this game build");
        }

        private static Action<TObj, TVal> Set<TObj, TVal>(string _name)
        {
            string label = typeof(TObj).Name + "." + _name + " (write)";
            try
            {
                MemberInfo m = Find(typeof(TObj), _name, true);
                if (m != null)
                {
                    ParameterExpression o = Expression.Parameter(typeof(TObj), "o");
                    ParameterExpression v = Expression.Parameter(typeof(TVal), "v");
                    MemberExpression target = Expression.MakeMemberAccess(o, m);
                    Expression value = target.Type != typeof(TVal) ? (Expression)Expression.Convert(v, target.Type) : v;
                    return Expression.Lambda<Action<TObj, TVal>>(Expression.Assign(target, value), o, v).Compile();
                }
            }
            catch (Exception) { /* reported below, with the member's name */ }

            missing.Add(label);
            return (_, __) => throw new MissingMemberException(label + " cannot be written on this game build");
        }
    }
}
