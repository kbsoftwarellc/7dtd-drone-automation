#!/usr/bin/env python3
"""Prove a compiled mod DLL resolves against a given game build, without running the game.

A 7DTD mod is compiled against ONE Assembly-CSharp.dll and then run against whatever build the
player has. When a game update renames, deletes, or changes the *kind* of a member (3.0.1 turned
RecipeQueueItem.Recipe from a property into a plain field), the DLL still loads and then throws
MissingMethodException the first time Mono JITs the calling method — often per tick, forever, and
the log fills with it. That failure mode is invisible until someone plays on the new build.

This walks the mod's MemberRef table (every external member it binds to) and checks each one
against a target Assembly-CSharp.dll, following base classes. Method refs are matched on name AND
parameter count, which catches a signature drift like 3.1 adding a second parameter to
ItemStack.CanMoveTo. Field refs are matched as fields, so a field that became a property (or the
reverse) is reported rather than silently passing.

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


class Target:
    """The members a game assembly actually declares, indexed by type full name."""

    def __init__(self, path):
        self.path = path
        t = dnfile.dnPE(path).net.mdtables
        self.methods = {}   # type full name -> {(name, param count)}
        self.fields = {}    # type full name -> {name}
        self.extends = {}   # type full name -> base type full name, or None

        for td in t.TypeDef.rows:
            name = full_name(td.TypeNamespace, td.TypeName)
            self.methods[name] = {
                (s(m.row.Name), sig_param_count(blob(m.row.Signature)))
                for m in td.MethodList if m.row is not None
            }
            self.fields[name] = {s(f.row.Name) for f in td.FieldList if f.row is not None}

            # A TypeSpec base is a generic instantiation and carries no plain name; treat it as an
            # end of the chain rather than trying to resolve it.
            base = getattr(td.Extends, "row", None)
            self.extends[name] = full_name(getattr(base, "TypeNamespace", ""),
                                           getattr(base, "TypeName", "")) or None if base else None

    def has_type(self, name):
        return name in self.methods

    def _walk(self, type_name):
        """This type then each base, stopping at a base declared outside this assembly."""
        seen, cur = set(), type_name
        while cur and cur in self.methods and cur not in seen:
            seen.add(cur)
            yield cur
            cur = self.extends.get(cur)

    def has_method(self, type_name, member, params):
        for cur in self._walk(type_name):
            for n, p in self.methods[cur]:
                # A None count on either side means the signature could not be decoded; fall back
                # to a name match rather than reporting a break that isn't one.
                if n == member and (params is None or p is None or p == params):
                    return True
        return False

    def has_field(self, type_name, member):
        for cur in self._walk(type_name):
            if member in self.fields[cur]:
                return True
        return False


def mod_references(path, scopes):
    """(type full name, member, param count, is_field) for every external member the mod binds."""
    t = dnfile.dnPE(path).net.mdtables
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
                 sig_param_count(sig), is_field_sig(sig)))
    return sorted(out)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2

    mod_path, targets = sys.argv[1], sys.argv[2:]
    scopes = {"Assembly-CSharp", "Assembly-CSharp-firstpass"}
    refs = mod_references(mod_path, scopes)
    print(f"{mod_path}: {len(refs)} external member reference(s) into {', '.join(sorted(scopes))}\n")
    if not refs:
        print("No references found — that is almost certainly a bug in this script, not a clean mod.")
        return 2

    failed = False
    for target_path in targets:
        target = Target(target_path)
        missing = []
        for type_name, member, params, is_field in refs:
            if not target.has_type(type_name):
                missing.append(f"type {type_name}")
                continue
            ok = target.has_field(type_name, member) if is_field \
                else target.has_method(type_name, member, params)
            if not ok:
                missing.append(f"{'field' if is_field else f'method/{params}'} {type_name}::{member}")
        print(f"[{'OK ' if not missing else 'FAIL'}] {target_path}")
        for m in sorted(set(missing)):
            print(f"         missing: {m}")
        failed |= bool(missing)

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
