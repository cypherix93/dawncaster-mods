using System;
using System.Collections.Generic;
using System.Text;

namespace DawnKit.Core.Ownership
{
    /// <summary>
    /// AutoId block allocation (SPEC.md §4.3): deterministic per-mod ID blocks so
    /// nobody does manual ID bookkeeping (P6).
    ///
    ///   block = 700,000,000 + (FNV1a32(utf8(lower(author + "/" + modName))) % 1,000,000) * 100
    ///
    /// 1,000,000 blocks of 100 IDs inside the verified-free mod range
    /// 700,000,000–799,999,999. Within a block: cards allocate bottom-up from the
    /// block start, weapons and starting cards top-down from the block end on one
    /// shared cursor (WEAPON-SPEC §3), weapon
    /// powers (talents) top-down from the block end in their own ID space
    /// ("talent IDs reuse the same numbers"). Same owner string → same block on
    /// every machine, forever (set values and save data depend on it) — the
    /// Python twin lives in tools/gamedata.py and both sides pin the same test
    /// vectors.
    ///
    /// Collision policy is HARD REFUSAL: a block already claimed by a different
    /// owner string refuses the registration with both owners named — never
    /// probing to adjacent blocks (silent drift would break the set-value and
    /// save-stability guarantees). The remedy is always an explicit block.
    /// </summary>
    internal static class AutoIdAllocator
    {
        internal const long ModRangeStart = 700000000L;
        internal const int BlockSize = 100;

        /// <summary>block start → owner string that claimed it (explicit sets claim too).</summary>
        private static readonly Dictionary<long, string> claims = new Dictionary<long, string>();

        private sealed class Cursors
        {
            internal int NextCardOffset;              // bottom-up: 0, 1, 2, …
            internal int NextWeaponOffset = BlockSize - 1;  // top-down: 99, 98, …
            internal int NextTalentOffset = BlockSize - 1;  // top-down, separate ID space
        }

        private static readonly Dictionary<long, Cursors> cursors = new Dictionary<long, Cursors>();

        /// <summary>FNV-1a 32-bit (offset 2166136261, prime 16777619) over UTF-8 bytes.</summary>
        internal static uint Fnv1a32(string text)
        {
            uint hash = 2166136261u;
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return hash;
        }

        /// <summary>Canonical owner string: lower(author + "/" + modName).</summary>
        internal static string OwnerString(string author, string modName) =>
            ((author ?? "") + "/" + (modName ?? "")).ToLowerInvariant();

        internal static long BlockFor(string ownerString) =>
            ModRangeStart + (Fnv1a32(ownerString.ToLowerInvariant()) % 1000000u) * (long)BlockSize;

        /// <summary>
        /// Claim a block for an owner string. Idempotent for the same owner
        /// (case-insensitive); a different owner gets a refusal with the existing
        /// claimant returned. Explicit Sets.Register blocks claim through here
        /// too, so an AutoId mod hashing onto a shipped DC.* block is refused.
        /// </summary>
        internal static bool TryClaim(long block, string ownerString, out string existingOwner)
        {
            if (claims.TryGetValue(block, out existingOwner))
            {
                return string.Equals(existingOwner, ownerString, StringComparison.OrdinalIgnoreCase);
            }
            claims[block] = ownerString;
            existingOwner = null;
            return true;
        }

        internal static string ClaimantOf(long block)
        {
            claims.TryGetValue(block, out string owner);
            return owner;
        }

        private static Cursors CursorsFor(long block)
        {
            if (!cursors.TryGetValue(block, out Cursors c))
            {
                c = new Cursors();
                cursors[block] = c;
            }
            return c;
        }

        /// <summary>Next sequential card ID (bottom-up). Throws-free: returns -1 when the block is exhausted.</summary>
        internal static long AllocateCardId(long block)
        {
            Cursors c = CursorsFor(block);
            if (c.NextCardOffset > c.NextWeaponOffset)
            {
                return -1; // cards met the weapon cursor — 100-ID block exhausted
            }
            return block + c.NextCardOffset++;
        }

        /// <summary>Next loadout cardID (top-down from block end) — weapons and
        /// starting cards share this cursor (WEAPON-SPEC §3). -1 when exhausted.</summary>
        internal static long AllocateWeaponId(long block)
        {
            Cursors c = CursorsFor(block);
            if (c.NextWeaponOffset < c.NextCardOffset)
            {
                return -1;
            }
            return block + c.NextWeaponOffset--;
        }

        /// <summary>Next talentID (top-down, separate ID space). -1 when exhausted.</summary>
        internal static long AllocateTalentId(long block)
        {
            Cursors c = CursorsFor(block);
            if (c.NextTalentOffset < 0)
            {
                return -1;
            }
            return block + c.NextTalentOffset--;
        }

        /// <summary>The standard remedy line appended to every AutoId refusal.</summary>
        internal const string ExplicitBlockRemedy =
            "Fix: register an explicit free block instead — DawnKit.Sets.Register(name, idBlockStart: <a free " +
            "700,000,000–799,999,999 block aligned to 100; see docs/ID-REGISTRY.md>, author: ...) and use .Id(...) " +
            "or .InSet(set).AutoId().";

        // ------------------------------------------------------------------
        // Cross-language reference vectors (SPEC.md §8): the same five vectors
        // are pinned by tools/tests/test_autoid.py against the Python twin in
        // tools/gamedata.py. Checked once at boot — a mismatch means the two
        // implementations drifted and AutoId determinism is broken.
        // ------------------------------------------------------------------
        private static readonly object[,] TestVectors =
        {
            { "DCMods", "Example", 3497143552u, 714355200L },
            { "alice", "frostpack", 1346137019u, 713701900L },
            { "Bob", "VenomWorks", 4193822216u, 782221600L },
            { "dcmods.example", "My First Mod", 2376734u, 737673400L },
            { "Zoë", "Æther Deck", 718123484u, 712348400L },
        };

        /// <summary>Boot self-check of the reference vectors. Returns true when all five agree.</summary>
        internal static bool SelfCheck()
        {
            bool ok = true;
            for (int i = 0; i < TestVectors.GetLength(0); i++)
            {
                string owner = OwnerString((string)TestVectors[i, 0], (string)TestVectors[i, 1]);
                uint hash = Fnv1a32(owner);
                long block = BlockFor(owner);
                if (hash != (uint)TestVectors[i, 2] || block != (long)TestVectors[i, 3])
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] AutoId self-check FAILED for '{owner}': " +
                        $"hash={hash} (expected {TestVectors[i, 2]}), block={block} (expected {TestVectors[i, 3]}) — " +
                        "AutoId determinism is broken; do not trust auto-derived blocks this session.");
                    ok = false;
                }
            }
            return ok;
        }
    }
}
