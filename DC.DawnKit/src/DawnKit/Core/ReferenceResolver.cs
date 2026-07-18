using System;
using System.Collections.Generic;
using System.Linq;
using DawnKit.Core.Lifecycle;

namespace DawnKit.Core.RefResolver
{
    /// <summary>
    /// Two-phase referenceStatus / referenceCard resolution by asset name (P4).
    /// Statuses only exist after CreateStatusCollections() in the world phase;
    /// calling AssetManager.GetStatus before that would hit its lazy
    /// LoadAllAssets() fallback and re-enter the loading pipeline — so status
    /// refs are only attempted once the collections are populated. Card refs are
    /// attempted opportunistically at phase 1 (shipped + same-batch mod cards are
    /// already present). The world-phase pass is authoritative: every ref still
    /// unresolved after it is logged as an error with owner + ref name.
    /// </summary>
    internal static class ReferenceResolver
    {
        /// <summary>Unresolved refs from the most recent authoritative (final) pass — feeds the diagnostics dump.</summary>
        internal static readonly List<string> LastUnresolved = new List<string>();

        internal static void Resolve(string source, bool finalPass)
        {
            try
            {
                ResolveInner(source, finalPass);
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Reference resolution failed (hook: {source}): {ex}");
            }
        }

        private static void ResolveInner(string source, bool finalPass)
        {
            if (Registry.Cards.Count == 0 && Registry.Talents.Count == 0)
            {
                return;
            }

            // Guard: never trigger AssetManager.GetStatus's lazy LoadAllAssets()
            // fallback — only resolve statuses once the world phase populated them.
            bool statusesAvailable = AssetManager.allStatusEffects.Count > 0;
            bool cardsAvailable = AssetManager.allCards.Count > 0;
            int resolved = 0;
            var unresolved = new List<string>();

            var owners = Registry.Cards
                .Where(t => t.Card != null)
                .Select(t => new { Label = $"{t.Spec.Owner}/{t.Card.name}", Bindings = t.Bindings })
                .Concat(Registry.Talents
                    .Where(t => t.Talent != null)
                    .Select(t => new { Label = $"{t.Spec.Owner}/{t.Talent.name} (weapon power)", Bindings = t.Bindings }));

            foreach (var owner in owners)
            {
                foreach (EffectBinding b in owner.Bindings)
                {
                    // referenceStatus by asset name.
                    if (b.Effect.referenceStatus == null && !string.IsNullOrEmpty(b.Spec.ReferenceStatus))
                    {
                        StatusEffect status = statusesAvailable ? AssetManager.GetStatus(b.Spec.ReferenceStatus) : null;
                        if (status != null)
                        {
                            b.Effect.referenceStatus = status;
                            resolved++;
                        }
                        else if (finalPass)
                        {
                            unresolved.Add($"referenceStatus '{b.Spec.ReferenceStatus}' on {owner.Label}");
                        }
                    }

                    // referenceCards by asset name (index-aligned with the declared list).
                    List<string> names = b.Spec.ReferenceCards;
                    if (names == null)
                    {
                        continue;
                    }
                    for (int i = 0; i < names.Count; i++)
                    {
                        if (b.Effect.referenceCard[i] != null || string.IsNullOrEmpty(names[i]))
                        {
                            continue;
                        }
                        Card refCard = cardsAvailable ? AssetManager.GetCard(names[i]) : null;
                        if (refCard != null)
                        {
                            b.Effect.referenceCard[i] = refCard;
                            resolved++;
                        }
                        else if (finalPass)
                        {
                            unresolved.Add($"referenceCard '{names[i]}' on {owner.Label}");
                        }
                    }
                }
            }

            if (resolved > 0 || finalPass)
            {
                DawnKitPlugin.Log.LogInfo($"[DawnKit] Reference resolution: {resolved} resolved, {unresolved.Count} unresolved (hook: {source})");
            }
            foreach (string u in unresolved)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Unresolved reference: {u}");
            }
            if (finalPass)
            {
                LastUnresolved.Clear();
                LastUnresolved.AddRange(unresolved);
                // Refresh the boot report/diagnostics with the authoritative
                // resolution result (the log block only re-emits when it changed).
                Status.BootReport.Emit(source);
            }
        }
    }
}
