using System;
using System.Collections.Generic;
using DawnKit.Core.Ownership;

namespace DawnKit
{
    /// <summary>
    /// A registered mod card set. Cards placed in the set (CardBuilder.InSet) carry
    /// its synthetic CardExpansions value and the set surfaces as its own named,
    /// colored, toggleable row in the run-settings and Sunforge set screens,
    /// exactly like official sets (SPEC.md §2).
    /// </summary>
    public sealed class SetHandle
    {
        public string DisplayName { get; }
        public string Author { get; }
        /// <summary>Synthetic CardExpansions value: 1000 + (idBlockStart − 700,000,000) / 100.</summary>
        public int SetValue { get; }

        internal AssetManager.CardExpansions Expansion => (AssetManager.CardExpansions)SetValue;

        internal SetHandle(string displayName, string author, int setValue)
        {
            DisplayName = displayName;
            Author = author;
            SetValue = setValue;
        }
    }

    /// <summary>
    /// Public set registry (SPEC.md §4.1). Registering a set is one line; the
    /// synthetic expansion value is derived deterministically from the pack's ID
    /// block (CARD-PACK-SPEC.md §3), so it is stable across sessions, saves and
    /// machines. M1b adds AutoId (hash-allocated blocks, SPEC.md §4.3).
    /// </summary>
    public static class Sets
    {
        private static readonly List<SetHandle> registered = new List<SetHandle>();

        /// <summary>All registered sets, in registration order.</summary>
        public static IReadOnlyList<SetHandle> All => registered;

        /// <summary>
        /// Register a mod card set derived from an ID-REGISTRY block. Returns null
        /// (with a logged warning) when the block start is outside the mod range
        /// 700,000,000–799,999,999 or not aligned to a 100-ID block — in that case
        /// callers should fall back to a native expansion and the content gets no
        /// set row (fail-safe, matches the pre-split loader).
        /// </summary>
        public static SetHandle Register(string displayName, long idBlockStart, string author = null)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                DawnKitPlugin.Log.LogWarning("[DawnKit] Sets.Register: empty display name — set refused.");
                return null;
            }
            if (idBlockStart < 700000000L || idBlockStart > 799999999L || idBlockStart % 100 != 0)
            {
                DawnKitPlugin.Log.LogWarning($"[DawnKit] {displayName}: idBlock start {idBlockStart} is outside the mod range 700,000,000–799,999,999 or not block-aligned — no synthetic card set.");
                return null;
            }
            int setValue = 1000 + (int)((idBlockStart - 700000000L) / 100L);
            foreach (SetHandle existing in registered)
            {
                if (existing.SetValue == setValue)
                {
                    if (!string.Equals(existing.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                    {
                        // M1b: ownership registry turns this into a named conflict refusal (SPEC.md §3.5).
                        DawnKitPlugin.Log.LogWarning($"[DawnKit] Set value {setValue} already registered as '{existing.DisplayName}' — returning the existing set for '{displayName}'.");
                    }
                    return existing;
                }
            }
            var handle = new SetHandle(displayName, author, setValue);
            registered.Add(handle);
            RegistrationLedger.Record(new RegistrationInfo(author ?? displayName, "set", setValue, displayName, true, null));
            return handle;
        }

        /// <summary>Display name for a synthetic set value, or null if not registered.</summary>
        public static string FindDisplayName(int setValue)
        {
            foreach (SetHandle s in registered)
            {
                if (s.SetValue == setValue)
                {
                    return s.DisplayName;
                }
            }
            return null;
        }

        /// <summary>
        /// Validates a native expansion name against the game's CardExpansions enum
        /// (exact, then case-insensitive) and returns the canonical member name.
        /// Lets data-driven clients validate an ExpansionOverride knob without
        /// referencing game types.
        /// </summary>
        public static bool TryParseNativeExpansion(string raw, out string canonicalName)
        {
            canonicalName = null;
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }
            if (Enum.TryParse<AssetManager.CardExpansions>(raw, true, out var value) &&
                Enum.IsDefined(typeof(AssetManager.CardExpansions), value))
            {
                canonicalName = value.ToString();
                return true;
            }
            return false;
        }
    }
}
