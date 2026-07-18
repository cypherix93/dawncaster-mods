using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Newtonsoft.Json;
using UnityEngine;

namespace Dawncaster.Sandbox
{
    /// <summary>
    /// Loads card packs (PacksPath/&lt;Pack&gt;/pack.json) into AssetManager at runtime.
    ///
    /// Two-phase load, matching the game's own boot order (LoadPlayerAssets then
    /// LoadWorldAssets):
    ///   Phase 1 (player assets loaded) — construct Card ScriptableObjects from the
    ///     manifests and register them in allCards/playercards. referenceStatus is
    ///     NOT resolved here: statuses only exist after CreateStatusCollections()
    ///     in the world phase, and calling AssetManager.GetStatus early would hit
    ///     its lazy LoadAllAssets() fallback and reenter the loading pipeline.
    ///     Card refs are attempted opportunistically (shipped + same-batch mod
    ///     cards are already present in the player phase).
    ///   Phase 2 (world assets loaded) — resolve remaining referenceStatus /
    ///     referenceCard entries by name and log every ref still unresolved.
    ///
    /// Re-injection: ForceReloadAssets() clears every AssetManager list on a game
    /// version change; the hooks re-fire on the next load pass, tracked entries
    /// whose Card instance is no longer registered are pruned, and the cards are
    /// rebuilt from the manifests (idempotent by cardID/name check).
    /// </summary>
    internal static class PackLoader
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("PackLoader");

        /// <summary>Manifest problem that invalidates a single card (card is skipped, load continues).</summary>
        private sealed class ManifestError : Exception
        {
            public ManifestError(string message) : base(message) { }
        }

        /// <summary>Pairs a constructed CardEffect with its manifest so refs can be resolved later.</summary>
        private sealed class EffectBinding
        {
            public CardEffect Effect;
            public EffectManifest Manifest;
        }

        private sealed class InjectedCard
        {
            public string PackName;
            public CardManifest Manifest;
            public Card Card;
            public readonly List<EffectBinding> Effects = new List<EffectBinding>();
        }

        private static string packsPath;
        private static AssetManager.CardExpansions? expansionOverride;
        private static bool autoDiscoverModCards;
        private static HashSet<string> knownCommands; // null => runtime command validation unavailable
        private static readonly List<InjectedCard> tracked = new List<InjectedCard>();

        /// <summary>
        /// One entry per loaded pack whose cards carry a synthetic CardExpansions
        /// value (see PackSetInfo docs for the formula). Consumed by
        /// SetScreenPatches to build set rows / codex filters. Rebuilt on every
        /// injection pass so re-injection after ForceReloadAssets stays consistent.
        /// </summary>
        internal static readonly List<PackSetInfo> PackSets = new List<PackSetInfo>();

        internal static bool IsModExpansion(AssetManager.CardExpansions e) => (int)e >= 1000;

        internal static PackSetInfo FindSet(AssetManager.CardExpansions e)
        {
            foreach (PackSetInfo s in PackSets)
            {
                if (s.Expansion == e)
                {
                    return s;
                }
            }
            return null;
        }

        internal static void Configure(string configuredPacksPath, string configuredExpansionOverride, bool configuredAutoDiscover)
        {
            packsPath = configuredPacksPath;
            autoDiscoverModCards = configuredAutoDiscover;
            expansionOverride = null;
            if (!string.IsNullOrEmpty(configuredExpansionOverride))
            {
                if (Enum.TryParse<AssetManager.CardExpansions>(configuredExpansionOverride, true, out var exp) &&
                    Enum.IsDefined(typeof(AssetManager.CardExpansions), exp))
                {
                    expansionOverride = exp;
                }
                else
                {
                    Log.LogWarning($"[PackLoader] ExpansionOverride '{configuredExpansionOverride}' is not a CardExpansions member — manifest expansions will be used as-is.");
                }
            }
            knownCommands = LoadKnownCommands();
            Log.LogInfo($"[PackLoader] Configured. PacksPath={packsPath}, ExpansionOverride={(expansionOverride.HasValue ? expansionOverride.Value.ToString() : "(none — per-pack synthetic sets)")}, AutoDiscoverModCards={autoDiscoverModCards}, command vocabulary: {(knownCommands != null ? knownCommands.Count.ToString() : "unavailable")}.");
        }

        private static HashSet<string> LoadKnownCommands()
        {
            try
            {
                // reference/effect-commands.txt is embedded into the DLL at build time.
                using (var stream = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("Dawncaster.Sandbox.effect-commands.txt"))
                {
                    if (stream == null)
                    {
                        Log.LogWarning("[PackLoader] Embedded effect-commands.txt not found; codeLine command validation disabled.");
                        return null;
                    }
                    using (var reader = new StreamReader(stream))
                    {
                        var set = new HashSet<string>(StringComparer.Ordinal);
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (line.Length > 0)
                            {
                                set.Add(line);
                            }
                        }
                        return set;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[PackLoader] Failed to load command vocabulary: {ex.Message}; codeLine command validation disabled.");
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Phase 1 — construct and register cards (player-asset hooks)
        // ------------------------------------------------------------------

        internal static void InjectPacks(string source)
        {
            try
            {
                InjectPacksInner(source);
            }
            catch (Exception ex)
            {
                Log.LogError($"[PackLoader] Pack injection failed (hook: {source}): {ex}");
            }
        }

        private static void InjectPacksInner(string source)
        {
            if (string.IsNullOrEmpty(packsPath) || !Directory.Exists(packsPath))
            {
                Log.LogWarning($"[PackLoader] Packs path not found: '{packsPath}' — nothing to load (hook: {source}).");
                return;
            }

            // Prune entries wiped by ForceReloadAssets()/ClearAllCollections() so
            // their cards get rebuilt this pass.
            tracked.RemoveAll(t => t.Card == null ||
                                   (!AssetManager.allCards.Contains(t.Card) &&
                                    !AssetManager.metacards.Contains(t.Card)));

            foreach (string packDir in Directory.GetDirectories(packsPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string manifestFile = Path.Combine(packDir, "pack.json");
                if (!File.Exists(manifestFile))
                {
                    continue;
                }

                PackManifest pm;
                try
                {
                    pm = JsonConvert.DeserializeObject<PackManifest>(File.ReadAllText(manifestFile));
                }
                catch (Exception ex)
                {
                    Log.LogError($"[PackLoader] Failed to parse {manifestFile}: {ex.Message} — pack skipped.");
                    continue;
                }

                if (pm?.cards == null || pm.cards.Count == 0)
                {
                    Log.LogError($"[PackLoader] {manifestFile}: no cards in manifest — pack skipped.");
                    continue;
                }

                string packName = string.IsNullOrEmpty(pm.pack) ? Path.GetFileName(packDir) : pm.pack;
                AssetManager.CardExpansions? syntheticSet = ComputeSyntheticSet(pm, packName);
                int injected = 0, skipped = 0, alreadyPresent = 0;

                foreach (CardManifest cm in pm.cards)
                {
                    try
                    {
                        if (cm == null)
                        {
                            Log.LogError($"[PackLoader] {packName}: null card entry — skipped.");
                            skipped++;
                            continue;
                        }

                        // Idempotency: injected by us earlier this process and still registered.
                        if (tracked.Any(t => t.Manifest.cardID == cm.cardID))
                        {
                            alreadyPresent++;
                            continue;
                        }

                        // Collision with a foreign (shipped or other-mod) card.
                        if (AssetManager.allCards.Any(c => c != null && c.cardID == cm.cardID) ||
                            AssetManager.metacards.Any(c => c != null && c.cardID == cm.cardID))
                        {
                            Log.LogError($"[PackLoader] {packName}/{cm.name}: cardID {cm.cardID} collides with an existing card — skipped.");
                            skipped++;
                            continue;
                        }
                        if (AssetManager.allCards.Any(c => c != null && string.Equals(c.name, cm.name, StringComparison.OrdinalIgnoreCase)) ||
                            AssetManager.metacards.Any(c => c != null && string.Equals(c.name, cm.name, StringComparison.OrdinalIgnoreCase)))
                        {
                            Log.LogError($"[PackLoader] {packName}/{cm.name}: card name collides with an existing card — skipped.");
                            skipped++;
                            continue;
                        }

                        InjectedCard ic = BuildCard(packName, packDir, cm, syntheticSet);
                        RegisterCard(ic.Card);
                        tracked.Add(ic);
                        injected++;
                    }
                    catch (ManifestError me)
                    {
                        Log.LogError($"[PackLoader] {packName}/{cm?.name ?? "?"}: {me.Message} — card skipped.");
                        skipped++;
                    }
                    catch (Exception ex)
                    {
                        Log.LogError($"[PackLoader] {packName}/{cm?.name ?? "?"}: unexpected error, card skipped: {ex}");
                        skipped++;
                    }
                }

                if (injected > 0)
                {
                    AssetManager.RefreshCaches();
                    if (PlayerHandler.thePlayerData != null)
                    {
                        AssetManager.CreateRunLists();
                    }
                }

                string presentNote = alreadyPresent > 0 ? $", {alreadyPresent} already present" : "";
                Log.LogInfo($"[PackLoader] {packName}: {injected} cards injected, {skipped} skipped{presentNote} (hook: {source})");
            }

            RebuildPackSets();
            MarkModCardsDiscovered(source);

            // Opportunistic ref resolution: shipped cards and same-batch mod cards
            // are all registered by now, and if statuses happen to be loaded too
            // (re-inject passes), everything can resolve immediately. Never a
            // final pass here — statuses legitimately aren't loaded yet on boot.
            ResolveReferences(source + "/phase1", finalPass: false);
        }

        /// <summary>
        /// Synthetic per-pack card set: 1000 + (idBlock.start − 700,000,000) / 100
        /// (CARD-PACK-SPEC.md §3 amendment). Returns null (with a warning) when the
        /// pack has no usable idBlock, in which case the manifest expansion is used
        /// and the pack gets no set row.
        /// </summary>
        private static AssetManager.CardExpansions? ComputeSyntheticSet(PackManifest pm, string packName)
        {
            if (expansionOverride.HasValue)
            {
                return null; // explicit override wins; no synthetic sets at all
            }
            if (pm.idBlock == null || pm.idBlock.Count < 1)
            {
                Log.LogWarning($"[PackLoader] {packName}: manifest has no idBlock — no synthetic card set, manifest expansion used as-is.");
                return null;
            }
            long start = pm.idBlock[0];
            if (start < 700000000L || start > 799999999L || start % 100 != 0)
            {
                Log.LogWarning($"[PackLoader] {packName}: idBlock start {start} is outside the mod range 700,000,000–799,999,999 or not block-aligned — no synthetic card set.");
                return null;
            }
            var value = (AssetManager.CardExpansions)(1000 + (int)((start - 700000000L) / 100L));
            return value;
        }

        /// <summary>
        /// Rebuild the PackSets registry from the tracked cards (grouped by pack,
        /// synthetic expansions only). Idempotent; runs after every injection pass.
        /// </summary>
        private static void RebuildPackSets()
        {
            PackSets.Clear();
            foreach (InjectedCard t in tracked)
            {
                if (t.Card == null || !IsModExpansion(t.Card.cardexpansion))
                {
                    continue;
                }
                PackSetInfo set = FindSet(t.Card.cardexpansion);
                if (set == null)
                {
                    set = new PackSetInfo { DisplayName = t.PackName, Expansion = t.Card.cardexpansion };
                    PackSets.Add(set);
                }
                set.Cards.Add(t.Card);
            }
            PackSets.Sort((a, b) => ((int)a.Expansion).CompareTo((int)b.Expansion));
            if (PackSets.Count > 0)
            {
                Log.LogInfo("[PackLoader] Synthetic card sets: " +
                    string.Join(", ", PackSets.Select(s => $"{s.DisplayName}=(CardExpansions){(int)s.Expansion} [{s.Cards.Count} cards]")));
            }
        }

        /// <summary>
        /// Codex auto-discovery: mod cards render as discovered when their IDs are
        /// in CodexHandler.codex.cardList. Adds them in memory only — we never call
        /// SaveCodex ourselves (the game persists the list on its own saves; stale
        /// mod IDs in Codex.dtt are harmless to the base game, whose checks are
        /// pure Contains() and whose cleanup skips unknown IDs).
        /// </summary>
        internal static void MarkModCardsDiscovered(string source)
        {
            try
            {
                if (!autoDiscoverModCards || !CodexHandler.codexLoaded ||
                    CodexHandler.codex == null || CodexHandler.codex.cardList == null)
                {
                    return;
                }
                int added = 0;
                foreach (InjectedCard t in tracked)
                {
                    if (t.Card != null && !CodexHandler.codex.cardList.Contains(t.Card.cardID))
                    {
                        CodexHandler.codex.cardList.Add(t.Card.cardID);
                        added++;
                    }
                }
                if (added > 0)
                {
                    Log.LogInfo($"[PackLoader] Codex: marked {added} mod cards as discovered (in-memory; hook: {source})");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[PackLoader] Codex auto-discovery failed (hook: {source}): {ex}");
            }
        }

        // ------------------------------------------------------------------
        // Phase 2 — resolve references (world-asset hooks)
        // ------------------------------------------------------------------

        internal static void ResolveReferences(string source, bool finalPass)
        {
            try
            {
                ResolveReferencesInner(source, finalPass);
            }
            catch (Exception ex)
            {
                Log.LogError($"[PackLoader] Reference resolution failed (hook: {source}): {ex}");
            }
        }

        private static void ResolveReferencesInner(string source, bool finalPass)
        {
            // Guard: never trigger AssetManager.GetStatus's lazy LoadAllAssets()
            // fallback — only resolve statuses once the world phase populated them.
            bool statusesAvailable = AssetManager.allStatusEffects.Count > 0;
            bool cardsAvailable = AssetManager.allCards.Count > 0;
            int resolved = 0;
            var unresolved = new List<string>();

            foreach (InjectedCard t in tracked)
            {
                foreach (EffectBinding b in t.Effects)
                {
                    // referenceStatus by asset name.
                    if (b.Effect.referenceStatus == null && !string.IsNullOrEmpty(b.Manifest.referenceStatus))
                    {
                        StatusEffect status = statusesAvailable ? AssetManager.GetStatus(b.Manifest.referenceStatus) : null;
                        if (status != null)
                        {
                            b.Effect.referenceStatus = status;
                            resolved++;
                        }
                        else if (finalPass)
                        {
                            unresolved.Add($"referenceStatus '{b.Manifest.referenceStatus}' on {t.PackName}/{t.Card.name}");
                        }
                    }

                    // referenceCards by asset name (index-aligned with the manifest list).
                    List<string> names = b.Manifest.referenceCards;
                    if (names == null)
                    {
                        continue;
                    }
                    for (int i = 0; i < names.Count; i++)
                    {
                        if (b.Effect.referenceCard[i] != null || string.IsNullOrEmpty(names[i]))
                        {
                            continue;
                        }
                        Card refCard = cardsAvailable ? AssetManager.GetCard(names[i]) : null;
                        if (refCard != null)
                        {
                            b.Effect.referenceCard[i] = refCard;
                            resolved++;
                        }
                        else if (finalPass)
                        {
                            unresolved.Add($"referenceCard '{names[i]}' on {t.PackName}/{t.Card.name}");
                        }
                    }
                }
            }

            if (resolved > 0 || finalPass)
            {
                Log.LogInfo($"[PackLoader] Reference resolution: {resolved} resolved, {unresolved.Count} unresolved (hook: {source})");
            }
            foreach (string u in unresolved)
            {
                Log.LogError($"[PackLoader] Unresolved reference: {u}");
            }
        }

        // ------------------------------------------------------------------
        // Manifest → Card construction
        // ------------------------------------------------------------------

        private static InjectedCard BuildCard(string packName, string packDir, CardManifest m,
            AssetManager.CardExpansions? syntheticSet)
        {
            if (string.IsNullOrEmpty(m.name))
            {
                throw new ManifestError("card has no name");
            }
            if (m.cardID == 0)
            {
                throw new ManifestError("cardID is missing or 0");
            }

            var ic = new InjectedCard { PackName = packName, Manifest = m };

            Card card = ScriptableObject.CreateInstance<Card>();
            card.name = m.name;
            card.hideFlags = HideFlags.HideAndDontSave; // survive scene loads, hide from Unity teardown
            card.cardID = m.cardID;

            // Precedence: explicit config override > per-pack synthetic set > manifest value.
            card.cardexpansion = expansionOverride
                ?? syntheticSet
                ?? ParseEnum<AssetManager.CardExpansions>(m.expansion, "expansion", m.name);
            card.cardType = ParseEnum<Card.CardType>(m.type, "type", m.name);
            card.cardCategory = ParseEnum<Card.CardCategory>(m.category, "category", m.name);
            card.cardSuffix = ParseEnum<Card.Suffix>(m.suffix, "suffix", m.name);
            card.cardRarity = ParseEnum<Card.CardRariry>(m.rarity, "rarity", m.name);

            ApplyCosts(card, m.cost);
            card.cardDescription = m.description ?? "";
            card.utilityNumber = m.utilityNumber ?? "";
            card.charges = m.charges;

            card.keywords = new List<Card.CardProperties>();
            if (m.keywords != null)
            {
                foreach (string kw in m.keywords)
                {
                    card.keywords.Add(ParseEnum<Card.CardProperties>(kw, "keyword", m.name));
                }
            }
            card.cardKeywords = m.cardKeywords != null ? new List<string>(m.cardKeywords) : new List<string>();

            if (m.flags != null)
            {
                foreach (string flag in m.flags)
                {
                    ApplyFlag(card, flag);
                }
            }

            card.playConditions = BuildConditions(m.playConditions, m.name);
            card.CardEffectList = new List<CardEffect>();
            if (m.effects != null)
            {
                foreach (EffectManifest fx in m.effects)
                {
                    card.CardEffectList.Add(BuildEffect(fx, m.name, ic));
                }
            }

            // Always a non-null Enchantment payload (Codex display code dereferences it).
            if (m.enchantment == null)
            {
                card.CardEnchantments = new Enchantment
                {
                    enchantmentText = "",
                    CardEffectList = new List<CardEffect>()
                };
            }
            else
            {
                var ench = new Enchantment
                {
                    theType = ParseEnum<LastingEffect.EffectType>(m.enchantment.type, "enchantment type", m.name),
                    enchantmentText = m.enchantment.text ?? "",
                    combatEnchantment = m.enchantment.combat,
                    showstacks = m.enchantment.showstacks,
                    CardEffectList = new List<CardEffect>()
                };
                if (m.enchantment.effects != null)
                {
                    foreach (EffectManifest fx in m.enchantment.effects)
                    {
                        ench.CardEffectList.Add(BuildEffect(fx, m.name, ic));
                    }
                }
                card.CardEnchantments = ench;
            }

            // Art: prefer packs/<Pack>/art/<CardName>.png when it exists, else a
            // generated placeholder colored by the card's cost-color identity.
            // audioClip stays null — every game usage site null-checks it
            // (CodexUI.cs:1229, CombatUIHandler.cs:1224, SpellEffects.cs:260).
            card.artwork = LoadArt(packDir, m, card);

            ic.Card = card;
            return ic;
        }

        private static void RegisterCard(Card card)
        {
            // Mirrors AssetManager.ProcessCard's routing rules.
            if (card.cardexpansion == AssetManager.CardExpansions.Metaprogress)
            {
                AssetManager.metacards.Add(card);
                return;
            }
            AssetManager.allCards.Add(card);
            if (card.cardexpansion != AssetManager.CardExpansions.None &&
                card.cardRarity != Card.CardRariry.Monster &&
                card.cardSuffix != Card.Suffix.Companion)
            {
                AssetManager.playercards.Add(card);
            }
        }

        private static CardEffect BuildEffect(EffectManifest m, string cardName, InjectedCard owner)
        {
            if (m == null)
            {
                throw new ManifestError("null effect entry");
            }
            ValidateCodeLine(m.codeLine, cardName);
            var fx = new CardEffect
            {
                cardTrigger = ParseEnum<EventHandler.GameTriggers>(m.trigger, "trigger", cardName),
                codeLine = m.codeLine ?? "",
                forecast = m.forecast,
                hideReferenceCards = m.hideReferenceCards,
                referenceCard = new Card[m.referenceCards?.Count ?? 0],
                effectConditions = BuildConditions(m.conditions, cardName)
            };
            owner.Effects.Add(new EffectBinding { Effect = fx, Manifest = m });
            return fx;
        }

        private static List<Condition> BuildConditions(List<ConditionManifest> list, string cardName)
        {
            var result = new List<Condition>();
            if (list == null)
            {
                return result;
            }
            foreach (ConditionManifest c in list)
            {
                if (c == null)
                {
                    throw new ManifestError("null condition entry");
                }
                result.Add(new Condition
                {
                    valueToCheck = ParseEnum<ConditionChecker.ConditionValue>(c.value, "condition value", cardName),
                    conditonOperator = ParseEnum<ConditionChecker.ConditionOperator>(c.op, "condition op", cardName),
                    targetValue = c.target ?? "",
                    ignoreForDisplay = c.ignoreForDisplay
                });
            }
            return result;
        }

        private static void ValidateCodeLine(string codeLine, string cardName)
        {
            if (knownCommands == null || string.IsNullOrEmpty(codeLine))
            {
                return;
            }
            foreach (string statement in codeLine.Split(';'))
            {
                string s = statement.Trim();
                if (s.Length == 0)
                {
                    continue;
                }
                string command = s.Split(':')[0].Trim();
                if (command.Length > 0 && !knownCommands.Contains(command))
                {
                    throw new ManifestError($"unknown effect command '{command}' in codeLine '{codeLine}'");
                }
            }
        }

        private static void ApplyCosts(Card card, Dictionary<string, int> cost)
        {
            if (cost == null)
            {
                return;
            }
            foreach (KeyValuePair<string, int> kv in cost)
            {
                switch (kv.Key)
                {
                    case "DEX": card.costDEX = kv.Value; break;
                    case "INT": card.costINT = kv.Value; break;
                    case "STR": card.costSTR = kv.Value; break;
                    case "HOLY": card.costHOLY = kv.Value; break;
                    case "Neutral": card.costNeutral = kv.Value; break;
                    case "DEXINT": card.costDEXINT = kv.Value; break;
                    case "DEXSTR": card.costDEXSTR = kv.Value; break;
                    case "INTSTR": card.costINTSTR = kv.Value; break;
                    case "Life": card.costLife = kv.Value; break;
                    default: throw new ManifestError($"unknown cost key '{kv.Key}'");
                }
            }
        }

        private static void ApplyFlag(Card card, string flag)
        {
            switch (flag)
            {
                case "uniqueInHand": card.uniqueInHand = true; break;
                case "canBeAcquired": card.canBeAcquired = true; break;
                case "hideConditionGlow": card.hideConditionGlow = true; break;
                case "resetTempValues": card.resetTempValues = true; break;
                case "cullLastWordFromName": card.cullLastWordFromName = true; break;
                case "overwriteUpgradable": card.overwriteUpgradable = true; break;
                case "cantbeupgraded": card.cantbeupgraded = true; break;
                case "requireAllConditions": card.requireAllConditions = true; break;
                case "pauseQueue": card.pauseQueue = true; break;
                case "excludeFromConjurations": card.excludeFromConjurations = true; break;
                case "excludeFromSunforge": card.excludeFromSunforge = true; break;
                case "excludeFromRewards": card.excludeFromRewards = true; break;
                case "excludeFromCodex": card.excludeFromCodex = true; break;
                default: throw new ManifestError($"unknown flag '{flag}'");
            }
        }

        private static T ParseEnum<T>(string raw, string field, string cardName) where T : struct
        {
            if (string.IsNullOrEmpty(raw))
            {
                throw new ManifestError($"missing {field}");
            }
            if (Enum.TryParse<T>(raw, ignoreCase: false, out T value) && Enum.IsDefined(typeof(T), value))
            {
                return value;
            }
            if (Enum.TryParse<T>(raw, ignoreCase: true, out value) && Enum.IsDefined(typeof(T), value))
            {
                Log.LogWarning($"[PackLoader] {cardName}: {field} '{raw}' only matched {typeof(T).Name}.{value} case-insensitively — fix the manifest spelling.");
                return value;
            }
            throw new ManifestError($"unknown {typeof(T).Name} member '{raw}' for {field}");
        }

        // ------------------------------------------------------------------
        // Art
        // ------------------------------------------------------------------

        private static Sprite LoadArt(string packDir, CardManifest m, Card card)
        {
            // Prefer shipped PNG (art/<CardName>.png per ART-PIPELINE.md).
            if (!string.IsNullOrEmpty(m.art))
            {
                string pngPath = Path.Combine(packDir, m.art.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(pngPath))
                {
                    try
                    {
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                        tex.LoadImage(File.ReadAllBytes(pngPath)); // resizes to actual dimensions
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.hideFlags = HideFlags.HideAndDontSave;
                        var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f), 100f); // 100 PPU = Unity default
                        sprite.hideFlags = HideFlags.HideAndDontSave;
                        return sprite;
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"[PackLoader] {card.name}: failed to load art '{pngPath}' ({ex.Message}) — using placeholder.");
                    }
                }
            }
            return CreatePlaceholderArt(card);
        }

        /// <summary>
        /// 512×512 two-band placeholder: flat color from the card's cost-color
        /// identity (Card.GetColor(), which reads only cost fields/colorCard),
        /// with a slightly darker lower half so it is obviously placeholder art.
        /// </summary>
        private static Sprite CreatePlaceholderArt(Card card)
        {
            Color32 baseColor = PlaceholderColor(card.GetColor());
            var darker = new Color32(
                (byte)(baseColor.r * 0.65f),
                (byte)(baseColor.g * 0.65f),
                (byte)(baseColor.b * 0.65f),
                255);

            const int size = 512;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                Color32 c = y < size / 2 ? darker : baseColor; // row 0 = bottom in Unity
                int row = y * size;
                for (int x = 0; x < size; x++)
                {
                    pixels[row + x] = c;
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true); // free the CPU copy
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Color32 PlaceholderColor(Card.ColorOverwrite color)
        {
            switch (color)
            {
                case Card.ColorOverwrite.Green: return new Color32(51, 140, 64, 255);   // DEX
                case Card.ColorOverwrite.Blue: return new Color32(51, 89, 178, 255);    // INT
                case Card.ColorOverwrite.Red: return new Color32(178, 51, 46, 255);     // STR
                case Card.ColorOverwrite.Gold: return new Color32(217, 178, 64, 255);   // HOLY
                case Card.ColorOverwrite.Aqua: return new Color32(51, 166, 166, 255);   // DEX+INT
                case Card.ColorOverwrite.Orange: return new Color32(217, 128, 38, 255); // DEX+STR
                case Card.ColorOverwrite.Purple: return new Color32(140, 77, 166, 255); // INT+STR
                case Card.ColorOverwrite.Black: return new Color32(38, 31, 41, 255);    // life/corruption
                case Card.ColorOverwrite.White: return new Color32(217, 217, 204, 255);
                case Card.ColorOverwrite.Monster: return new Color32(89, 89, 89, 255);
                case Card.ColorOverwrite.Brown:
                default: return new Color32(115, 89, 64, 255);                          // neutral
            }
        }
    }
}
