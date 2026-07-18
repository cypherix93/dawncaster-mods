using System;
using System.Linq;
using System.Reflection;
using DawncasterLocalisation;
using UnityEngine;

namespace DawnKit.Integration.Sets
{
    /// <summary>
    /// Surfaces mod sets as distinct card sets in the run-settings screen
    /// (NameSelectorDisplay) and the Sunforge settings screen, wired to the same
    /// excludedsets logic as the official rows (P8–P11). Every patch is
    /// skip-don't-crash: failures are logged and the native behavior is left
    /// intact (the fail-safe rule). Patch methods are wired by PatchManager,
    /// which logs "Target found"/"Target MISSING" per target at boot.
    /// </summary>
    internal static class SetScreenIntegration
    {
        /// <summary>Private NameSelectorDisplay.ToggleSet(SetConfig, SunforgeSettingButton) — resolved at boot by PatchManager.</summary>
        internal static MethodInfo NameSelectorToggleSet;
        /// <summary>Private SunforgeSettings.ToggleSet(SetConfig, SunforgeSettingButton) — resolved at boot by PatchManager.</summary>
        internal static MethodInfo SunforgeToggleSet;

        // ------------------------------------------------------------------
        // Run-settings "card sets" screen: one native-cloned row per mod set.
        // Native rows are built in NameSelectorDisplay.SetSettings() from
        // Resources.Load<SetConfig>("Sets/" + enumValue); synthetic values have
        // no resource (and are not returned by Enum.GetValues), so we append
        // our rows after the native ones using the same prefab + wiring.
        // ------------------------------------------------------------------

        internal static void NameSelector_SetSettings_Postfix(NameSelectorDisplay __instance)
        {
            AddSetRows(__instance);
            AddStatusRow(__instance);
        }

        private static void AddSetRows(NameSelectorDisplay __instance)
        {
            try
            {
                if (ModSets.PackSets.Count == 0)
                {
                    return;
                }
                if (NameSelectorToggleSet == null)
                {
                    DawnKitPlugin.Log.LogError("[DawnKit] NameSelectorDisplay.ToggleSet unavailable — cannot add run-settings set rows.");
                    return;
                }
                foreach (PackSetInfo set in ModSets.PackSets)
                {
                    PackSetInfo captured = set;
                    SetConfig cfg = captured.GetOrCreateConfig();
                    GameObject row = UnityEngine.Object.Instantiate(__instance.setSelectionSetting, __instance.setOverview);
                    var sfb = row.GetComponent<SunforgeSettingButton>();
                    if (sfb == null)
                    {
                        UnityEngine.Object.Destroy(row);
                        DawnKitPlugin.Log.LogError($"[DawnKit] Row prefab has no SunforgeSettingButton — cannot add set row for {captured.DisplayName}.");
                        continue;
                    }
                    sfb.SetDisplay(cfg);        // display-name fix happens in the SetDisplay postfix below
                    sfb.bonus.text = "";        // native SetSettings blanks this on every row too
                    sfb.preview.onClick.AddListener(() => __instance.ShowSet(cfg)); // eye icon = native preview panel
                    sfb.main.onClick.AddListener(() =>
                    {
                        NameSelectorToggleSet.Invoke(__instance, new object[] { cfg, sfb }); // native excludedsets add/remove
                        bool excluded = PlayerHandler.thePlayerData.excludedsets.Contains(captured.Expansion);
                        DawnKitPlugin.Log.LogInfo($"[DawnKit] Set toggle: {captured.DisplayName} ((CardExpansions){(int)captured.Expansion}) excluded={excluded}");
                    });
                    if (PlayerHandler.thePlayerData.excludedsets.Contains(captured.Expansion))
                    {
                        sfb.SetDisabled();
                    }
                    else
                    {
                        sfb.SetEnabled();
                    }
                    DawnKitPlugin.Log.LogInfo($"[DawnKit] Run-settings set row added: {captured.DisplayName} -> (CardExpansions){(int)captured.Expansion}, {captured.Cards.Count} cards");
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Failed to add run-settings set rows: {ex}");
            }
        }

        // ------------------------------------------------------------------
        // Player-facing load status (SPEC.md §2, P19): one non-interactive row
        // appended after the set rows — "DawnKit: N mods, M items loaded", plus
        // the error count when nonzero. Visually humble: same cloned row prefab,
        // buttons disabled, no toggle state. Fail-safe: any problem logs and
        // skips the row, never the screen.
        // ------------------------------------------------------------------

        private static void AddStatusRow(NameSelectorDisplay __instance)
        {
            try
            {
                if (Core.Ownership.RegistrationLedger.All.Count == 0)
                {
                    return; // no mods registered anything — nothing changes (SPEC.md §2)
                }
                string status = Core.Status.BootReport.StatusText();
                GameObject row = UnityEngine.Object.Instantiate(__instance.setSelectionSetting, __instance.setOverview);
                var sfb = row.GetComponent<SunforgeSettingButton>();
                if (sfb == null)
                {
                    UnityEngine.Object.Destroy(row);
                    DawnKitPlugin.Log.LogError("[DawnKit] Row prefab has no SunforgeSettingButton — status row skipped.");
                    return;
                }
                sfb.mainText.text = status;
                sfb.bonus.text = "";
                if (sfb.main != null)
                {
                    sfb.main.interactable = false;
                }
                if (sfb.preview != null)
                {
                    sfb.preview.gameObject.SetActive(false); // no eye icon — not a set
                }
                sfb.SetEnabled();
                DawnKitPlugin.Log.LogInfo($"[DawnKit] Status row added: {status}");
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Failed to add the status row (skipped): {ex}");
            }
        }

        // Row label: native code renders thisSet.expansion.ToString(), which for a
        // synthetic member is the raw int ("1002"). Swap in the set name.
        internal static void SettingButton_SetDisplay_Postfix(SunforgeSettingButton __instance, SetConfig set)
        {
            try
            {
                PackSetInfo info = ModSets.FindSet(set.expansion);
                if (info != null)
                {
                    __instance.mainText.text = info.DisplayName;
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] SetDisplay name patch failed: {ex}");
            }
        }

        // Preview (eye icon) panel title: same raw-int problem as the row label.
        internal static void SetPreviewPanel_SetDisplay_Postfix(SetPreviewPanel __instance, SetConfig set)
        {
            try
            {
                PackSetInfo info = set != null ? ModSets.FindSet(set.expansion) : null;
                if (info != null)
                {
                    __instance.setTitle.text = info.DisplayName;
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] SetPreviewPanel title patch failed: {ex}");
            }
        }

        // Set description (preview panel body + screen-reader line): the native
        // string.Format inserts the enum value ("1002"); build the same line with
        // the set name instead.
        internal static bool SetConfig_GetDescription_Prefix(SetConfig __instance, ref string __result)
        {
            try
            {
                PackSetInfo info = ModSets.FindSet(__instance.expansion);
                if (info == null)
                {
                    return true; // native set — run original
                }
                int count = AssetManager.allCards.FindAll(x => x.cardexpansion == __instance.expansion).Count;
                try
                {
                    __result = string.Format(LocalizationHelper.GetLocalValue("UI", "SET_DESCRIPTION_LINE"),
                        info.DisplayName, count, "");
                }
                catch
                {
                    __result = $"The {info.DisplayName} set contains {count} cards.";
                }
                return false;
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] GetDescription patch failed: {ex}");
                return true;
            }
        }

        // "x/y sets" recap under the card-selection button: the native count only
        // includes sets that have a Resources SetConfig, so mod sets would push
        // the fraction negative-of-intent. Recompute with mod sets included.
        internal static void ExpansionInfo_Postfix(NameSelectorDisplay __instance)
        {
            try
            {
                if (ModSets.PackSets.Count == 0)
                {
                    return;
                }
                int total = ModSets.PackSets.Count;
                foreach (AssetManager.CardExpansions value in Enum.GetValues(typeof(AssetManager.CardExpansions)))
                {
                    if (Resources.Load<SetConfig>("Sets/" + value) != null)
                    {
                        total++;
                    }
                }
                int excluded = PlayerHandler.thePlayerData.excludedsets.Count;
                if (excluded > 0)
                {
                    __instance.cardSelectionText.text =
                        LocalizationHelper.GetLocalValue("UI", "CREATION_LIMITEDCARDS") + $" ({total - excluded}/{total})";
                }
                else
                {
                    __instance.cardSelectionText.text = LocalizationHelper.GetLocalValue("UI", "CREATION_ALLCARDS");
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] ExpansionInfo patch failed: {ex}");
            }
        }

        // ------------------------------------------------------------------
        // Set-count bonuses: GetBonusTalent/GetBonusTransmute grant extra talent
        // rerolls/transmutes based on excludedsets.Count. Mod sets must not eat
        // those bonuses when disabled, so count only native exclusions (P10).
        // ------------------------------------------------------------------

        private static int NativeExcludedCount()
        {
            return PlayerHandler.thePlayerData.excludedsets.Count(e => !ModSets.IsModExpansion(e));
        }

        internal static bool GetBonusTalent_Prefix(int amount, ref int __result)
        {
            try
            {
                if (ModSets.PackSets.Count == 0)
                {
                    return true;
                }
                int c = NativeExcludedCount();
                if (c < 1) amount++;
                if (c < 3) amount++;
                __result = amount;
                return false;
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] GetBonusTalent patch failed: {ex}");
                return true;
            }
        }

        internal static bool GetBonusTransmute_Prefix(int amount, ref int __result)
        {
            try
            {
                if (ModSets.PackSets.Count == 0)
                {
                    return true;
                }
                int c = NativeExcludedCount();
                if (c < 2) amount++;
                if (c < 3) amount++;
                if (c < 4) amount++;
                __result = amount;
                return false;
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] GetBonusTransmute patch failed: {ex}");
                return true;
            }
        }

        // ------------------------------------------------------------------
        // Sunforge settings screen: same row pattern as the run-settings screen.
        // ------------------------------------------------------------------

        internal static void Sunforge_SetSettings_Postfix(SunforgeSettings __instance)
        {
            try
            {
                if (ModSets.PackSets.Count == 0)
                {
                    return;
                }
                if (SunforgeToggleSet == null)
                {
                    DawnKitPlugin.Log.LogError("[DawnKit] SunforgeSettings.ToggleSet unavailable — cannot add Sunforge set rows.");
                    return;
                }
                foreach (PackSetInfo set in ModSets.PackSets)
                {
                    PackSetInfo captured = set;
                    SetConfig cfg = captured.GetOrCreateConfig();
                    GameObject row = UnityEngine.Object.Instantiate(__instance.settingsOption, __instance.cardSettingsBox);
                    var sfb = row.GetComponent<SunforgeSettingButton>();
                    if (sfb == null)
                    {
                        UnityEngine.Object.Destroy(row);
                        continue;
                    }
                    sfb.SetDisplay(cfg);
                    sfb.preview.onClick.AddListener(() => __instance.ShowSet(cfg));
                    sfb.main.onClick.AddListener(() => SunforgeToggleSet.Invoke(__instance, new object[] { cfg, sfb }));
                    if (__instance.forgeHandler.inactiveSets.Contains(captured.Expansion))
                    {
                        sfb.SetDisabled();
                    }
                    else
                    {
                        sfb.SetEnabled();
                    }
                    DawnKitPlugin.Log.LogInfo($"[DawnKit] Sunforge set row added: {captured.DisplayName} -> (CardExpansions){(int)captured.Expansion}");
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Failed to add Sunforge set rows: {ex}");
            }
        }

        // Sunforge reroll economy: the native baseline is +1 reroll per disableable
        // set (Enum.GetValues.Length - 3) and -1 per excluded set. Mod sets aren't
        // in Enum.GetValues, so without this they'd cost a reroll when disabled but
        // never grant one when enabled. Raise the baseline by one per mod set so
        // mod sets follow the exact native rule (P11).
        internal static void InitializeSunforgeSettings_Postfix(SunforgeSettings __instance)
        {
            try
            {
                int packs = ModSets.PackSets.Count;
                if (packs > 0)
                {
                    __instance.forgeHandler.rerollTokens += packs;
                    __instance.rerollDisplay.text = __instance.forgeHandler.rerollTokens.ToString();
                    DawnKitPlugin.Log.LogInfo($"[DawnKit] Sunforge reroll baseline raised by {packs} (one per mod set).");
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Sunforge reroll patch failed: {ex}");
            }
        }
    }
}
