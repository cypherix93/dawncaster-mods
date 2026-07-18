using System;
using System.Collections.Generic;
using System.Linq;
using DawnKit.Core.Lifecycle;

namespace DawnKit.Integration.Classes
{
    /// <summary>
    /// Profession.weapons / Profession.talents attachment (WEAPON-SPEC §5.3, P18).
    /// The character-creation UI reads those lists live — no UI patching.
    /// Attachment is idempotent and stale-safe: keyed on cardID/talentID + name,
    /// never object identity, replacing any previous (wiped) instance so
    /// re-injection after ForceReloadAssets converges on the live objects.
    /// </summary>
    internal static class ClassIntegration
    {
        /// <summary>
        /// Appends this owner's injected weapons/talents to the live Profession
        /// assets. Returns the distinct class names that carry at least one attachment.
        /// </summary>
        internal static List<string> AttachOwner(string owner)
        {
            var attached = new List<string>();
            bool anyContent = Registry.Cards.Any(t => t.Spec.IsWeapon && t.Spec.Owner == owner && t.Card != null) ||
                              Registry.Talents.Any(t => t.Spec.Owner == owner && t.Talent != null);
            if (!anyContent)
            {
                return attached;
            }
            if (AssetManager.allClasses == null || AssetManager.allClasses.Count == 0)
            {
                DawnKitPlugin.Log.LogWarning($"[DawnKit] {owner}: AssetManager.allClasses is empty — class attachment deferred to the next load pass.");
                return attached;
            }

            foreach (CardRegistration t in Registry.Cards)
            {
                if (!t.Spec.IsWeapon || t.Spec.Owner != owner || t.Card == null)
                {
                    continue;
                }
                foreach (Profession prof in ResolveClasses(owner, t.Card.name, t.Spec.Classes))
                {
                    if (prof.weapons == null)
                    {
                        prof.weapons = new List<Card>();
                    }
                    if (!prof.weapons.Contains(t.Card))
                    {
                        prof.weapons.RemoveAll(w => w != null &&
                            (w.cardID == t.Card.cardID ||
                             string.Equals(w.name, t.Card.name, StringComparison.OrdinalIgnoreCase)));
                        prof.weapons.Add(t.Card);
                    }
                    if (!attached.Contains(prof.name))
                    {
                        attached.Add(prof.name);
                    }
                }
            }

            foreach (TalentRegistration t in Registry.Talents)
            {
                if (t.Spec.Owner != owner || t.Talent == null)
                {
                    continue;
                }
                foreach (Profession prof in ResolveClasses(owner, t.Talent.name, t.Spec.Classes))
                {
                    if (prof.talents == null)
                    {
                        prof.talents = new List<Talent>();
                    }
                    if (!prof.talents.Contains(t.Talent))
                    {
                        prof.talents.RemoveAll(x => x != null &&
                            (x.ID == t.Talent.ID ||
                             string.Equals(x.name, t.Talent.name, StringComparison.OrdinalIgnoreCase)));
                        prof.talents.Add(t.Talent);
                    }
                    if (!attached.Contains(prof.name))
                    {
                        attached.Add(prof.name);
                    }
                }
            }

            attached.Sort(StringComparer.OrdinalIgnoreCase);
            return attached;
        }

        /// <summary>Declared class names → live Professions. "all" = every class;
        /// unknown names log an error and skip only that attachment.</summary>
        private static List<Profession> ResolveClasses(string owner, string itemName, List<string> classes)
        {
            var result = new List<Profession>();
            if (classes == null || classes.Count == 0)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {owner}/{itemName}: no target classes declared — not offered to any Profession.");
                return result;
            }
            foreach (string cls in classes)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                if (string.Equals(cls, "all", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (Profession p in AssetManager.allClasses)
                    {
                        if (p != null && !result.Contains(p))
                        {
                            result.Add(p);
                        }
                    }
                    continue;
                }
                Profession prof = AssetManager.allClasses.FirstOrDefault(p => p != null &&
                    string.Equals(p.name, cls, StringComparison.OrdinalIgnoreCase));
                if (prof == null)
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] {owner}/{itemName}: unknown class '{cls}' — that attachment skipped.");
                    continue;
                }
                if (!result.Contains(prof))
                {
                    result.Add(prof);
                }
            }
            return result;
        }

        /// <summary>Debug dump: per-class weapon/talent list sizes after injection,
        /// so live verification can confirm Profession lists actually grew.</summary>
        internal static void LogClassCounts()
        {
            if (AssetManager.allClasses == null || AssetManager.allClasses.Count == 0 ||
                (!Registry.Cards.Any(t => t.Spec.IsWeapon) && Registry.Talents.Count == 0))
            {
                return;
            }
            DawnKitPlugin.Log.LogInfo("[DawnKit] Class weapon/talent counts: " + string.Join(", ",
                AssetManager.allClasses.Where(p => p != null).Select(p =>
                    $"{p.name}={(p.weapons?.Count ?? 0)}w/{(p.talents?.Count ?? 0)}t")));
        }
    }
}
