using System;
using System.Collections.Generic;
using System.Reflection;
using DawnKit.Core.Lifecycle;
using DawnKit.Core.Status;
using DawnKit.Integration.Codex;
using DawnKit.Integration.Dialogues;
using DawnKit.Integration.Sets;
using HarmonyLib;

namespace DawnKit.Core
{
    /// <summary>
    /// Applies every Harmony patch the engine owns (SPEC.md §6 — the complete
    /// inventory; client mods never patch the game). Each target is resolved and
    /// logged at boot — "Target found: X" / "Target MISSING: X" (ftk2/EOR
    /// practice) — so breakage after game updates is diagnosable from logs alone.
    /// A missing target disables only that integration; patch application is
    /// per-target try/catch (fail-safe rule).
    /// </summary>
    internal static class PatchManager
    {
        internal static int TargetCount { get; private set; }
        internal static int FoundCount { get; private set; }

        private sealed class PatchDef
        {
            internal string Label;
            internal Func<MethodInfo> Target;
            internal MethodInfo Prefix;
            internal MethodInfo Postfix;
            internal Action OnApplied;
        }

        internal static void ApplyAll(Harmony harmony)
        {
            var defs = new List<PatchDef>
            {
                // ---- Lifecycle (phase 1: player assets; phase 2: world assets) ----
                Postfix("AssetManager.SetPlayerAssetsLoaded",
                    () => AccessTools.Method(typeof(AssetManager), "SetPlayerAssetsLoaded"),
                    typeof(AssetLoadHooks), nameof(AssetLoadHooks.SetPlayerAssetsLoaded_Postfix)),
                Postfix("AssetManager.LoadPlayerAssets",
                    () => AccessTools.Method(typeof(AssetManager), "LoadPlayerAssets"),
                    typeof(AssetLoadHooks), nameof(AssetLoadHooks.LoadPlayerAssets_Postfix)),
                Postfix("AssetManager.SetWorldAssetsLoaded",
                    () => AccessTools.Method(typeof(AssetManager), "SetWorldAssetsLoaded"),
                    typeof(AssetLoadHooks), nameof(AssetLoadHooks.SetWorldAssetsLoaded_Postfix)),
                Postfix("AssetManager.LoadWorldAssets",
                    () => AccessTools.Method(typeof(AssetManager), "LoadWorldAssets"),
                    typeof(AssetLoadHooks), nameof(AssetLoadHooks.LoadWorldAssets_Postfix)),

                // ---- Set screens (Integration.Sets) ----
                Postfix("NameSelectorDisplay.SetSettings",
                    () => AccessTools.Method(typeof(NameSelectorDisplay), "SetSettings"),
                    typeof(SetScreenIntegration), nameof(SetScreenIntegration.NameSelector_SetSettings_Postfix)),
                Postfix("SunforgeSettingButton.SetDisplay(SetConfig)",
                    () => AccessTools.Method(typeof(SunforgeSettingButton), "SetDisplay", new[] { typeof(SetConfig) }),
                    typeof(SetScreenIntegration), nameof(SetScreenIntegration.SettingButton_SetDisplay_Postfix)),
                Postfix("SetPreviewPanel.SetDisplay(SetConfig)",
                    () => AccessTools.Method(typeof(SetPreviewPanel), "SetDisplay", new[] { typeof(SetConfig) }),
                    typeof(SetScreenIntegration), nameof(SetScreenIntegration.SetPreviewPanel_SetDisplay_Postfix)),
                Prefix("SetConfig.GetDescription",
                    () => AccessTools.Method(typeof(SetConfig), "GetDescription"),
                    typeof(SetScreenIntegration), nameof(SetScreenIntegration.SetConfig_GetDescription_Prefix)),
                Postfix("NameSelectorDisplay.ExpansionInfo",
                    () => AccessTools.Method(typeof(NameSelectorDisplay), "ExpansionInfo"),
                    typeof(SetScreenIntegration), nameof(SetScreenIntegration.ExpansionInfo_Postfix)),
                Prefix("CreateCharacterFunctions.GetBonusTalent",
                    () => AccessTools.Method(typeof(CreateCharacterFunctions), "GetBonusTalent"),
                    typeof(SetScreenIntegration), nameof(SetScreenIntegration.GetBonusTalent_Prefix)),
                Prefix("CreateCharacterFunctions.GetBonusTransmute",
                    () => AccessTools.Method(typeof(CreateCharacterFunctions), "GetBonusTransmute"),
                    typeof(SetScreenIntegration), nameof(SetScreenIntegration.GetBonusTransmute_Prefix)),
                Postfix("SunforgeSettings.SetSettings",
                    () => AccessTools.Method(typeof(SunforgeSettings), "SetSettings"),
                    typeof(SetScreenIntegration), nameof(SetScreenIntegration.Sunforge_SetSettings_Postfix)),
                Postfix("SunforgeSettings.InitializeSunforgeSettings",
                    () => AccessTools.Method(typeof(SunforgeSettings), "InitializeSunforgeSettings"),
                    typeof(SetScreenIntegration), nameof(SetScreenIntegration.InitializeSunforgeSettings_Postfix)),

                // ---- Codex (Integration.Codex) ----
                Postfix("CodexHandler.LoadCodex",
                    () => AccessTools.Method(typeof(CodexHandler), "LoadCodex"),
                    typeof(CodexIntegration), nameof(CodexIntegration.LoadCodex_Postfix)),
                Postfix("CodexUI.Start",
                    () => AccessTools.Method(typeof(CodexUI), "Start"),
                    typeof(CodexIntegration), nameof(CodexIntegration.CodexUI_Start_Postfix)),

                // ---- Verification probe (Core.Status) ----
                Postfix("AssetManager.CreateRunLists",
                    () => AccessTools.Method(typeof(AssetManager), "CreateRunLists"),
                    typeof(RunListProbe), nameof(RunListProbe.CreateRunLists_Postfix)),

                // ---- Events (Integration.Dialogues) ----
                new PatchDef
                {
                    Label = "DialogueManagerINK.StartDialogue(string)",
                    Target = () => AccessTools.Method(typeof(DialogueManagerINK), "StartDialogue", new[] { typeof(string) }),
                    Prefix = AccessTools.Method(typeof(DialogueIntegration), nameof(DialogueIntegration.StartDialogue_Prefix)),
                    OnApplied = () => DialogueIntegration.PatchApplied = true,
                },
            };

            TargetCount = defs.Count;
            FoundCount = 0;
            foreach (PatchDef def in defs)
            {
                MethodInfo target = null;
                try
                {
                    target = def.Target();
                }
                catch (Exception ex)
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] Target resolution threw for {def.Label}: {ex.Message}");
                }
                if (target == null)
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] Target MISSING: {def.Label} — that integration is disabled.");
                    continue;
                }
                DawnKitPlugin.Log.LogInfo($"[DawnKit] Target found: {def.Label}");
                try
                {
                    harmony.Patch(target,
                        prefix: def.Prefix != null ? new HarmonyMethod(def.Prefix) : null,
                        postfix: def.Postfix != null ? new HarmonyMethod(def.Postfix) : null);
                    FoundCount++;
                    def.OnApplied?.Invoke();
                }
                catch (Exception ex)
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] Patch failed for {def.Label} — that integration is disabled: {ex}");
                }
            }

            // Non-patch private members (AccessTools access at call sites) get the
            // same found/missing boot log; a missing member disables only its
            // integration (the consuming code null-checks).
            SetScreenIntegration.NameSelectorToggleSet = ResolveMember("NameSelectorDisplay.ToggleSet(SetConfig, SunforgeSettingButton)",
                () => AccessTools.Method(typeof(NameSelectorDisplay), "ToggleSet", new[] { typeof(SetConfig), typeof(SunforgeSettingButton) }));
            SetScreenIntegration.SunforgeToggleSet = ResolveMember("SunforgeSettings.ToggleSet(SetConfig, SunforgeSettingButton)",
                () => AccessTools.Method(typeof(SunforgeSettings), "ToggleSet", new[] { typeof(SetConfig), typeof(SunforgeSettingButton) }));
            CodexIntegration.ShownExpansionsField = ResolveMember("CodexUI.shownExpansions",
                () => AccessTools.Field(typeof(CodexUI), "shownExpansions"));
            RunListProbe.RuncardsField = ResolveMember("AssetManager._runcards",
                () => AccessTools.Field(typeof(AssetManager), "_runcards"));

            // Events story serving (EVENT-SPEC §6): all members the StartDialogue
            // prefix replays. A missing one disables the WHOLE Events integration
            // (injection included) — an unservable story must never reach the map.
            DialogueIntegration.DialogueTempField = ResolveMember("DialogueManagerINK.dialogueTemp",
                () => AccessTools.Field(typeof(DialogueManagerINK), "dialogueTemp"));
            DialogueIntegration.StoryField = ResolveMember("DialogueManagerINK.story",
                () => AccessTools.Field(typeof(DialogueManagerINK), "story"));
            DialogueIntegration.DialogueNameField = ResolveMember("DialogueManagerINK.dialogueName",
                () => AccessTools.Field(typeof(DialogueManagerINK), "dialogueName"));
            DialogueIntegration.AreaUIField = ResolveMember("DialogueManagerINK.areaUI",
                () => AccessTools.Field(typeof(DialogueManagerINK), "areaUI"));
            DialogueIntegration.HidePortraitMethod = ResolveMember("DialogueManagerINK.HidePortrait(float, float)",
                () => AccessTools.Method(typeof(DialogueManagerINK), "HidePortrait", new[] { typeof(float), typeof(float) }));
            DialogueIntegration.SetDialogueRunningMethod = ResolveMember("DialogueManagerINK.SetDialogueRunning(bool)",
                () => AccessTools.Method(typeof(DialogueManagerINK), "SetDialogueRunning", new[] { typeof(bool) }));
            DialogueIntegration.FadeUIInMethod = ResolveMember("DialogueManagerINK.FadeUIIn",
                () => AccessTools.Method(typeof(DialogueManagerINK), "FadeUIIn"));
            DialogueIntegration.EnableVisualDialogueUIMethod = ResolveMember("DialogueManagerINK.EnableVisualDialogueUI",
                () => AccessTools.Method(typeof(DialogueManagerINK), "EnableVisualDialogueUI"));
            DialogueIntegration.ProceedDialogueMethod = ResolveMember("DialogueManagerINK.ProceedDialogue(int)",
                () => AccessTools.Method(typeof(DialogueManagerINK), "ProceedDialogue", new[] { typeof(int) }));

            if (DialogueIntegration.Enabled && !(DialogueIntegration.PatchApplied && DialogueIntegration.MembersResolved))
            {
                DawnKitPlugin.Log.LogError("[DawnKit] Events integration disabled — StartDialogue target or a tracked member is missing; mod events will NOT be injected (fail-safe: an unservable story must never reach the map).");
            }
        }

        private static T ResolveMember<T>(string label, Func<T> resolve) where T : class
        {
            T member = null;
            try
            {
                member = resolve();
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Member resolution threw for {label}: {ex.Message}");
            }
            if (member == null)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Target MISSING: {label} (member) — that integration is disabled.");
                return null;
            }
            DawnKitPlugin.Log.LogInfo($"[DawnKit] Target found: {label} (member)");
            return member;
        }

        private static PatchDef Prefix(string label, Func<MethodInfo> target, Type patchType, string patchMethod) =>
            new PatchDef { Label = label, Target = target, Prefix = AccessTools.Method(patchType, patchMethod) };

        private static PatchDef Postfix(string label, Func<MethodInfo> target, Type patchType, string patchMethod) =>
            new PatchDef { Label = label, Target = target, Postfix = AccessTools.Method(patchType, patchMethod) };
    }
}
