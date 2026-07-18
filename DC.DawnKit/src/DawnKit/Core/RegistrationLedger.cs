using System.Collections.Generic;

namespace DawnKit.Core.Ownership
{
    /// <summary>
    /// M1a stub of the ownership registry (SPEC.md §3.5). Records every
    /// registration attempt — (owner, kind, id, name) plus its Register() result —
    /// and backs the public Cards.All / Weapons.All / WeaponPowers.All / Sets.All
    /// views.
    ///
    /// M1b fills this seam: cross-mod ID/name collision validation at Register(),
    /// the boot-completion conflict report with both claimants named, and the
    /// player-facing status surface ("N mods, M items, K errors").
    /// </summary>
    internal static class RegistrationLedger
    {
        private static readonly List<RegistrationInfo> entries = new List<RegistrationInfo>();

        internal static void Record(RegistrationInfo info)
        {
            entries.Add(info);
        }

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
    }
}
