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

        /// <summary>First ID of the set's 100-ID block (the .AutoId() allocation source).</summary>
        public long IdBlockStart => 700000000L + (SetValue - 1000L) * 100L;

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
    /// block (CARD-PACK-SPEC.md §3) — either an explicit ID-REGISTRY block or the
    /// AutoId block hashed from author/modName (SPEC.md §4.3) — so it is stable
    /// across sessions, saves and machines.
    /// </summary>
    public static class Sets
    {
        private static readonly List<SetHandle> registered = new List<SetHandle>();

        /// <summary>All registered sets, in registration order.</summary>
        public static IReadOnlyList<SetHandle> All => registered;

        /// <summary>
        /// Register a mod card set on an automatically derived ID block (SPEC.md
        /// §4.3): block = 700,000,000 + (FNV1a32(lower(author + "/" + name)) %
        /// 1,000,000) * 100. Deterministic — the same author/name pair maps to the
        /// same block on every machine, forever. If the block is already owned by
        /// a different mod the registration is refused (null, rich conflict error)
        /// — no probing to adjacent blocks; use an explicit block instead.
        /// </summary>
        public static SetHandle Register(string displayName, string author)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                DawnKitPlugin.Log.LogWarning("[DawnKit] Sets.Register: empty display name — set refused.");
                return null;
            }
            if (string.IsNullOrEmpty(author))
            {
                DawnKitPlugin.Log.LogWarning($"[DawnKit] {displayName}: Sets.Register(name, author) needs a non-empty author (the AutoId block is hashed from author/name).");
                return null;
            }
            string ownerString = AutoIdAllocator.OwnerString(author, displayName);
            long block = AutoIdAllocator.BlockFor(ownerString);
            return RegisterOnBlock(displayName, block, author, ownerString);
        }

        /// <summary>
        /// Register a mod card set on an explicit ID block (docs/ID-REGISTRY.md).
        /// Returns null (with a logged error) when the block start is outside the
        /// mod range 700,000,000–799,999,999, not aligned to a 100-ID block, or
        /// already owned by a different mod — in that case callers should fall
        /// back to a native expansion and the content gets no set row (fail-safe,
        /// matches the pre-split loader).
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
            return RegisterOnBlock(displayName, idBlockStart, author, author ?? displayName);
        }

        private static SetHandle RegisterOnBlock(string displayName, long block, string author, string ownerString)
        {
            int setValue = 1000 + (int)((block - 700000000L) / 100L);
            foreach (SetHandle existing in registered)
            {
                if (existing.SetValue == setValue)
                {
                    if (string.Equals(existing.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                    {
                        return existing; // same set re-registered — idempotent
                    }
                    string msg = $"{ownerString}/{displayName}: set value {setValue} (ID block {block}) already owned by " +
                                 $"{existing.Author ?? existing.DisplayName} (set \"{existing.DisplayName}\"). {AutoIdAllocator.ExplicitBlockRemedy}";
                    DawnKitPlugin.Log.LogError($"[DawnKit] {msg} — set refused.");
                    RegistrationLedger.Record(new RegistrationInfo(ownerString, "set", setValue, displayName, false, msg));
                    RegistrationLedger.RecordConflict(ownerString, msg);
                    return null;
                }
            }

            // Claim the ID block in the AutoId allocator (SPEC.md §4.3): explicit
            // blocks claim too, so an AutoId mod hashing onto an already-shipped
            // block is refused with both owners named — hard refusal, no probing.
            if (!AutoIdAllocator.TryClaim(block, ownerString, out string claimedBy))
            {
                string msg = $"{ownerString}/{displayName}: ID block {block} already owned by '{claimedBy}'. " +
                             AutoIdAllocator.ExplicitBlockRemedy;
                DawnKitPlugin.Log.LogError($"[DawnKit] {msg} — set refused.");
                RegistrationLedger.Record(new RegistrationInfo(ownerString, "set", setValue, displayName, false, msg));
                RegistrationLedger.RecordConflict(ownerString, msg);
                return null;
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
