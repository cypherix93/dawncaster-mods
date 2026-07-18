using System.Collections.Generic;
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
            string owner = draft.Owner ?? "(unknown)";
            ParsedCard spec;
            try
            {
                spec = Validator.ParseCard(draft, isWeapon);
            }
            catch (ManifestError me)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {owner}/{draft.Name ?? "?"}: {me.Message} — {(isWeapon ? "weapon" : "card")} skipped.");
                RegistrationLedger.Record(new RegistrationInfo(owner, kind, draft.CardId, draft.Name, false, me.Message));
                return RegisterResult.Failed(kind, owner, draft.Name, me.Message);
            }

            Cards.Add(new CardRegistration { Spec = spec });
            NoteOwner(spec.Owner);
            RegistrationLedger.Record(new RegistrationInfo(spec.Owner, kind, spec.CardId, spec.Name, true, null));
            return RegisterResult.Success(kind, spec.Owner, spec.Name);
        }

        internal static RegisterResult RegisterTalent(TalentDraft draft)
        {
            const string kind = "weaponPower";
            string owner = draft.Owner ?? "(unknown)";
            ParsedTalent spec;
            try
            {
                spec = Validator.ParseTalent(draft);
            }
            catch (ManifestError me)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {owner}/{draft.Name ?? "?"}: {me.Message} — weapon power skipped.");
                RegistrationLedger.Record(new RegistrationInfo(owner, kind, draft.TalentId, draft.Name, false, me.Message));
                return RegisterResult.Failed(kind, owner, draft.Name, me.Message);
            }

            Talents.Add(new TalentRegistration { Spec = spec });
            NoteOwner(spec.Owner);
            RegistrationLedger.Record(new RegistrationInfo(spec.Owner, kind, spec.TalentId, spec.Name, true, null));
            return RegisterResult.Success(kind, spec.Owner, spec.Name);
        }

        private static void NoteOwner(string owner)
        {
            if (!OwnerOrder.Contains(owner))
            {
                OwnerOrder.Add(owner);
            }
        }
    }
}
