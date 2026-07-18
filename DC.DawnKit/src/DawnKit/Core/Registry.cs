using System;
using System.Collections.Generic;
using System.Linq;
using DawnKit.Core.Ownership;

namespace DawnKit.Core.Lifecycle
{
    /// <summary>Pairs a constructed CardEffect with its parsed spec so refs can be resolved later.</summary>
    internal sealed class EffectBinding
    {
        internal CardEffect Effect;
        internal ParsedEffect Spec;
    }

    /// <summary>A durable card/weapon registration and (when injected) its live Card instance.</summary>
    internal sealed class CardRegistration
    {
        internal ParsedCard Spec;
        internal Card Card; // null until injected, or after being pruned by an asset wipe
        internal readonly List<EffectBinding> Bindings = new List<EffectBinding>();
    }

    /// <summary>A durable weapon-power registration and its live Talent instance.</summary>
    internal sealed class TalentRegistration
    {
        internal ParsedTalent Spec;
        internal Talent Talent;
        internal readonly List<EffectBinding> Bindings = new List<EffectBinding>();
    }

    /// <summary>
    /// The engine registration store. Registrations are declarative and durable
    /// (SPEC.md §3.3): recorded here at Register(), applied by InjectionEngine at
    /// the load phases, pruned+rebuilt after ForceReloadAssets wipes collections.
    /// Register() also runs the ownership checks (SPEC.md §3.5): AutoId
    /// allocation, cross-mod ID/name collision vs every other registered mod,
    /// and — once the pools are loaded — vs the shipped pool.
    /// </summary>
    internal static class Registry
    {
        internal static readonly List<CardRegistration> Cards = new List<CardRegistration>();
        internal static readonly List<TalentRegistration> Talents = new List<TalentRegistration>();
        /// <summary>Owner display names in first-registration order (injection log grouping).</summary>
        internal static readonly List<string> OwnerOrder = new List<string>();

        internal static RegisterResult RegisterCard(CardDraft draft, bool isWeapon)
        {
            string kind = isWeapon ? "weapon" : "card";
            if (string.IsNullOrEmpty(draft.Owner))
            {
                draft.Owner = OwnerResolver.ResolveCallingOwner();
            }
            string owner = draft.Owner;
            ParsedCard spec;
            try
            {
                ResolveAutoCardId(draft, isWeapon);
                spec = Validator.ParseCard(draft, isWeapon);
            }
            catch (ManifestError me)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {owner}/{draft.Name ?? "?"}: {me.Message} — {kind} skipped.");
                RegistrationLedger.Record(new RegistrationInfo(owner, kind, draft.CardId, draft.Name, false, me.Message));
                return RegisterResult.Failed(kind, owner, draft.Name, me.Message);
            }

            string conflict = FindCardConflict(spec.Owner, kind, spec.CardId, spec.Name);
            if (conflict != null)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {conflict} — {kind} refused.");
                RegistrationLedger.Record(new RegistrationInfo(spec.Owner, kind, spec.CardId, spec.Name, false, conflict));
                RegistrationLedger.RecordConflict(spec.Owner, conflict);
                return RegisterResult.Failed(kind, spec.Owner, spec.Name, conflict);
            }

            Cards.Add(new CardRegistration { Spec = spec });
            NoteOwner(spec.Owner);
            RegistrationLedger.Record(new RegistrationInfo(spec.Owner, kind, spec.CardId, spec.Name, true, null));
            return RegisterResult.Success(kind, spec.Owner, spec.Name);
        }

        internal static RegisterResult RegisterTalent(TalentDraft draft)
        {
            const string kind = "weaponPower";
            if (string.IsNullOrEmpty(draft.Owner))
            {
                draft.Owner = OwnerResolver.ResolveCallingOwner();
            }
            string owner = draft.Owner;
            ParsedTalent spec;
            try
            {
                ResolveAutoTalentId(draft);
                spec = Validator.ParseTalent(draft);
            }
            catch (ManifestError me)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {owner}/{draft.Name ?? "?"}: {me.Message} — weapon power skipped.");
                RegistrationLedger.Record(new RegistrationInfo(owner, kind, draft.TalentId, draft.Name, false, me.Message));
                return RegisterResult.Failed(kind, owner, draft.Name, me.Message);
            }

            string conflict = FindTalentConflict(spec.Owner, spec.TalentId, spec.Name);
            if (conflict != null)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {conflict} — weapon power refused.");
                RegistrationLedger.Record(new RegistrationInfo(spec.Owner, kind, spec.TalentId, spec.Name, false, conflict));
                RegistrationLedger.RecordConflict(spec.Owner, conflict);
                return RegisterResult.Failed(kind, spec.Owner, spec.Name, conflict);
            }

            Talents.Add(new TalentRegistration { Spec = spec });
            NoteOwner(spec.Owner);
            RegistrationLedger.Record(new RegistrationInfo(spec.Owner, kind, spec.TalentId, spec.Name, true, null));
            return RegisterResult.Success(kind, spec.Owner, spec.Name);
        }

        // ------------------------------------------------------------------
        // AutoId (SPEC.md §4.3): sequential IDs from the mod's block — the set's
        // block when .InSet(...) was called, else the block hashed from the owner
        // string. Hard refusal on block conflicts, never probing.
        // ------------------------------------------------------------------

        private static void ResolveAutoCardId(CardDraft draft, bool isWeapon)
        {
            if (!draft.AutoIdRequested)
            {
                return;
            }
            if (draft.CardId != 0)
            {
                throw new ManifestError("both .Id(...) and .AutoId() were called — pick one");
            }
            long block = ResolveBlock(draft.Set, draft.Owner, draft.Name);
            long id = isWeapon ? AutoIdAllocator.AllocateWeaponId(block) : AutoIdAllocator.AllocateCardId(block);
            if (id < 0)
            {
                throw new ManifestError(
                    $"AutoId block {block} is exhausted (100 IDs per block). {AutoIdAllocator.ExplicitBlockRemedy}");
            }
            draft.CardId = (int)id;
        }

        private static void ResolveAutoTalentId(TalentDraft draft)
        {
            if (!draft.AutoIdRequested)
            {
                return;
            }
            if (draft.TalentId != 0)
            {
                throw new ManifestError("both .Id(...) and .AutoId() were called — pick one");
            }
            long block = ResolveBlock(draft.Set, draft.Owner, draft.Name);
            long id = AutoIdAllocator.AllocateTalentId(block);
            if (id < 0)
            {
                throw new ManifestError(
                    $"AutoId block {block} is exhausted (100 IDs per block). {AutoIdAllocator.ExplicitBlockRemedy}");
            }
            draft.TalentId = (int)id;
        }

        private static long ResolveBlock(SetHandle set, string owner, string itemName)
        {
            if (set != null)
            {
                return set.IdBlockStart; // the set already claimed its block at Sets.Register
            }
            if (string.IsNullOrEmpty(owner) || owner == "(unknown)")
            {
                throw new ManifestError(".AutoId() needs an owner — call .InSet(set) or .Owner(\"author/ModName\") first");
            }
            long block = AutoIdAllocator.BlockFor(owner);
            if (!AutoIdAllocator.TryClaim(block, owner, out string existing))
            {
                throw new ManifestError(
                    $"AutoId block {block} (derived from owner '{owner}') is already owned by '{existing}'. " +
                    AutoIdAllocator.ExplicitBlockRemedy);
            }
            return block;
        }

        // ------------------------------------------------------------------
        // Cross-mod + shipped-pool collision checks at Register() (SPEC.md §3.5).
        // The shipped pool is only consulted once loaded (registrations during
        // Awake run before any assets exist — injection re-checks the live pools
        // either way). Returns the rich conflict message, or null.
        // ------------------------------------------------------------------

        private static string FindCardConflict(string owner, string kind, int cardId, string name)
        {
            RegistrationInfo other = RegistrationLedger.FindCardSpaceConflict(cardId, name);
            if (other != null)
            {
                return other.Id == cardId
                    ? $"{owner}/{name}: cardID {cardId} already owned by {other.Owner} ({other.Kind} \"{other.Name}\")"
                    : $"{owner}/{name}: name already owned by {other.Owner} ({other.Kind} \"{other.Name}\", cardID {other.Id})";
            }
            try
            {
                if (AssetManager.allCards != null && AssetManager.allCards.Count > 0)
                {
                    Card shipped = AssetManager.allCards.FirstOrDefault(c => c != null &&
                                       (c.cardID == cardId || string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)))
                                   ?? AssetManager.metacards?.FirstOrDefault(c => c != null &&
                                       (c.cardID == cardId || string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)));
                    if (shipped != null && !IsOurCard(shipped))
                    {
                        return shipped.cardID == cardId
                            ? $"{owner}/{name}: cardID {cardId} already owned by the shipped card pool (card \"{shipped.name}\")"
                            : $"{owner}/{name}: name already owned by the shipped card pool (cardID {shipped.cardID})";
                    }
                }
            }
            catch
            {
                // Pool probing must never break Register(); injection re-checks.
            }
            return null;
        }

        private static string FindTalentConflict(string owner, int talentId, string name)
        {
            RegistrationInfo other = RegistrationLedger.FindTalentSpaceConflict(talentId, name);
            if (other != null)
            {
                return other.Id == talentId
                    ? $"{owner}/{name}: talentID {talentId} already owned by {other.Owner} (weapon power \"{other.Name}\")"
                    : $"{owner}/{name}: name already owned by {other.Owner} (weapon power \"{other.Name}\", talentID {other.Id})";
            }
            try
            {
                if (AssetManager.allTalents != null && AssetManager.allTalents.Count > 0)
                {
                    Talent shipped = AssetManager.allTalents.FirstOrDefault(t => t != null &&
                        (t.ID == talentId || string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)));
                    if (shipped != null && !Talents.Any(r => r.Talent == shipped))
                    {
                        return shipped.ID == talentId
                            ? $"{owner}/{name}: talentID {talentId} already owned by the shipped talent pool (talent \"{shipped.name}\")"
                            : $"{owner}/{name}: name already owned by the shipped talent pool (talentID {shipped.ID})";
                    }
                }
            }
            catch
            {
                // Pool probing must never break Register(); injection re-checks.
            }
            return null;
        }

        private static bool IsOurCard(Card card) => Cards.Any(r => r.Card == card);

        private static void NoteOwner(string owner)
        {
            if (!OwnerOrder.Contains(owner))
            {
                OwnerOrder.Add(owner);
            }
        }
    }
}
