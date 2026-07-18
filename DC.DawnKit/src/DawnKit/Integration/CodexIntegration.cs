using System;
using System.Collections.Generic;
using System.Reflection;
using DawnKit.Core.Lifecycle;
using DawnKit.Integration.Sets;

namespace DawnKit.Integration.Codex
{
    /// <summary>
    /// Codex integration (P12): mod cards render face-up (in-memory discovery —
    /// the engine never calls SaveCodex; stale mod IDs in Codex.dtt are harmless
    /// to the base game, whose checks are pure Contains() and whose cleanup skips
    /// unknown IDs), and mod expansions are appended to the private
    /// shownExpansions filter list, which is seeded from Enum.GetValues and would
    /// otherwise drop synthetic-set cards from the list entirely.
    /// </summary>
    internal static class CodexIntegration
    {
        /// <summary>Private CodexUI.shownExpansions — resolved at boot by PatchManager.</summary>
        internal static FieldInfo ShownExpansionsField;

        /// <summary>
        /// Add every injected, discovery-enabled card ID to
        /// CodexHandler.codex.cardList (in memory only). Called from the LoadCodex
        /// postfix and after each injection pass.
        /// </summary>
        internal static void MarkModCardsDiscovered(string source)
        {
            try
            {
                if (!CodexHandler.codexLoaded || CodexHandler.codex == null || CodexHandler.codex.cardList == null)
                {
                    return;
                }
                int added = 0;
                foreach (CardRegistration t in Registry.Cards)
                {
                    if (t.Card != null && t.Spec.CodexDiscovered &&
                        !CodexHandler.codex.cardList.Contains(t.Card.cardID))
                    {
                        CodexHandler.codex.cardList.Add(t.Card.cardID);
                        added++;
                    }
                }
                if (added > 0)
                {
                    DawnKitPlugin.Log.LogInfo($"[DawnKit] Codex: marked {added} mod cards as discovered (in-memory; hook: {source})");
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Codex auto-discovery failed (hook: {source}): {ex}");
            }
        }

        internal static void LoadCodex_Postfix()
        {
            MarkModCardsDiscovered("LoadCodex");
        }

        // The Codex expansion filter list is seeded from Enum.GetValues, which
        // never yields synthetic values — without this, mod cards are filtered
        // out of the list the moment any filter runs.
        internal static void CodexUI_Start_Postfix(CodexUI __instance)
        {
            try
            {
                if (ModSets.PackSets.Count == 0)
                {
                    return;
                }
                if (ShownExpansionsField == null)
                {
                    DawnKitPlugin.Log.LogError("[DawnKit] CodexUI.shownExpansions unavailable — mod expansions not added to the Codex filter.");
                    return;
                }
                var shown = (List<AssetManager.CardExpansions>)ShownExpansionsField.GetValue(__instance);
                int added = 0;
                foreach (PackSetInfo set in ModSets.PackSets)
                {
                    if (!shown.Contains(set.Expansion))
                    {
                        shown.Add(set.Expansion);
                        added++;
                    }
                }
                if (added > 0)
                {
                    DawnKitPlugin.Log.LogInfo($"[DawnKit] Codex: {added} mod expansions added to shownExpansions filter.");
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] CodexUI filter patch failed: {ex}");
            }
        }
    }
}
