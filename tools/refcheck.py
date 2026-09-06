# SPDX-License-Identifier: MIT
# Canonical copy: 7dtd-mods/shared/refcheck.py -- edit THERE, then re-sync the per-mod copies with
# tools/sync_shared.py. Each mod ships its own copy because each mod repo must verify itself with
# no checkout of any other repo present.
#
# REFCHECK_VERSION: 2
#!/usr/bin/env python3
"""Prove a compiled mod DLL resolves against a given game build, without running the game.

A 7DTD mod is compiled against ONE Assembly-CSharp.dll and then run against whatever build the
player has. When a game update renames, deletes, or changes the *kind* of a member (3.0.1 turned
RecipeQueueItem.Recipe from a property into a plain field), the DLL still loads and then throws
MissingMethodException the first time Mono JITs the calling method — often per tick, forever, and
the log fills with it. That failure mode is invisible until someone plays on the new build.

This walks the mod's MemberRef table (every external member it binds to) and checks each one
against a target Assembly-CSharp.dll, following base classes. Method refs are matched on name and
full PARAMETER TYPES, which catches both a drift in arity (3.1 adding a second parameter to
ItemStack.CanMoveTo) and one in shape alone (3.0.0 turning SelectionCategory.AddBox's second
parameter from Vector3 into Vector3i while keeping all five). The types matter because that is what
the runtime binds on: a count-only match called AddBox resolved, and AutoMiner shipped a claim on
three game versions where its mark preview died with MethodNotFound the moment anyone used it.

Where a signature cannot be decoded on either side the parameter count still gates it. This tool may
fail to report a break, but it must never invent one. Field refs are matched as fields, so a field
that became a property (or the reverse) is reported rather than silently passing.

    python3 tools/refcheck.py mod/FuelTopOff.dll \\
        "<game>/7DaysToDie_Data/Managed/Assembly-CSharp.dll" \\
        "<server 3.1>/7DaysToDieServer_Data/Managed/Assembly-CSharp.dll"

Exit code 0 = every reference resolved in every target. Needs `pip install dnfile`.
"""
import sys
import dnfile


def s(x):
    """dnfile hands back HeapItemString / HeapItemBinary wrappers, not str / bytes."""
    if x is None:
        return ""
    v = getattr(x, "value", x)
    return v.decode() if isinstance(v, (bytes, bytearray)) else str(v)


def blob(x):
    if x is None:
        return b""
    v = getattr(x, "value", x)
    return bytes(v) if v else b""


def full_name(ns, name):
    ns, name = s(ns), s(name)
    return f"{ns}.{name}" if ns else name


def uncompress(b, i):
    """Read an ECMA-335 compressed unsigned int; returns (value, next index)."""
    x = b[i]
    if x & 0x80 == 0:
        return x, i + 1
    if x & 0xC0 == 0x80:
        return ((x & 0x3F) << 8) | b[i + 1], i + 2
    return ((x & 0x1F) << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3], i + 4


def is_field_sig(b):
    return len(b) > 0 and (b[0] & 0x0F) == 0x06


def sig_param_count(b):
    """Parameter count of a MethodDefSig / MethodRefSig, or None when it is not a method sig."""
    if not b or is_field_sig(b):
        return None
    i = 1
    if b[0] & 0x10:            # GENERIC: a generic-arg count comes first
        _, i = uncompress(b, i)
    try:
        count, _ = uncompress(b, i)
    except IndexError:
        return None
    return count


# ---------------------------------------------------------------------------
# Signature TYPE decoding.
#
# Matching on name + parameter COUNT is not enough. 3.2 changed
# SelectionCategory.AddBox's parameter types while keeping all five of them, so
# a count-only check passed a build whose mark preview then died at runtime with
# MethodNotFound. The arity is the same on both sides; only the types moved.
#
# Tokens cannot be compared raw: the mod's blob indexes ITS TypeRef table and the
# game's blob indexes the game's TypeDef/TypeRef tables, so the same type has
# different tokens in each. Both sides are therefore resolved to type NAMES here,
# and the names are what get compared.
#
# Anything this cannot decode yields None, and a None on either side falls back to
# the count check. That direction is deliberate: this tool may fail to report a
# break, but it must never invent one.
# ---------------------------------------------------------------------------

ELEMENT_TYPES = {
    0x01: "void", 0x02: "bool", 0x03: "char", 0x04: "sbyte", 0x05: "byte",
    0x06: "short", 0x07: "ushort", 0x08: "int", 0x09: "uint", 0x0A: "long",
    0x0B: "ulong", 0x0C: "float", 0x0D: "double", 0x0E: "string",
    0x16: "typedref", 0x18: "IntPtr", 0x19: "UIntPtr", 0x1C: "object",
}


class TypeNames:
    """Resolves a TypeDefOrRef coded index to a type full name, for ONE assembly."""

    def __init__(self, tables):
        self.defs, self.refs = [], []
        try:
            self.defs = [full_name(r.TypeNamespace, r.TypeName) for r in tables.TypeDef.rows]
        except AttributeError:
            pass
        try:
            self.refs = [full_name(r.TypeNamespace, r.TypeName) for r in tables.TypeRef.rows]
        except AttributeError:
            pass

    def coded(self, index):
        tag, row = index & 0x03, (index >> 2) - 1      # rows are 1-based
        if row < 0:
            return None
        if tag == 0:
            return self.defs[row] if row < len(self.defs) else None
        if tag == 1:
            return self.refs[row] if row < len(self.refs) else None
        return None                                     # TypeSpec: a generic instantiation


def read_type(b, i, names):
    """One Type from a signature blob -> (name or None, next index). Never raises."""
    try:
        e = b[i]
        i += 1
        if e in ELEMENT_TYPES:
            return ELEMENT_TYPES[e], i
        if e in (0x11, 0x12):                          # VALUETYPE | CLASS
            tok, i = uncompress(b, i)
            return names.coded(tok), i
        if e in (0x0F, 0x10, 0x1D, 0x45):              # PTR | BYREF | SZARRAY | PINNED
            inner, i = read_type(b, i, names)
            suffix = {0x0F: "*", 0x10: "&", 0x1D: "[]", 0x45: ""}[e]
            return (inner + suffix if inner else None), i
        if e in (0x1F, 0x20):                          # CMOD_REQD | CMOD_OPT: skip and continue
            _, i = uncompress(b, i)
            return read_type(b, i, names)
        if e == 0x15:                                  # GENERICINST
            base, i = read_type(b, i, names)
            argc, i = uncompress(b, i)
            args = []
            for _ in range(argc):
                a, i = read_type(b, i, names)
                args.append(a or "?")
            return (f"{base}<{','.join(args)}>" if base else None), i
        if e in (0x13, 0x1E):                          # VAR | MVAR
            n, i = uncompress(b, i)
            return (f"!{n}" if e == 0x13 else f"!!{n}"), i
        if e == 0x14:                                  # ARRAY: type rank numsizes sizes numlobounds
            inner, i = read_type(b, i, names)
            _rank, i = uncompress(b, i)
            n, i = uncompress(b, i)
            for _ in range(n):
                _, i = uncompress(b, i)
            n, i = uncompress(b, i)
            for _ in range(n):
                _, i = uncompress(b, i)
            return (inner + "[,]" if inner else None), i
        return None, i                                 # FNPTR, SENTINEL, anything unhandled
    except (IndexError, TypeError):
        return None, len(b)


def sig_param_types(b, names):
    """Parameter type names of a method signature, or None if it cannot be decoded."""
    if not b or is_field_sig(b):
        return None
    try:
        i = 1
        if b[0] & 0x10:
            _, i = uncompress(b, i)
        count, i = uncompress(b, i)
        _ret, i = read_type(b, i, names)               # return type is not part of overload identity
        out = []
        for _ in range(count):
            t, i = read_type(b, i, names)
            if t is None:
                return None                            # one unknown makes the whole tuple untrustworthy
            out.append(t)
        return tuple(out)
    except (IndexError, TypeError):
        return None


class Target:
    """The members a game assembly actually declares, indexed by type full name."""

    def __init__(self, path):
        self.path = path
        t = dnfile.dnPE(path).net.mdtables
        names = TypeNames(t)
        self.methods = {}   # type full name -> {(name, param count, param type names or None)}
        self.fields = {}    # type full name -> {name}
        self.extends = {}   # type full name -> base type full name, or None

        # A NESTED type carries an empty namespace, so full_name() gives it the same key as a
        # top-level type of the same simple name. Assembly-CSharp has exactly that collision: the
        # real `Stat` (the entity stat, 30 methods) and a one-method `Stat` nested inside another
        # class. Whichever came second used to overwrite the first, and every genuine member of the
        # survivor's namesake was then reported missing — a build that runs perfectly failing its
        # own version check, with a message pointing at the wrong cause entirely.
        #
        # The reading side collapses a nested TypeRef to its simple name too, so keys cannot simply
        # be qualified here without changing both halves. Merging is the honest fix for what this
        # tool promises: it is a "does this reference resolve" check, and a union can only ever fail
        # to report a break, never invent one. The old behaviour did invent them.
        nested = set()
        try:
            for nc in t.NestedClass.rows:
                row = getattr(nc.NestedClass, "row", None)
                if row is not None:
                    nested.add(id(row))
        except AttributeError:
            pass    # no NestedClass table: nothing is nested, nothing to disambiguate

        for td in t.TypeDef.rows:
            name = full_name(td.TypeNamespace, td.TypeName)
            methods = {
                (s(m.row.Name),
                 sig_param_count(blob(m.row.Signature)),
                 sig_param_types(blob(m.row.Signature), names))
                for m in td.MethodList if m.row is not None
            }
            fields = {s(f.row.Name) for f in td.FieldList if f.row is not None}

            if name in self.methods:
                self.methods[name] |= methods
                self.fields[name] |= fields
            else:
                self.methods[name] = methods
                self.fields[name] = fields

            # A TypeSpec base is a generic instantiation and carries no plain name; treat it as an
            # end of the chain rather than trying to resolve it.
            base = getattr(td.Extends, "row", None)
            extends = full_name(getattr(base, "TypeNamespace", ""),
                                getattr(base, "TypeName", "")) or None if base else None

            # On a collision the TOP-LEVEL type owns the base chain: walking a nested namesake's
            # ancestors would wander into an unrelated hierarchy.
            if name not in self.extends or id(td) not in nested:
                self.extends[name] = extends

    def has_type(self, name):
        return name in self.methods

    def _walk(self, type_name):
        """This type then each base, stopping at a base declared outside this assembly."""
        seen, cur = set(), type_name
        while cur and cur in self.methods and cur not in seen:
            seen.add(cur)
            yield cur
            cur = self.extends.get(cur)

    def has_method(self, type_name, member, params, types=None):
        """Does some overload of `member` match?

        Types decide it when BOTH sides decoded, because that is what the runtime binds on: 3.2 kept
        SelectionCategory.AddBox at five parameters and changed their types, and a count-only match
        called that resolved. When either side is undecodable the count still gates it -- weaker, but
        this tool must never invent a break it cannot prove.
        """
        count_only = []
        for cur in self._walk(type_name):
            for n, p, ts in self.methods[cur]:
                if n != member:
                    continue
                if params is not None and p is not None and p != params:
                    continue
                if types is not None and ts is not None:
                    if ts == types:
                        return True
                    count_only.append(ts)   # same name and arity, different types
                    continue
                return True                 # cannot compare types: the count match stands
        # Every same-arity overload disagreed on types. That is the AddBox case exactly.
        self.last_near_miss = count_only or None
        return False

    def has_field(self, type_name, member):
        for cur in self._walk(type_name):
            if member in self.fields[cur]:
                return True
        return False


def mod_references(path, scopes):
    """(type full name, member, param count, is_field, param type names) per external member bound."""
    t = dnfile.dnPE(path).net.mdtables
    names = TypeNames(t)
    out = set()
    for mr in (t.MemberRef.rows if t.MemberRef else []):
        parent = getattr(mr.Class, "row", None)
        # A TypeSpec parent is a generic instantiation and a ModuleRef is a P/Invoke; neither is a
        # plain external type reference, so neither is ours to check.
        if parent is None or type(parent).__name__ != "TypeRefRow":
            continue
        scope = getattr(parent.ResolutionScope, "row", None)
        if scope is None or s(getattr(scope, "Name", "")) not in scopes:
            continue
        sig = blob(mr.Signature)
        out.add((full_name(parent.TypeNamespace, parent.TypeName), s(mr.Name),
                 sig_param_count(sig), is_field_sig(sig), sig_param_types(sig, names)))
    return sorted(out)


def main():
    argv = sys.argv[1:]

    # The game's own assemblies are the default because they are the ones that move. Everything else
    # -- Unity modules, the post-processing stack -- is checked by naming its scope explicitly, e.g.
    #   --scopes UnityEngine.CoreModule  mod/SteadyView.dll <game>/.../UnityEngine.CoreModule.dll
    # which is worth doing for anything added from a newer Unity than the mod was written against.
    scopes = {"Assembly-CSharp", "Assembly-CSharp-firstpass"}
    if argv and argv[0] == "--scopes":
        if len(argv) < 2:
            print(__doc__)
            return 2
        scopes = {p for p in argv[1].split(",") if p}
        argv = argv[2:]

    if len(argv) < 2:
        print(__doc__)
        return 2

    mod_path, targets = argv[0], argv[1:]
    refs = mod_references(mod_path, scopes)
    print(f"{mod_path}: {len(refs)} external member reference(s) into {', '.join(sorted(scopes))}\n")
    if not refs:
        print("No references found — that is almost certainly a bug in this script, not a clean mod.")
        return 2

    failed = False
    for target_path in targets:
        target = Target(target_path)
        missing = []
        for type_name, member, params, is_field, types in refs:
            if not target.has_type(type_name):
                missing.append(f"type {type_name}")
                continue
            target.last_near_miss = None
            ok = target.has_field(type_name, member) if is_field \
                else target.has_method(type_name, member, params, types)
            if ok:
                continue
            if is_field:
                missing.append(f"field {type_name}::{member}")
            elif target.last_near_miss:
                # Same name, same arity, different types -- the failure a count-only check waved
                # through. Say so, because "missing" reads as "deleted" and this member is right
                # there with a new shape.
                want = ", ".join(types or ())
                got = " | ".join(", ".join(t) for t in target.last_near_miss)
                missing.append(f"method {type_name}::{member} SIGNATURE CHANGED — "
                               f"mod binds ({want}); this build has ({got})")
            else:
                missing.append(f"method/{params} {type_name}::{member}")
        print(f"[{'OK ' if not missing else 'FAIL'}] {target_path}")
        for m in sorted(set(missing)):
            print(f"         missing: {m}")
        failed |= bool(missing)

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
