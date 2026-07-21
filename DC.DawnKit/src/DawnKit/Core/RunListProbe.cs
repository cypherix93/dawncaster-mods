using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DawnKit.Integration.Sets;

namespace DawnKit.Core.Status
{
    /// <summary>
    /// Verification probe (SPEC.md §6 #16): per-set run-pool counts every time the
    /// run pool is rebuilt — the log evidence that toggling a mod set actually
    /// includes/excludes its cards. M1b grows this namespace into the player-facing
    /// status surface ("N mods, M items, K errors" on the run-settings set screen)
    /// and the DiagnosticsDump knob.
    /// </summary>
    internal static class RunListProbe
    {
        /// <summary>Private static AssetManager._runcards — resolved at boot by PatchManager.</summary>
        internal static FieldInfo RuncardsField;

        internal static void CreateRunLists_Postfix()
        {
            try
            {
                if (ModSets.PackSets.Count == 0 || RuncardsField == null)
                {
                    return;
                }
                var runcards = (List<Card>)RuncardsField.GetValue(null);
                if (runcards == null || PlayerHandler.thePlayerData == null)
                {
                    return;
                }
                string perSet = string.Join(", ", ModSets.PackSets.Select(s =>
                    $"{s.DisplayName}={runcards.Count(c => c != null && c.cardexpansion == s.Expansion)}"));
                string excluded = string.Join(",", PlayerHandler.thePlayerData.excludedsets.Select(e => (int)e));
                DawnKitPlugin.Log.LogInfo($"[DawnKit] CreateRunLists: runcards={runcards.Count} [{perSet}] excludedsets=[{excluded}]");

                if (DawnKitPlugin.SandboxModOnlyRewards)
                {
                    // Sandbox test knob (SPEC.md §5, [Sandbox] ModOnlyRewards): shrink the run
                    // pool to mod-set cards (synthetic expansions >= 1000, Sets.SetHandle) so
                    // test runs only offer mod content. Fail-safe: never leave the pool empty.
                    int modCount = runcards.Count(c => c != null && (int)c.cardexpansion >= 1000);
                    if (modCount == 0)
                    {
                        DawnKitPlugin.Log.LogWarning(
                            "[DawnKit] [Sandbox] ModOnlyRewards=true but the run pool holds no mod-set cards — filter skipped, pool left vanilla.");
                    }
                    else
                    {
                        int removed = runcards.RemoveAll(c => c == null || (int)c.cardexpansion < 1000);
                        DawnKitPlugin.Log.LogWarning(
                            $"[DawnKit] [Sandbox] ModOnlyRewards ACTIVE: removed {removed} native cards; run pool = {runcards.Count} mod cards. TESTING ONLY.");
                    }
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] CreateRunLists probe failed: {ex}");
            }
        }
    }
}
