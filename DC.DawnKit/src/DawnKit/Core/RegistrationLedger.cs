using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace DawnKit.Core.Ownership
{
    /// <summary>
    /// The ownership registry (SPEC.md §3.5). Every registration attempt is
    /// recorded — (owner, kind, id, name) plus its Register() result — backing
    /// the public Cards.All / Weapons.All / WeaponPowers.All views, the
    /// cross-mod collision validation at Register(), the boot conflict report
    /// and the player-facing status row.
    ///
    /// ID/name spaces: cards, weapons and starting cards share the cardID/
    /// card-name space; weapon powers (talents) are their own space; sets are
    /// their own space. Names are identity and compared case-insensitively (P7).
    /// </summary>
    internal static class RegistrationLedger
    {
        private static readonly List<RegistrationInfo> entries = new List<RegistrationInfo>();
        private static readonly List<string> conflictOwners = new List<string>();
        private static readonly List<string> conflictMessages = new List<string>();

        internal static void Record(RegistrationInfo info)
        {
            entries.Add(info);
        }

        internal static IReadOnlyList<RegistrationInfo> All => entries;

        internal static IReadOnlyList<RegistrationInfo> OfKind(string kind)
        {
            var result = new List<RegistrationInfo>();
            foreach (RegistrationInfo e in entries)
            {
                if (e.Kind == kind)
                {
                    result.Add(e);
                }
            }
            return result;
        }

        /// <summary>Failed registrations for one owner and kind — folded into the injection "skipped" counts.</summary>
        internal static int FailedCount(string owner, string kind)
        {
            int count = 0;
            foreach (RegistrationInfo e in entries)
            {
                if (!e.Ok && e.Owner == owner && e.Kind == kind)
                {
                    count++;
                }
            }
            return count;
        }

        private static bool IsCardSpace(string kind) => kind == "card" || kind == "weapon" || kind == "startingCard";

        /// <summary>
        /// Cross-mod validation at Register(): an already-registered (Ok) entry in
        /// the card ID/name space that claims this ID or (case-insensitively) this
        /// name, or null. Applies across mods AND within one mod (per-item isolation).
        /// </summary>
        internal static RegistrationInfo FindCardSpaceConflict(long id, string name)
        {
            foreach (RegistrationInfo e in entries)
            {
                if (e.Ok && IsCardSpace(e.Kind) &&
                    (e.Id == id || string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return e;
                }
            }
            return null;
        }

        /// <summary>Same as <see cref="FindCardSpaceConflict"/> for the talent (weapon power) space.</summary>
        internal static RegistrationInfo FindTalentSpaceConflict(long id, string name)
        {
            foreach (RegistrationInfo e in entries)
            {
                if (e.Ok && e.Kind == "weaponPower" &&
                    (e.Id == id || string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return e;
                }
            }
            return null;
        }

        /// <summary>Same idea for the event space — NAME-only (events have no
        /// numeric IDs; EVENT-SPEC §3: name-keyed ledger kind "event").</summary>
        internal static RegistrationInfo FindEventSpaceConflict(string name)
        {
            foreach (RegistrationInfo e in entries)
            {
                if (e.Ok && e.Kind == "event" &&
                    string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return e;
                }
            }
            return null;
        }

        /// <summary>
        /// Record a conflict for the boot report (register-time cross-mod refusals
        /// and injection-time shipped-pool collisions). Deduplicated by message so
        /// re-injection passes don't multiply the report.
        /// </summary>
        internal static void RecordConflict(string owner, string message)
        {
            if (conflictMessages.Contains(message))
            {
                return;
            }
            conflictOwners.Add(owner);
            conflictMessages.Add(message);
        }

        internal static int ConflictCount => conflictMessages.Count;

        internal static IReadOnlyList<string> ConflictMessages => conflictMessages;

        /// <summary>Distinct owners of item registrations (cards/weapons/powers), first-seen order.</summary>
        internal static List<string> ItemOwners()
        {
            var owners = new List<string>();
            foreach (RegistrationInfo e in entries)
            {
                if (e.Kind != "set" && !owners.Contains(e.Owner))
                {
                    owners.Add(e.Owner);
                }
            }
            return owners;
        }

        /// <summary>Registered item count (cards/weapons/powers; all attempts) for one owner.</summary>
        internal static int RegisteredCount(string owner)
        {
            int count = 0;
            foreach (RegistrationInfo e in entries)
            {
                if (e.Kind != "set" && e.Owner == owner)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// Resolves the owning mod when a builder never called .Owner(...): the first
    /// non-engine assembly on the call stack, mapped to its BepInEx plugin GUID
    /// when it contains a loaded plugin (SPEC.md §3.5 — "owner: BepInEx plugin
    /// GUID or explicit .Owner(...)"). Falls back to the assembly name.
    /// </summary>
    internal static class OwnerResolver
    {
        internal static string ResolveCallingOwner()
        {
            try
            {
                Assembly engine = typeof(OwnerResolver).Assembly;
                var trace = new StackTrace(false);
                for (int i = 0; i < trace.FrameCount; i++)
                {
                    MethodBase method = trace.GetFrame(i).GetMethod();
                    Assembly asm = method?.DeclaringType?.Assembly;
                    if (asm == null || asm == engine)
                    {
                        continue;
                    }
                    return GuidForAssembly(asm) ?? asm.GetName().Name;
                }
            }
            catch
            {
                // Owner attribution must never break a registration.
            }
            return "(unknown)";
        }

        private static string GuidForAssembly(Assembly asm)
        {
            try
            {
                foreach (var pair in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    if (pair.Value?.Instance != null && pair.Value.Instance.GetType().Assembly == asm)
                    {
                        return pair.Key;
                    }
                }
            }
            catch
            {
                // Chainloader unavailable (tests, early boot) — fall through.
            }
            return null;
        }
    }
}
