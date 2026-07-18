using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using DawncasterLocalisation;
using UnityEngine;

namespace Dawncaster.Sandbox
{
    /// <summary>
    /// One entry per loaded mod pack that carries a synthetic CardExpansions value.
    ///
    /// Synthetic set formula (CARD-PACK-SPEC.md §3 amendment):
    ///   expansion = 1000 + (idBlock.start - 700,000,000) / 100
    /// Deterministic from the pack's ID-REGISTRY block, so the value is stable
    /// across sessions, saves and machines. The whole game pipeline stores
    /// CardExpansions as ints (JsonUtility save data, PlayerPrefs "lastExcludedSets"
    /// round-trips through Enum.TryParse which accepts numeric strings), so
    /// undefined enum members survive persistence fine.
    /// </summary>
    internal sealed class PackSetInfo
    {
        internal string DisplayName;
        internal AssetManager.CardExpansions Expansion;
        internal readonly List<Card> Cards = new List<Card>();

        private SetConfig config;

        // (primary=row/fade tint, secondary=border) pairs, indexed by set value.
        private static readonly Color[,] Palette =
        {
            { new Color(0.55f, 0.22f, 0.10f), new Color(1.00f, 0.62f, 0.25f) }, // ember
            { new Color(0.20f, 0.38f, 0.14f), new Color(0.62f, 0.85f, 0.32f) }, // venom
            { new Color(0.42f, 0.33f, 0.13f), new Color(0.95f, 0.82f, 0.45f) }, // brass
            { new Color(0.42f, 0.10f, 0.14f), new Color(0.92f, 0.30f, 0.35f) }, // crimson
            { new Color(0.16f, 0.20f, 0.45f), new Color(0.45f, 0.55f, 0.95f) }, // indigo
            { new Color(0.10f, 0.36f, 0.36f), new Color(0.30f, 0.80f, 0.80f) }, // teal
            { new Color(0.32f, 0.16f, 0.42f), new Color(0.72f, 0.45f, 0.95f) }, // violet
            { new Color(0.25f, 0.28f, 0.32f), new Color(0.65f, 0.72f, 0.80f) }, // slate
        };

        internal SetConfig GetOrCreateConfig()
        {
            if (config != null)
            {
                return config;
            }
            int paletteIndex = Math.Abs((int)Expansion - 1000) % (Palette.Length / 2);
            Color primary = Palette[paletteIndex, 0];
            Color secondary = Palette[paletteIndex, 1];

            config = ScriptableObject.CreateInstance<SetConfig>();
            config.name = DisplayName; // read by accessibility Say() on toggle
            config.hideFlags = HideFlags.HideAndDontSave;
            config.expansion = Expansion;
            config.canBeDisabled = true;
            config.setKeywords = new List<string>();
            config.setDescription = $"Mod pack \"{DisplayName}\" ({Cards.Count} cards), loaded by Dawncaster Sandbox.";
            config.setPrimaryColor = primary;
            config.setSecondaryColor = secondary;
            config.previewCards = BuildPreviewCards();
            config.setPromoImage = CreatePromoSprite(primary, secondary);
            return config;
        }

        /// <summary>
        /// SetPreviewPanel.SetDisplay indexes set.previewCards[i] for each of ITS
        /// CardDisplay slots, so the list must be at least as long as the panel's
        /// slot count (unknown here). Pad by cycling to a safe length.
        /// </summary>
        private List<Card> BuildPreviewCards()
        {
            var list = new List<Card>();
            if (Cards.Count == 0)
            {
                return list;
            }
            const int safeLength = 12;
            for (int i = 0; i < Math.Max(Cards.Count, safeLength); i++)
            {
                list.Add(Cards[i % Cards.Count]);
            }
            return list;
        }

        /// <summary>Vertical two-tone banner so the row doesn't render a flat white block.</summary>
        private static Sprite CreatePromoSprite(Color primary, Color secondary)
        {
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
            var pixels = new Color32[w * h];
            Color32 top = Color.Lerp(primary, secondary, 0.35f);
            Color32 bottom = Color.Lerp(primary, Color.black, 0.45f);
            for (int y = 0; y < h; y++)
            {
                Color32 c = Color32.Lerp(bottom, top, (float)y / (h - 1));
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    pixels[row + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }

    /// <summary>
    /// Harmony patches that surface mod packs as distinct card sets in the
    /// run-settings screen (NameSelectorDisplay), the Sunforge settings screen,
    /// and the Codex. Every patch is skip-don't-crash: failures are logged and
    /// the native behavior is left intact.
    /// </summary>
    [HarmonyPatch]
    internal static class SetScreenPatches
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("PackLoader.UI");

        // ------------------------------------------------------------------
        // Run-settings "card sets" screen: one native-cloned row per mod pack.
        // Native rows are built in NameSelectorDisplay.SetSettings() from
        // Resources.Load<SetConfig>("Sets/" + enumValue); synthetic values have
        // no resource (and are not returned by Enum.GetValues), so we append
        // our rows after the native ones using the same prefab + wiring.
        // ------------------------------------------------------------------

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NameSelectorDisplay), "SetSettings")]
        private static void NameSelector_SetSettings_Postfix(NameSelectorDisplay __instance)
        {
            try
            {
                if (PackLoader.PackSets.Count == 0)
                {
                    return;
                }
                MethodInfo toggleSet = AccessTools.Method(typeof(NameSelectorDisplay), "ToggleSet",
                    new[] { typeof(SetConfig), typeof(SunforgeSettingButton) });
                foreach (PackSetInfo set in PackLoader.PackSets)
                {
                    PackSetInfo captured = set;
                    SetConfig cfg = captured.GetOrCreateConfig();
                    GameObject row = UnityEngine.Object.Instantiate(__instance.setSelectionSetting, __instance.setOverview);
                    var sfb = row.GetComponent<SunforgeSettingButton>();
                    if (sfb == null)
                    {
                        UnityEngine.Object.Destroy(row);
                        Log.LogError($"[PackLoader] Row prefab has no SunforgeSettingButton — cannot add set row for {captured.DisplayName}.");
                        continue;
                    }
                    sfb.SetDisplay(cfg);        // display-name fix happens in the SetDisplay postfix below
                    sfb.bonus.text = "";        // native SetSettings blanks this on every row too
                    sfb.preview.onClick.AddListener(() => __instance.ShowSet(cfg)); // eye icon = native preview panel
                    sfb.main.onClick.AddListener(() =>
                    {
                        toggleSet.Invoke(__instance, new object[] { cfg, sfb }); // native excludedsets add/remove
                        bool excluded = PlayerHandler.thePlayerData.excludedsets.Contains(captured.Expansion);
                        Log.LogInfo($"[PackLoader] Set toggle: {captured.DisplayName} ((CardExpansions){(int)captured.Expansion}) excluded={excluded}");
                    });
                    if (PlayerHandler.thePlayerData.excludedsets.Contains(captured.Expansion))
                    {
                        sfb.SetDisabled();
                    }
                    else
                    {
                        sfb.SetEnabled();
                    }
                    Log.LogInfo($"[PackLoader] Run-settings set row added: {captured.DisplayName} -> (CardExpansions){(int)captured.Expansion}, {captured.Cards.Count} cards");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[PackLoader] Failed to add run-settings set rows: {ex}");
            }
        }

        // Row label: native code renders thisSet.expansion.ToString(), which for a
        // synthetic member is the raw int ("1002"). Swap in the pack name.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SunforgeSettingButton), nameof(SunforgeSettingButton.SetDisplay), typeof(SetConfig))]
        private static void SettingButton_SetDisplay_Postfix(SunforgeSettingButton __instance, SetConfig set)
        {
            try
            {
                PackSetInfo info = PackLoader.FindSet(set.expansion);
                if (info != null)
                {
                    __instance.mainText.text = info.DisplayName;
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[PackLoader] SetDisplay name patch failed: {ex}");
            }
        }

        // Preview (eye icon) panel title: same raw-int problem as the row label.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SetPreviewPanel), nameof(SetPreviewPanel.SetDisplay), typeof(SetConfig))]
        private static void SetPreviewPanel_SetDisplay_Postfix(SetPreviewPanel __instance, SetConfig set)
        {
            try
            {
                PackSetInfo info = set != null ? PackLoader.FindSet(set.expansion) : null;
                if (info != null)
                {
                    __instance.setTitle.text = info.DisplayName;
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[PackLoader] SetPreviewPanel title patch failed: {ex}");
            }
        }

        // Set description (preview panel body + screen-reader line): the native
        // string.Format inserts the enum value ("1002"); build the same line with
        // the pack name instead.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SetConfig), nameof(SetConfig.GetDescription))]
        private static bool SetConfig_GetDescription_Prefix(SetConfig __instance, ref string __result)
        {
            try
            {
                PackSetInfo info = PackLoader.FindSet(__instance.expansion);
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
                Log.LogError($"[PackLoader] GetDescription patch failed: {ex}");
                return true;
            }
        }

        // "x/y sets" recap under the card-selection button: the native count only
        // includes sets that have a Resources SetConfig, so mod sets would push
        // the fraction negative-of-intent. Recompute with mod sets included.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(NameSelectorDisplay), nameof(NameSelectorDisplay.ExpansionInfo))]
        private static void ExpansionInfo_Postfix(NameSelectorDisplay __instance)
        {
            try
            {
                if (PackLoader.PackSets.Count == 0)
                {
                    return;
                }
                int total = PackLoader.PackSets.Count;
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
                Log.LogError($"[PackLoader] ExpansionInfo patch failed: {ex}");
            }
        }

        // ------------------------------------------------------------------
        // Set-count bonuses: GetBonusTalent/GetBonusTransmute grant extra talent
        // rerolls/transmutes based on excludedsets.Count. Mod sets must not eat
        // those bonuses when disabled, so count only native exclusions.
        // ------------------------------------------------------------------

        private static int NativeExcludedCount()
        {
            return PlayerHandler.thePlayerData.excludedsets.Count(e => !PackLoader.IsModExpansion(e));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CreateCharacterFunctions), nameof(CreateCharacterFunctions.GetBonusTalent))]
        private static bool GetBonusTalent_Prefix(int amount, ref int __result)
        {
            try
            {
                if (PackLoader.PackSets.Count == 0)
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
                Log.LogError($"[PackLoader] GetBonusTalent patch failed: {ex}");
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CreateCharacterFunctions), nameof(CreateCharacterFunctions.GetBonusTransmute))]
        private static bool GetBonusTransmute_Prefix(int amount, ref int __result)
        {
            try
            {
                if (PackLoader.PackSets.Count == 0)
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
                Log.LogError($"[PackLoader] GetBonusTransmute patch failed: {ex}");
                return true;
            }
        }

        // ------------------------------------------------------------------
        // Sunforge settings screen: same row pattern as the run-settings screen.
        // ------------------------------------------------------------------

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SunforgeSettings), "SetSettings")]
        private static void Sunforge_SetSettings_Postfix(SunforgeSettings __instance)
        {
            try
            {
                if (PackLoader.PackSets.Count == 0)
                {
                    return;
                }
                MethodInfo toggleSet = AccessTools.Method(typeof(SunforgeSettings), "ToggleSet",
                    new[] { typeof(SetConfig), typeof(SunforgeSettingButton) });
                foreach (PackSetInfo set in PackLoader.PackSets)
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
                    sfb.main.onClick.AddListener(() => toggleSet.Invoke(__instance, new object[] { cfg, sfb }));
                    if (__instance.forgeHandler.inactiveSets.Contains(captured.Expansion))
                    {
                        sfb.SetDisabled();
                    }
                    else
                    {
                        sfb.SetEnabled();
                    }
                    Log.LogInfo($"[PackLoader] Sunforge set row added: {captured.DisplayName} -> (CardExpansions){(int)captured.Expansion}");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[PackLoader] Failed to add Sunforge set rows: {ex}");
            }
        }

        // Sunforge reroll economy: the native baseline is +1 reroll per disableable
        // set (Enum.GetValues.Length - 3) and -1 per excluded set. Mod sets aren't
        // in Enum.GetValues, so without this they'd cost a reroll when disabled but
        // never grant one when enabled. Raise the baseline by one per mod pack so
        // mod sets follow the exact native rule.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SunforgeSettings), nameof(SunforgeSettings.InitializeSunforgeSettings))]
        private static void InitializeSunforgeSettings_Postfix(SunforgeSettings __instance)
        {
            try
            {
                int packs = PackLoader.PackSets.Count;
                if (packs > 0)
                {
                    __instance.forgeHandler.rerollTokens += packs;
                    __instance.rerollDisplay.text = __instance.forgeHandler.rerollTokens.ToString();
                    Log.LogInfo($"[PackLoader] Sunforge reroll baseline raised by {packs} (one per mod pack).");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[PackLoader] Sunforge reroll patch failed: {ex}");
            }
        }

        // ------------------------------------------------------------------
        // Codex.
        // ------------------------------------------------------------------

        // Discovery: a card renders as discovered when its ID is in
        // CodexHandler.codex.cardList (Codex.dtt). Add mod IDs in memory whenever
        // the codex (re)loads; PackLoader also calls this right after injection.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CodexHandler), nameof(CodexHandler.LoadCodex))]
        private static void LoadCodex_Postfix()
        {
            PackLoader.MarkModCardsDiscovered("LoadCodex");
        }

        // The Codex expansion filter list is seeded from Enum.GetValues, which
        // never yields synthetic values — without this, mod cards are filtered
        // out of the list the moment any filter runs.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CodexUI), "Start")]
        private static void CodexUI_Start_Postfix(CodexUI __instance)
        {
            try
            {
                if (PackLoader.PackSets.Count == 0)
                {
                    return;
                }
                var shown = (List<AssetManager.CardExpansions>)AccessTools
                    .Field(typeof(CodexUI), "shownExpansions").GetValue(__instance);
                int added = 0;
                foreach (PackSetInfo set in PackLoader.PackSets)
                {
                    if (!shown.Contains(set.Expansion))
                    {
                        shown.Add(set.Expansion);
                        added++;
                    }
                }
                if (added > 0)
                {
                    Log.LogInfo($"[PackLoader] Codex: {added} mod expansions added to shownExpansions filter.");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[PackLoader] CodexUI filter patch failed: {ex}");
            }
        }

        // ------------------------------------------------------------------
        // Verification probe: per-set run-pool counts every time the run pool is
        // rebuilt. This is the log evidence that toggling a mod set actually
        // includes/excludes its cards.
        // ------------------------------------------------------------------

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AssetManager), nameof(AssetManager.CreateRunLists))]
        private static void CreateRunLists_Postfix()
        {
            try
            {
                if (PackLoader.PackSets.Count == 0)
                {
                    return;
                }
                var runcards = (List<Card>)AccessTools.Field(typeof(AssetManager), "_runcards").GetValue(null);
                if (runcards == null || PlayerHandler.thePlayerData == null)
                {
                    return;
                }
                string perSet = string.Join(", ", PackLoader.PackSets.Select(s =>
                    $"{s.DisplayName}={runcards.Count(c => c != null && c.cardexpansion == s.Expansion)}"));
                string excluded = string.Join(",", PlayerHandler.thePlayerData.excludedsets.Select(e => (int)e));
                Log.LogInfo($"[PackLoader] CreateRunLists: runcards={runcards.Count} [{perSet}] excludedsets=[{excluded}]");
            }
            catch (Exception ex)
            {
                Log.LogError($"[PackLoader] CreateRunLists probe failed: {ex}");
            }
        }
    }
}
