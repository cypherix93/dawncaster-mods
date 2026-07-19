using System;
using System.Linq;
using System.Reflection;
using DawnKit.Core.Lifecycle;
using Ink.Runtime;
using UnityEngine;

namespace DawnKit.Integration.Dialogues
{
    /// <summary>
    /// Story serving for mod events (EVENT-SPEC §3/§6). Vanilla
    /// DialogueManagerINK.StartDialogue ignores Dialogue.textFile and loads by
    /// NAME from Addressables/Resources (DialogueManagerINK.cs:260-344) — a
    /// runtime TextAsset exists in neither store, so this prefix replicates the
    /// vanilla success wiring against the registered story and skips the
    /// original. Vanilla names pass through untouched. Fail-safe: if the patch
    /// target or ANY tracked member is missing, <see cref="Available"/> is false
    /// and events are never injected — a node whose story can't be served must
    /// never reach the map.
    /// </summary>
    internal static class DialogueIntegration
    {
        /// <summary>[Events] Enabled config knob (set by DawnKitPlugin.Awake).</summary>
        internal static bool Enabled = true;

        /// <summary>Set by PatchManager when the StartDialogue prefix applied cleanly.</summary>
        internal static bool PatchApplied;

        // Tracked members (PatchManager resolves + logs found/missing for each).
        internal static FieldInfo DialogueTempField;
        internal static FieldInfo StoryField;
        internal static FieldInfo DialogueNameField;
        internal static FieldInfo AreaUIField;
        internal static MethodInfo HidePortraitMethod;
        internal static MethodInfo SetDialogueRunningMethod;
        internal static MethodInfo FadeUIInMethod;
        internal static MethodInfo EnableVisualDialogueUIMethod;
        internal static MethodInfo ProceedDialogueMethod;

        internal static bool MembersResolved =>
            DialogueTempField != null && StoryField != null && DialogueNameField != null &&
            AreaUIField != null && HidePortraitMethod != null && SetDialogueRunningMethod != null &&
            FadeUIInMethod != null && EnableVisualDialogueUIMethod != null && ProceedDialogueMethod != null;

        internal static bool Available => Enabled && PatchApplied && MembersResolved;

        /// <summary>
        /// Replicates, in order, the vanilla wiring for a served story
        /// (StartDialogue preamble + the OnAssetLoaded success branch,
        /// DialogueManagerINK.cs:260-307), then skips the original.
        /// </summary>
        internal static bool StartDialogue_Prefix(DialogueManagerINK __instance, string dialogueFile)
        {
            try
            {
                if (!Available || string.IsNullOrEmpty(dialogueFile))
                {
                    return true;
                }
                EventRegistration reg = Registry.Events.FirstOrDefault(r =>
                    r.Event != null && r.Spec != null &&
                    string.Equals(r.Spec.Name, dialogueFile, StringComparison.OrdinalIgnoreCase));
                if (reg == null)
                {
                    return true; // vanilla event — vanilla loading
                }

                DawnKitPlugin.Log.LogInfo($"[DawnKit] Serving mod event story \"{reg.Spec.Name}\" ({reg.Spec.Owner}).");
                DialogueTempField.SetValue(__instance, dialogueFile);
                HidePortraitMethod.Invoke(__instance, new object[] { 0f, 0f });
                SetDialogueRunningMethod.Invoke(null, new object[] { true });
                StoryField.SetValue(__instance, new Story(reg.Spec.StoryJson));
                DialogueNameField.SetValue(__instance, reg.Spec.Name);
                FadeUIInMethod.Invoke(__instance, null);
                EnableVisualDialogueUIMethod.Invoke(__instance, null);
                var areaUI = (AreaUI)AreaUIField.GetValue(__instance);
                areaUI.SetConversationUI(true);
                areaUI.HideAreaUI(0.25f);
                CanvasGroup cg = __instance.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.blocksRaycasts = true;
                }
                ProceedDialogueMethod.Invoke(__instance, new object[] { -1 });
                return false;
            }
            catch (Exception ex)
            {
                // Fall through to vanilla: its Addressables/Resources misses end in
                // HandleDialogueFailure (SetDialogueRunning(false) + CloseDialogue,
                // DialogueManagerINK.cs:316-343) — a clean abort, not a hang.
                DawnKitPlugin.Log.LogError($"[DawnKit] StartDialogue prefix failed for '{dialogueFile}' — falling back to vanilla loading: {ex}");
                return true;
            }
        }
    }
}
