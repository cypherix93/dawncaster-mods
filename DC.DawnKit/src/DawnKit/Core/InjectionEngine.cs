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
                int weaponsInjected = 0, powersInjected = 0;

                foreach (CardRegistration r in Registry.Cards)
                {
                    if (r.Spec.Owner == owner && !r.Spec.IsWeapon)
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
                foreach (TalentRegistration r in Registry.Talents)
                {
                    if (r.Spec.Owner == owner)
                    {
                        InjectPower(r, ref powersInjected);
                    }
                }

                if (injected + weaponsInjected + powersInjected > 0)
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
                bool hasWeaponContent =
                    Registry.Cards.Any(r => r.Spec.Owner == owner && r.Spec.IsWeapon) ||
                    Registry.Talents.Any(r => r.Spec.Owner == owner) ||
                    RegistrationLedger.FailedCount(owner, "weapon") > 0 ||
                    RegistrationLedger.FailedCount(owner, "weaponPower") > 0;

                string presentNote = alreadyPresent > 0 ? $", {alreadyPresent} already present" : "";
                DawnKitPlugin.Log.LogInfo($"[DawnKit] {owner}: {injected} cards injected, {skipped} skipped{presentNote} (hook: {source})");
                if (hasWeaponContent)
                {
                    DawnKitPlugin.Log.LogInfo($"[DawnKit] {owner}: {weaponsInjected} weapons, {powersInjected} weapon powers injected (classes: {(attachedClasses.Count > 0 ? string.Join(", ", attachedClasses) : "none")})");
                }
            }

            ClassIntegration.LogClassCounts();

            ModSets.Rebuild();
            CodexIntegration.MarkModCardsDiscovered(source);

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
                if (CardIdCollides(spec.CardId))
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] {spec.Owner}/{spec.Name}: cardID {spec.CardId} collides with an existing card — skipped.");
                    skipped++;
                    return;
                }
                if (CardNameCollides(spec.Name))
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] {spec.Owner}/{spec.Name}: card name collides with an existing card — skipped.");
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
                if (CardIdCollides(spec.CardId))
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] {spec.Owner}/{spec.Name}: weapon cardID {spec.CardId} collides with an existing card — skipped.");
                    return;
                }
                if (CardNameCollides(spec.Name))
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] {spec.Owner}/{spec.Name}: weapon name collides with an existing card — skipped.");
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

        private static void InjectPower(TalentRegistration r, ref int powersInjected)
        {
            ParsedTalent spec = r.Spec;
            try
            {
                if (r.Talent != null)
                {
                    return; // already injected this process and still registered
                }
                if (AssetManager.allTalents.Any(t => t != null && t.ID == spec.TalentId))
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] {spec.Owner}/{spec.Name}: talentID {spec.TalentId} collides with an existing talent — skipped.");
                    return;
                }
                if (AssetManager.allTalents.Any(t => t != null && string.Equals(t.name, spec.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    DawnKitPlugin.Log.LogError($"[DawnKit] {spec.Owner}/{spec.Name}: talent name collides with an existing talent — skipped.");
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

        private static bool CardIdCollides(int cardId)
        {
            return AssetManager.allCards.Any(c => c != null && c.cardID == cardId) ||
                   AssetManager.metacards.Any(c => c != null && c.cardID == cardId);
        }

        private static bool CardNameCollides(string name)
        {
            return AssetManager.allCards.Any(c => c != null && string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)) ||
                   AssetManager.metacards.Any(c => c != null && string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
