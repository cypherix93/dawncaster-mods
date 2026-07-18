using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Dawncaster.Sandbox
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SandboxPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.dawncastermods.sandbox";
        public const string PluginName = "Dawncaster Sandbox";
        public const string PluginVersion = "0.2.0";

        private const string CardName = "SandboxStrike";
        private const int CardId = 900001;

        internal static SandboxPlugin Instance;

        private ConfigEntry<string> packsPath;
        private ConfigEntry<string> expansionOverride;
        private ConfigEntry<bool> injectSandboxCard;

        private void Awake()
        {
            Instance = this;

            packsPath = Config.Bind("Packs", "PacksPath",
                Path.Combine(Paths.PluginPath, "DawncasterPacks"),
                "Directory scanned for <Pack>/pack.json card-pack manifests (CARD-PACK-SPEC.md schema).");
            expansionOverride = Config.Bind("Packs", "ExpansionOverride",
                "Core",
                "When non-empty, every loaded card's manifest expansion is overridden with this " +
                "AssetManager.CardExpansions member. Default 'Core' keeps mod cards in the run pool " +
                "even when all non-core sets are disabled in-game (CreateRunLists filters by " +
                "excludedsets, which would drop the manifests' 'Extended'). Empty = use manifest values.");
            injectSandboxCard = Config.Bind("Sandbox", "InjectSandboxCard",
                false,
                "Inject the SandboxStrike hello-world test card (id 900001).");

            PackLoader.Configure(packsPath.Value, expansionOverride.Value);

            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(AssetLoadHooks));
            Logger.LogInfo($"[Sandbox] {PluginName} {PluginVersion} loaded, hooks installed.");
        }

        [HarmonyPatch]
        internal static class AssetLoadHooks
        {
            // ---- Phase 1 (cards): player-asset load ----

            // Primary hook: GameLoader's async boot path calls
            // AssetManager.SetPlayerAssetsLoaded() after CreateCardCollectionsAsync.
            [HarmonyPostfix]
            [HarmonyPatch(typeof(AssetManager), nameof(AssetManager.SetPlayerAssetsLoaded))]
            private static void SetPlayerAssetsLoaded_Postfix()
            {
                Instance?.OnPlayerAssetsLoaded("SetPlayerAssetsLoaded");
            }

            // Safety net: the synchronous path (LoadAllAssets -> LoadPlayerAssets)
            // sets the playerAssetsLoaded flag directly without calling
            // SetPlayerAssetsLoaded(), so hook it too. Injection is idempotent.
            [HarmonyPostfix]
            [HarmonyPatch(typeof(AssetManager), nameof(AssetManager.LoadPlayerAssets))]
            private static void LoadPlayerAssets_Postfix()
            {
                Instance?.OnPlayerAssetsLoaded("LoadPlayerAssets");
            }

            // ---- Phase 2 (references): world-asset load ----
            // referenceStatus targets only exist after CreateStatusCollections()
            // runs in the world phase, which is always after the player phase
            // (LoadAllAssets: LoadPlayerAssets then LoadWorldAssets).

            [HarmonyPostfix]
            [HarmonyPatch(typeof(AssetManager), nameof(AssetManager.SetWorldAssetsLoaded))]
            private static void SetWorldAssetsLoaded_Postfix()
            {
                PackLoader.ResolveReferences("SetWorldAssetsLoaded", finalPass: true);
            }

            // Safety net, same reasoning as LoadPlayerAssets. Resolution is
            // idempotent (already-resolved refs are non-null and skipped).
            [HarmonyPostfix]
            [HarmonyPatch(typeof(AssetManager), nameof(AssetManager.LoadWorldAssets))]
            private static void LoadWorldAssets_Postfix()
            {
                PackLoader.ResolveReferences("LoadWorldAssets", finalPass: true);
            }
        }

        private void OnPlayerAssetsLoaded(string source)
        {
            PackLoader.InjectPacks(source);
            if (injectSandboxCard.Value)
            {
                InjectCards(source);
            }
        }

        private void InjectCards(string source)
        {
            try
            {
                // Idempotency guard: check the list directly (GetCard(name) logs a
                // warning when the card is missing, so avoid it here).
                if (AssetManager.allCards.Any(c => c != null && c.name == CardName))
                {
                    Logger.LogInfo($"[Sandbox] {CardName} already present (hook: {source}), skipping.");
                    return;
                }

                // cardID collision check.
                int id = CardId;
                while (AssetManager.allCards.Any(c => c != null && c.cardID == id))
                {
                    Logger.LogWarning($"[Sandbox] cardID {id} collides with an existing card, trying {id + 1}.");
                    id++;
                }

                Card card = ScriptableObject.CreateInstance<Card>();
                card.name = CardName;
                card.hideFlags = HideFlags.HideAndDontSave; // survive scene loads, hide from Unity teardown
                card.cardID = id;
                card.cardexpansion = AssetManager.CardExpansions.Core;
                card.cardType = Card.CardType.Melee;
                card.cardCategory = Card.CardCategory.Action;
                card.cardRarity = Card.CardRariry.Common; // enum name is typo'd in game code
                card.costSTR = 1;
                card.cardDescription = "Deal 6 damage. (Sandbox mod test)";

                // Initialize every list/ref field the game code touches — most are
                // not initialized inline on Card (only 'keywords' is).
                card.cardKeywords = new List<string>();
                card.playConditions = new List<Condition>();
                card.keywords = new List<Card.CardProperties>();
                card.CardEffectList = new List<CardEffect>
                {
                    new CardEffect
                    {
                        cardTrigger = EventHandler.GameTriggers.PlayAction,
                        codeLine = "damage:6",
                        forecast = true,
                        referenceCard = new Card[0],
                        effectConditions = new List<Condition>()
                    }
                };
                // Non-null Enchantment payload avoids NREs in Codex display code.
                card.CardEnchantments = new Enchantment
                {
                    enchantmentText = "",
                    CardEffectList = new List<CardEffect>()
                };

                AssetManager.allCards.Add(card);
                AssetManager.playercards.Add(card);
                AssetManager.RefreshCaches();
                if (PlayerHandler.thePlayerData != null)
                {
                    AssetManager.CreateRunLists();
                }

                Logger.LogInfo($"[Sandbox] Injected {CardName} (id {id}), allCards={AssetManager.allCards.Count} (hook: {source})");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Sandbox] Card injection failed (hook: {source}): {ex}");
            }
        }
    }
}
