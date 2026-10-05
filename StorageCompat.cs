using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace TehAon.Compat
{
    /// <summary>
    /// SHARED-CANONICAL: StorageCompat. Edit shared/StorageCompat.cs in the toolchain, then run
    /// tools/sync_shared.py --write; a fix made in one mod's copy is lost at the next sync.
    ///
    /// STORAGECOMPAT_VERSION: 1
    ///
    /// Where a container keeps its slots, on every game build one DLL has to run on.
    ///
    /// Up to game 3.2 each container owned its own array: TEFeatureStorage.items, Bag.GetSlots(),
    /// Inventory.GetSlots(), with the loot state (bTouched, bPlayerStorage) and SlotLocks beside it.
    /// Game 3.3 moved all of that into one ItemStackGrid per container, reached as .ItemGrid:
    /// ItemGrid.items, ItemGrid.Touched, ItemGrid.PlayerOwned, ItemGrid.SlotLocks. The old members are
    /// gone (and so is the ITileEntityLootable interface), so a compiled reference to either shape
    /// fails on the other. Each accessor here is resolved once, by name, to whichever the game has.
    ///
    /// READ THIS BEFORE WRITING TO A SLOT. On 3.3 the stacks in a grid are PERMANENT objects bound to
    /// their slot: the game never replaces one, it changes it in place (ItemStackGrid.SetItem ->
    /// items[i].Set(...)), and that is what raises the change event that saves and syncs the box.
    ///   - Never assign into the array (`slots[i] = stack`). On 3.3 that swaps in a stack the grid
    ///     does not know, and nothing after it is saved or sent. Use the game's own writer:
    ///     TEFeatureStorage.UpdateSlot(i, stack) (same on every build), or <see cref="SetBagSlot"/>.
    ///   - A stack you read BEFORE a write is the same object the write then changes. Take what you
    ///     need from it (count, a Clone of its itemValue) first; on 3.2 the old object kept its
    ///     values, on 3.3 it is now the new contents.
    ///   - Never put one ItemValue into two stacks: 3.3 binds an ItemValue to the stack holding it.
    ///
    /// An accessor is never null. One the running game cannot supply throws MissingMemberException
    /// naming the member when it is called, and is listed in <see cref="Missing"/>, which is empty
    /// on every build from V3.0.0 to V3.3.0.
    /// </summary>
    internal static class StorageCompat
    {
        private static readonly List<string> missing = new List<string>();

        /// <summary>True from game 3.3: containers keep their slots in an ItemStackGrid whose stacks are
        /// permanent objects the game changes in place. False up to 3.2, where a container is a plain
        /// array whose elements get replaced. For the few places a mod has to do one or the other.</summary>
        internal static readonly bool Grids = typeof(Bag).GetProperty("ItemGrid", INSTANCE) != null;

        // A storage box (TEFeatureStorage): the live slot array, never a copy.
        internal static readonly Func<TEFeatureStorage, ItemStack[]> Items =
            Read<TEFeatureStorage, ItemStack[]>("TEFeatureStorage.items", "ItemGrid.items", "items");

        /// <summary>The player's slot locks on a box, or null when it has none (always possible up to 3.2).</summary>
        internal static readonly Func<TEFeatureStorage, PackedBoolArray> SlotLocks =
            Read<TEFeatureStorage, PackedBoolArray>("TEFeatureStorage.SlotLocks", "ItemGrid.SlotLocks", "SlotLocks");

        /// <summary>Replaces a box's slot locks. On 3.3 the array must be as long as the box has slots.</summary>
        internal static readonly Action<TEFeatureStorage, PackedBoolArray> SetSlotLocks =
            Write<TEFeatureStorage, PackedBoolArray>("TEFeatureStorage.SlotLocks (write)", "ItemGrid.SetSlotLocks()", "SlotLocks");

        /// <summary>True for a box a player placed or took over, false for world loot.</summary>
        internal static readonly Func<TEFeatureStorage, bool> PlayerStorage =
            Read<TEFeatureStorage, bool>("TEFeatureStorage.bPlayerStorage", "ItemGrid.PlayerOwned", "bPlayerStorage");

        /// <summary>True once anyone has opened the container.</summary>
        internal static readonly Func<TEFeatureStorage, bool> Touched =
            Read<TEFeatureStorage, bool>("TEFeatureStorage.bTouched", "ItemGrid.Touched", "bTouched");

        /// <summary>Marks a container opened, as the game does when a player first looks inside:
        /// `bTouched = true` up to 3.2, ItemGrid.Touch() (which stamps the world time) from 3.3.</summary>
        internal static readonly Action<TEFeatureStorage> Touch =
            Mark<TEFeatureStorage>("TEFeatureStorage.bTouched (write)", "ItemGrid.Touch()", "bTouched");

        /// <summary>LootManager.LootContainerOpened(container, entityId, tags): rolls a world container's
        /// loot. Its first parameter was the ITileEntityLootable interface up to 3.2 and is
        /// TEFeatureStorage from 3.3; TEFeatureStorage was the interface's only implementer.</summary>
        internal static readonly Action<LootManager, TEFeatureStorage, int, FastTags<TagGroup.Global>> LootContainerOpened =
            Call<Action<LootManager, TEFeatureStorage, int, FastTags<TagGroup.Global>>>("LootManager.LootContainerOpened()",
                typeof(LootManager), "LootContainerOpened", typeof(TEFeatureStorage), typeof(int), typeof(FastTags<TagGroup.Global>));

        // A backpack (Bag) and the toolbelt (Inventory): the live slot arrays.
        internal static readonly Func<Bag, ItemStack[]> BagSlots =
            Read<Bag, ItemStack[]>("Bag slots", "ItemGrid.items", "GetSlots()");

        internal static readonly Func<Inventory, ItemStack[]> ToolbeltSlots =
            Read<Inventory, ItemStack[]>("Inventory slots", "ItemGrid.items", "GetSlots()");

        internal static readonly Func<Bag, PackedBoolArray> BagLockedSlots =
            Read<Bag, PackedBoolArray>("Bag.LockedSlots", "LockedSlots");

        internal static readonly Func<Bag, bool> BagTouched =
            Read<Bag, bool>("Bag.Touched", "Touched");

        /// <summary>Marks a bag opened: the `Touched` field up to 3.2, ItemGrid.Touch() from 3.3 (where
        /// Bag.Touched is read-only).</summary>
        internal static readonly Action<Bag> TouchBag =
            Mark<Bag>("Bag.Touched (write)", "ItemGrid.Touch()", "Touched");

        /// <summary>Bag.SetSlot(index, stack): the game's own writer. It took a third `callChangedEvent`
        /// up to 3.2 (passed as its default, true) and takes two arguments from 3.3.</summary>
        internal static readonly Action<Bag, int, ItemStack> SetBagSlot =
            Call<Action<Bag, int, ItemStack>>("Bag.SetSlot()", typeof(Bag), "SetSlot", typeof(int), typeof(ItemStack));

        /// <summary>
        /// Replaces a bag's contents with these stacks, slot for slot. Bag.SetSlots(stacks) up to 3.2; from
        /// 3.3 Bag.SetSlots(stacks, _resize: false). The 3.3 default for that second argument is TRUE, and
        /// true also reshapes the bag into ONE ROW of that many slots - which no caller of the old method
        /// ever asked for, so it is never passed here.
        /// On 3.3 you rarely need this at all: a stack edited in place has already raised the bag's change
        /// event, and handing a bag its own array back only rewrites every slot with a copy of itself.
        /// </summary>
        internal static readonly Action<Bag, ItemStack[]> SetBagSlots = BagSetSlots();

        /// <summary>Every accessor above the running game could not supply, comma separated; "" when
        /// all resolved. Log it from InitMod: it is the line a bug report needs.</summary>
        internal static string Missing => string.Join(", ", missing);

        /// <summary>True when the named accessor (the label in its declaration, e.g. "TEFeatureStorage.items")
        /// did not resolve. For a mod that uses only some of these and should not fail over the rest.</summary>
        internal static bool IsMissing(string _label) => missing.Contains(_label);

        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.DeclaredOnly;

        /// <summary>One step of a path: a readable property, else a field, else (name ending "()") a method
        /// taking _argCount arguments. Walks base types.</summary>
        private static MemberInfo Step(Type _type, string _name, int _argCount)
        {
            bool call = _name.EndsWith("()");
            string name = call ? _name.Substring(0, _name.Length - 2) : _name;
            for (Type t = _type; t != null; t = t.BaseType)
            {
                if (call)
                {
                    foreach (MethodInfo m in t.GetMethods(INSTANCE))
                        if (m.Name == name && m.GetParameters().Length == _argCount) return m;
                    continue;
                }
                PropertyInfo p = t.GetProperty(name, INSTANCE);
                if (p != null && p.GetIndexParameters().Length == 0 && p.GetGetMethod(true) != null) return p;
                FieldInfo f = t.GetField(name, INSTANCE);
                if (f != null) return f;
            }
            return null;
        }

        private static Type TypeOf(MemberInfo _m)
        {
            if (_m is PropertyInfo p) return p.PropertyType;
            if (_m is FieldInfo f) return f.FieldType;
            return ((MethodInfo)_m).ReturnType;
        }

        /// <summary>o.A.B.C as an expression, or null when any step is missing on this build.</summary>
        private static Expression Walk(Expression _from, string[] _steps, int _count)
        {
            Expression at = _from;
            for (int i = 0; i < _count; i++)
            {
                MemberInfo m = Step(at.Type, _steps[i], 0);
                if (m == null) return null;
                at = m is MethodInfo call ? (Expression)Expression.Call(at, call) : Expression.MakeMemberAccess(at, m);
            }
            return at;
        }

        /// <summary>The first of these dotted paths the running game has, compiled to a getter.</summary>
        private static Func<TObj, TVal> Read<TObj, TVal>(string _label, params string[] _paths)
        {
            foreach (string path in _paths)
            {
                try
                {
                    string[] steps = path.Split('.');
                    ParameterExpression o = Expression.Parameter(typeof(TObj), "o");
                    Expression read = Walk(o, steps, steps.Length);
                    if (read == null || !typeof(TVal).IsAssignableFrom(read.Type)) continue;
                    if (read.Type != typeof(TVal)) read = Expression.Convert(read, typeof(TVal));
                    return Expression.Lambda<Func<TObj, TVal>>(read, o).Compile();
                }
                catch (Exception) { /* try the next shape */ }
            }
            missing.Add(_label);
            return _ => throw new MissingMemberException(_label + " cannot be read on this game build");
        }

        /// <summary>The first of these paths the game has, compiled to a setter. A last step ending "()"
        /// is a one-argument method called with the value; otherwise a property or field assigned to.</summary>
        private static Action<TObj, TVal> Write<TObj, TVal>(string _label, params string[] _paths)
        {
            foreach (string path in _paths)
            {
                try
                {
                    string[] steps = path.Split('.');
                    ParameterExpression o = Expression.Parameter(typeof(TObj), "o");
                    ParameterExpression v = Expression.Parameter(typeof(TVal), "v");
                    Expression owner = Walk(o, steps, steps.Length - 1);
                    if (owner == null) continue;
                    string last = steps[steps.Length - 1];
                    MemberInfo m = Step(owner.Type, last, 1);
                    if (m == null) continue;

                    Expression body;
                    if (m is MethodInfo call)
                    {
                        Type want = call.GetParameters()[0].ParameterType;
                        if (!want.IsAssignableFrom(typeof(TVal))) continue;
                        body = Expression.Call(owner, call, want != typeof(TVal) ? (Expression)Expression.Convert(v, want) : v);
                    }
                    else
                    {
                        if (m is PropertyInfo p && p.GetSetMethod(true) == null) continue;
                        if (m is FieldInfo f && f.IsInitOnly) continue;
                        Type want = TypeOf(m);
                        if (!want.IsAssignableFrom(typeof(TVal))) continue;
                        body = Expression.Assign(Expression.MakeMemberAccess(owner, m),
                                                 want != typeof(TVal) ? (Expression)Expression.Convert(v, want) : v);
                    }
                    return Expression.Lambda<Action<TObj, TVal>>(body, o, v).Compile();
                }
                catch (Exception) { /* try the next shape */ }
            }
            missing.Add(_label);
            return (_, __) => throw new MissingMemberException(_label + " cannot be written on this game build");
        }

        private static Action<Bag, ItemStack[]> BagSetSlots()
        {
            const string label = "Bag.SetSlots()";
            try
            {
                MethodInfo one = null, two = null;
                foreach (MethodInfo m in typeof(Bag).GetMethods(INSTANCE))
                {
                    if (m.Name != "SetSlots") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 0 || ps[0].ParameterType != typeof(ItemStack[])) continue;
                    if (ps.Length == 1) one = m;
                    else if (ps.Length == 2 && ps[1].ParameterType == typeof(bool)) two = m;
                }

                ParameterExpression bag = Expression.Parameter(typeof(Bag), "bag");
                ParameterExpression stacks = Expression.Parameter(typeof(ItemStack[]), "stacks");
                if (one != null)
                    return Expression.Lambda<Action<Bag, ItemStack[]>>(Expression.Call(bag, one, stacks), bag, stacks).Compile();
                if (two != null)
                    return Expression.Lambda<Action<Bag, ItemStack[]>>(
                        Expression.Call(bag, two, stacks, Expression.Constant(false)), bag, stacks).Compile();
            }
            catch (Exception) { /* reported below, with the member's name */ }

            missing.Add(label);
            return (_, __) => throw new MissingMemberException(label + " is not available on this game build");
        }

        /// <summary>A "set this flag" action: the first path that resolves. One ending "()" is a method
        /// called with no arguments; otherwise a bool property or field set to true.</summary>
        private static Action<TObj> Mark<TObj>(string _label, params string[] _paths)
        {
            foreach (string path in _paths)
            {
                try
                {
                    string[] steps = path.Split('.');
                    ParameterExpression o = Expression.Parameter(typeof(TObj), "o");
                    Expression owner = Walk(o, steps, steps.Length - 1);
                    if (owner == null) continue;
                    string last = steps[steps.Length - 1];
                    MemberInfo m = Step(owner.Type, last, 0);
                    if (m == null) continue;

                    Expression body;
                    if (m is MethodInfo call) body = Expression.Call(owner, call);
                    else
                    {
                        if (TypeOf(m) != typeof(bool)) continue;
                        if (m is PropertyInfo p && p.GetSetMethod(true) == null) continue;
                        if (m is FieldInfo f && f.IsInitOnly) continue;
                        body = Expression.Assign(Expression.MakeMemberAccess(owner, m), Expression.Constant(true));
                    }
                    return Expression.Lambda<Action<TObj>>(body, o).Compile();
                }
                catch (Exception) { /* try the next shape */ }
            }
            missing.Add(_label);
            return _ => throw new MissingMemberException(_label + " cannot be set on this game build");
        }

        /// <summary>
        /// An instance method by name whose FIRST parameters are these types, compiled to a delegate that
        /// takes the instance and just those. Any further parameters the game added or has always had are
        /// passed their declared default. That is what "an optional parameter was appended" needs: the C#
        /// call site reads the same, the compiled call does not. A leading parameter may be declared as
        /// a base type or interface of the type given (the game retyped one from an interface to its
        /// only implementer).
        /// </summary>
        private static TDelegate Call<TDelegate>(string _label, Type _type, string _name, params Type[] _leading)
            where TDelegate : class
        {
            try
            {
                for (Type t = _type; t != null; t = t.BaseType)
                {
                    foreach (MethodInfo m in t.GetMethods(INSTANCE))
                    {
                        if (m.Name != _name) continue;
                        ParameterInfo[] ps = m.GetParameters();
                        if (ps.Length < _leading.Length) continue;

                        bool fits = true;
                        for (int i = 0; i < _leading.Length && fits; i++) fits = ps[i].ParameterType.IsAssignableFrom(_leading[i]);
                        for (int i = _leading.Length; i < ps.Length && fits; i++) fits = ps[i].HasDefaultValue;
                        if (!fits) continue;

                        ParameterExpression o = Expression.Parameter(_type, "o");
                        var given = new ParameterExpression[_leading.Length + 1];
                        var args = new Expression[ps.Length];
                        given[0] = o;
                        for (int i = 0; i < ps.Length; i++)
                        {
                            if (i < _leading.Length)
                            {
                                given[i + 1] = Expression.Parameter(_leading[i], "a" + i);
                                args[i] = ps[i].ParameterType == _leading[i]
                                    ? (Expression)given[i + 1]
                                    : Expression.Convert(given[i + 1], ps[i].ParameterType);
                            }
                            else args[i] = Expression.Constant(ps[i].DefaultValue, ps[i].ParameterType);
                        }
                        return Expression.Lambda<TDelegate>(Expression.Call(o, m, args), given).Compile();
                    }
                }
            }
            catch (Exception) { /* reported below, with the member's name */ }

            missing.Add(_label);
            return Thrower<TDelegate>(_label);
        }

        /// <summary>A delegate of the wanted shape that throws, so a missing member is reported by name
        /// where it is used instead of as a NullReferenceException.</summary>
        private static TDelegate Thrower<TDelegate>(string _label) where TDelegate : class
        {
            MethodInfo invoke = typeof(TDelegate).GetMethod("Invoke");
            ParameterInfo[] ps = invoke.GetParameters();
            var given = new ParameterExpression[ps.Length];
            for (int i = 0; i < ps.Length; i++) given[i] = Expression.Parameter(ps[i].ParameterType, "p" + i);
            Expression boom = Expression.Throw(
                Expression.Constant(new MissingMemberException(_label + " is not available on this game build")),
                invoke.ReturnType);
            return Expression.Lambda<TDelegate>(boom, given).Compile();
        }
    }
}
