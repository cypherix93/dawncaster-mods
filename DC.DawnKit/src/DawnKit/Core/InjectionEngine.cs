using System;
using System.Collections.Generic;
using System.Linq;
using DawnKit.Content.Factories;
using DawnKit.Core.Ownership;
using DawnKit.Core.RefResolver;
using DawnKit.Integration.Classes;
using DawnKit.Integration.Codex;
using DawnKit.Integration.Sets;

namespace DawnKit.Core.Lifecycle
{
    /// <summary>
    /// The engine's asset-load lifecycle (SPEC.md §3.3). Hooks both the async and
    /// sync boot paths (the sync path never calls SetPlayerAssetsLoaded — P2).
    /// Phase 1 (player assets loaded): construct + inject registered content,
    /// refresh caches and run lists, attach classes. Phase 2 (world assets
    /// loaded): resolve name-declared references (statuses only exist after
    /// CreateStatusCollections — an early GetStatus would re-enter loading, P4).
    /// Re-injection: ForceReloadAssets() clears every AssetManager list on a game
    /// version change; the hooks re-fire on the next load pass, wiped instances
    /// are pruned by list membership and rebuilt from the durable registrations
    /// (idempotent by ID/name — P3, P21).
    /// </summary>
    internal static class AssetLoadHooks
    {
        internal static void SetPlayerAssetsLoaded_Postfix() => InjectionEngine.OnPlayerAssetsLoaded("SetPlayerAssetsLoaded");

        internal static void LoadPlayerAssets_Postfix() => InjectionEngine.OnPlayerAssetsLoaded("LoadPlayerAssets");

        internal static void SetWorldAssetsLoaded_Postfix() => ReferenceResolver.Resolve("SetWorldAssetsLoaded", finalPass: true);

        internal static void LoadWorldAssets_Postfix() => ReferenceResolver.Resolve("LoadWorldAssets", finalPass: true);
    }

    internal static class InjectionEngine
    {
        internal static void OnPlayerAssetsLoaded(string source)
        {
            try
            {
                InjectInner(source);
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Content injection failed (hook: {source}): {ex}");
            }
        }

        private static void InjectInner(string source)
        {
            if (Registry.Cards.Count == 0 && Registry.Talents.Count == 0)
            {
                return;
            }

            // Prune instances wiped by ForceReloadAssets()/ClearAllCollections()
            // so they get rebuilt this pass (membership check, never object identity).
            foreach (CardRegistration r in Registry.Cards)
            {
                if (r.Card != null &&
                    !AssetManager.allCards.Contains(r.Card) &&
                    !AssetManager.metacards.Contains(r.Card))
                {
                    r.Card = null;
                    r.Bindings.Clear();
                }
            }
            foreach (TalentRegistration r in Registry.Talents)
            {
                if (r.Talent != null && !AssetManager.allTalents.Contains(r.Talent))
                {
                    r.Talent = null;
                    r.Bindings.Clear();
                }
            }

            foreach (string owner in Registry.OwnerOrder)
            {
                int injected = 0, skipped = 0, alreadyPresent = 0;
                int weaponsInjected = 0, powersInjected = 0, startingCardsInjected = 0;

                foreach (CardRegistration r in Registry.Cards)
                {
                    if (r.Spec.Owner == owner && r.Spec.Kind == CardKind.Card)
                    {
                        InjectCard(r, ref injected, ref skipped, ref alreadyPresent);
                    }
                }
                foreach (CardRegistration r in Registry.Cards)
                {
                    if (r.Spec.Owner == owner && r.Spec.IsWeapon)
                    {
                        InjectWeapon(r, ref weaponsInjected);
                    }
                }
                foreach (CardRegistration r in Registry.Cards)
                {
                    if (r.Spec.Owner == owner && r.Spec.IsStartingCard)
                    {
                        InjectStartingCard(r, ref startingCardsInjected);
                    }
                }
                foreach (TalentRegistration r in Registry.Talents)
                {
                    if (r.Spec.Owner == owner)
                    {
                        InjectPower(r, ref powersInjected);
                    }
                }

                if (injected + weaponsInjected + startingCardsInjected + powersInjected > 0)
                {
                    AssetManager.RefreshCaches();
                    if (PlayerHandler.thePlayerData != null)
                    {
                        AssetManager.CreateRunLists();
                    }
                }

                // Class attachment runs every pass (idempotent by ID/name) so a
                // ForceReloadAssets that re-fetched Profession assets — or wiped
                // and rebuilt our cards — always converges on the live instances.
                List<string> attachedClasses = ClassIntegration.AttachOwner(owner);

                // Register()-time failures count as skipped, like the pre-split loader.
                skipped += RegistrationLedger.FailedCount(owner, "card");
                bool hasLoadoutContent =
                    Registry.Cards.Any(r => r.Spec.Owner == owner && r.Spec.Kind != CardKind.Card) ||
                    Registry.Talents.Any(r => r.Spec.Owner == owner) ||
                    RegistrationLedger.FailedCount(owner, "weapon") > 0 ||
                    RegistrationLedger.FailedCount(owner, "weaponPower") > 0 ||
                    RegistrationLedger.FailedCount(owner, "startingCard") > 0;

                string presentNote = alreadyPresent > 0 ? $", {alreadyPresent} already present" : "";
                DawnKitPlugin.Log.LogInfo($"[DawnKit] {owner}: {injected} cards injected, {skipped} skipped{presentNote} (hook: {source})");
                if (hasLoadoutContent)
                {
                    DawnKitPlugin.Log.LogInfo($"[DawnKit] {owner}: {weaponsInjected} weapons, {powersInjected} weapon powers, {startingCardsInjected} starting cards injected (classes: {(attachedClasses.Count > 0 ? string.Join(", ", attachedClasses) : "none")})");
                }
            }

            ClassIntegration.LogClassCounts();

            ModSets.Rebuild();
            CodexIntegration.MarkModCardsDiscovered(source);

            // Consolidated boot conflict report (SPEC.md §3.5) + optional
            // diagnostics dump — emitted after every injection pass, printed
            // only when its content changed.
            Core.Status.BootReport.Emit(source);

            // Opportunistic ref resolution: shipped cards and same-batch mod cards
            // are all registered by now, and if statuses happen to be loaded too
            // (re-inject passes; the async boot path sets both flags after all
            // loading), everything can resolve immediately. Never a final pass
            // here — statuses legitimately aren't loaded yet on the sync boot path.
            ReferenceResolver.Resolve(source + "/phase1", finalPass: false);
        }

        private static void InjectCard(CardRegistration r, ref int injected, ref int skipped, ref int alreadyPresent)
        {
            ParsedCard spec = r.Spec;
            try
            {
                // Idempotency: injected by us earlier this process and still registered.
                if (r.Card != null)
                {
                    alreadyPresent++;
                    return;
                }
                string conflict = FindPoolConflict(spec, "card");
                if (conflict != null)
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] {conflict} — card skipped.");
                    RegistrationLedger.RecordConflict(spec.Owner, conflict);
                    skipped++;
                    return;
                }
                CardFactory.Build(r);
                CardFactory.RegisterInPools(r.Card, isWeapon: false);
                injected++;
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {spec.Owner}/{spec.Name}: unexpected error, card skipped: {ex}");
                skipped++;
            }
        }

        private static void InjectWeapon(CardRegistration r, ref int weaponsInjected)
        {
            ParsedCard spec = r.Spec;
            try
            {
                if (r.Card != null)
                {
                    return; // already injected this process and still registered
                }
                string conflict = FindPoolConflict(spec, "weapon");
                if (conflict != null)
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] {conflict} — weapon skipped.");
                    RegistrationLedger.RecordConflict(spec.Owner, conflict);
                    return;
                }
                CardFactory.Build(r);
                CardFactory.RegisterInPools(r.Card, isWeapon: true);
                weaponsInjected++;
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {spec.Owner}/{spec.Name}: unexpected error, weapon skipped: {ex}");
            }
        }

        private static void InjectStartingCard(CardRegistration r, ref int startingCardsInjected)
        {
            ParsedCard spec = r.Spec;
            try
            {
                if (r.Card != null)
                {
                    return; // already injected this process and still registered
                }
                string conflict = FindPoolConflict(spec, "starting card");
                if (conflict != null)
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] {conflict} — starting card skipped.");
                    RegistrationLedger.RecordConflict(spec.Owner, conflict);
                    return;
                }
                CardFactory.Build(r);
                // Normal pool routing (playercards-eligible): starting cards are
                // ordinary reward-pool cards per the shipped corpus (WEAPON-SPEC §1).
                CardFactory.RegisterInPools(r.Card, isWeapon: false);
                startingCardsInjected++;
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {spec.Owner}/{spec.Name}: unexpected error, starting card skipped: {ex}");
            }
        }

        private static void InjectPower(TalentRegistration r, ref int powersInjected)
        {
            ParsedTalent spec = r.Spec;
            try
            {
                if (r.Talent != null)
                {
                    return; // already injected this process and still registered
                }
                Talent existingTalent = AssetManager.allTalents.FirstOrDefault(t => t != null &&
                    (t.ID == spec.TalentId || string.Equals(t.name, spec.Name, StringComparison.OrdinalIgnoreCase)));
                if (existingTalent != null)
                {
                    string claimant = ClaimantOfTalent(existingTalent);
                    string conflict = existingTalent.ID == spec.TalentId
                        ? $"{spec.Owner}/{spec.Name}: talentID {spec.TalentId} already owned by {claimant} (talent \"{existingTalent.name}\")"
                        : $"{spec.Owner}/{spec.Name}: talent name already owned by {claimant} (talentID {existingTalent.ID})";
                    DawnKitPlugin.Log.LogError($"[DawnKit] {conflict} — weapon power skipped.");
                    RegistrationLedger.RecordConflict(spec.Owner, conflict);
                    return;
                }
                TalentFactory.Build(r);
                AssetManager.allTalents.Add(r.Talent);
                powersInjected++;
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] {spec.Owner}/{spec.Name}: unexpected error, weapon power skipped: {ex}");
            }
        }

        /// <summary>
        /// Live-pool collision check at injection time — the backstop behind the
        /// Register()-time ledger check, and the only place shipped-pool
        /// collisions can be seen on the boot path (pools are empty during
        /// Awake). The conflict message names BOTH claimants (SPEC.md §3.5).
        /// </summary>
        private static string FindPoolConflict(ParsedCard spec, string kind)
        {
            Card existing = AssetManager.allCards.FirstOrDefault(c => c != null &&
                                (c.cardID == spec.CardId || string.Equals(c.name, spec.Name, StringComparison.OrdinalIgnoreCase)))
                            ?? AssetManager.metacards.FirstOrDefault(c => c != null &&
                                (c.cardID == spec.CardId || string.Equals(c.name, spec.Name, StringComparison.OrdinalIgnoreCase)));
            if (existing == null)
            {
                return null;
            }
            string claimant = ClaimantOfCard(existing);
            return existing.cardID == spec.CardId
                ? $"{spec.Owner}/{spec.Name}: cardID {spec.CardId} already owned by {claimant} (card \"{existing.name}\")"
                : $"{spec.Owner}/{spec.Name}: {kind} name already owned by {claimant} (cardID {existing.cardID})";
        }

        /// <summary>The owner of a live pool card: a registered mod, or the shipped pool.</summary>
        private static string ClaimantOfCard(Card card)
        {
            CardRegistration ours = Registry.Cards.FirstOrDefault(r => r.Card == card);
            return ours != null ? ours.Spec.Owner : "the shipped card pool";
        }

        private static string ClaimantOfTalent(Talent talent)
        {
            TalentRegistration ours = Registry.Talents.FirstOrDefault(r => r.Talent == talent);
            return ours != null ? ours.Spec.Owner : "the shipped talent pool";
        }
    }
}
