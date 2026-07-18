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
    /// Manifest v1.1 (WEAPON-SPEC.md) additionally supports "weapons" (BasicAttack
    /// cards registered in allCards only + appended to Profession.weapons) and
    /// "weaponPowers" (tier-0 Talents registered in allTalents + appended to
    /// Profession.talents).
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
            public bool IsWeapon;              // WEAPON-SPEC §5 — registered in allCards only
            public List<string> Classes;       // weapon target Professions (manifest "classes")
            public readonly List<EffectBinding> Effects = new List<EffectBinding>();
        }

        private sealed class InjectedTalent
        {
            public string PackName;
            public WeaponPowerManifest Manifest;
            public Talent Talent;
            public readonly List<EffectBinding> Effects = new List<EffectBinding>();
        }

        private static string packsPath;
        private static AssetManager.CardExpansions? expansionOverride;
        private static bool autoDiscoverModCards;
        private static HashSet<string> knownCommands;  // effect DSL; null => runtime command validation unavailable
        private static HashSet<string> talentCommands; // effect DSL ∪ TalentHandler.RunTalentEffect switch labels
        private static readonly List<InjectedCard> tracked = new List<InjectedCard>();
        private static readonly List<InjectedTalent> trackedTalents = new List<InjectedTalent>();

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
            knownCommands = LoadCommandFile("effect-commands.txt");
            HashSet<string> talentOnly = LoadCommandFile("talent-commands.txt");
            if (knownCommands != null && talentOnly != null)
            {
                talentCommands = new HashSet<string>(knownCommands, StringComparer.Ordinal);
                talentCommands.UnionWith(talentOnly);
            }
            else
            {
                talentCommands = null; // half a vocabulary would misreport — disable instead
            }
            Log.LogInfo($"[PackLoader] Configured. PacksPath={packsPath}, ExpansionOverride={(expansionOverride.HasValue ? expansionOverride.Value.ToString() : "(none — per-pack synthetic sets)")}, AutoDiscoverModCards={autoDiscoverModCards}, command vocabulary: {(knownCommands != null ? knownCommands.Count.ToString() : "unavailable")} effect / {(talentCommands != null ? talentCommands.Count.ToString() : "unavailable")} talent-union.");
        }

        private static HashSet<string> LoadCommandFile(string fileName)
        {
            try
            {
                // reference/*.txt vocabularies are embedded into the DLL at build time.
                using (var stream = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("Dawncaster.Sandbox." + fileName))
                {
                    if (stream == null)
                    {
                        Log.LogWarning($"[PackLoader] Embedded {fileName} not found; codeLine command validation disabled.");
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
                Log.LogWarning($"[PackLoader] Failed to load command vocabulary {fileName}: {ex.Message}; codeLine command validation disabled.");
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
            // their cards/talents get rebuilt this pass.
            tracked.RemoveAll(t => t.Card == null ||
                                   (!AssetManager.allCards.Contains(t.Card) &&
                                    !AssetManager.metacards.Contains(t.Card)));
            trackedTalents.RemoveAll(t => t.Talent == null ||
                                          !AssetManager.allTalents.Contains(t.Talent));

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

                bool hasCards = pm?.cards != null && pm.cards.Count > 0;
                bool hasWeapons = pm?.weapons != null && pm.weapons.Count > 0;
                bool hasPowers = pm?.weaponPowers != null && pm.weaponPowers.Count > 0;
                if (!hasCards && !hasWeapons && !hasPowers)
                {
                    Log.LogError($"[PackLoader] {manifestFile}: no cards/weapons/weaponPowers in manifest — pack skipped.");
                    continue;
                }

                string packName = string.IsNullOrEmpty(pm.pack) ? Path.GetFileName(packDir) : pm.pack;
                AssetManager.CardExpansions? syntheticSet = ComputeSyntheticSet(pm, packName);
                int injected = 0, skipped = 0, alreadyPresent = 0;

                foreach (CardManifest cm in pm.cards ?? new List<CardManifest>())
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

                // ---- v1.1: weapons (Cards, allCards only) + weapon powers (tier-0 Talents) ----
                int weaponsInjected = InjectWeapons(pm, packName, packDir, syntheticSet);
                int powersInjected = InjectWeaponPowers(pm, packName, packDir, syntheticSet);

                if (injected + weaponsInjected + powersInjected > 0)
                {
                    AssetManager.RefreshCaches();
                    if (PlayerHandler.thePlayerData != null)
                    {
                        AssetManager.CreateRunLists();
                    }
                }

                // Class attachment runs every pass (idempotent by ID/name) so a
                // ForceReloadAssets that re-fetched Profession assets — or wiped and
                // rebuilt our cards — always converges on the live instances (§5.5).
                List<string> attachedClasses = AttachPackToClasses(packName);

                string presentNote = alreadyPresent > 0 ? $", {alreadyPresent} already present" : "";
                Log.LogInfo($"[PackLoader] {packName}: {injected} cards injected, {skipped} skipped{presentNote} (hook: {source})");
                if (hasWeapons || hasPowers)
                {
                    Log.LogInfo($"[PackLoader] {packName}: {weaponsInjected} weapons, {powersInjected} weapon powers injected (classes: {(attachedClasses.Count > 0 ? string.Join(", ", attachedClasses) : "none")})");
                }
            }

            LogClassCounts();

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
        // Weapons & weapon powers (WEAPON-SPEC.md §5)
        // ------------------------------------------------------------------

        /// <summary>
        /// Weapon cards: full card factory, category forced to BasicAttack,
        /// excludeFromRewards always true, registered in allCards ONLY (weapons
        /// enter play via character creation, never rewards — spec §5.2).
        /// </summary>
        private static int InjectWeapons(PackManifest pm, string packName, string packDir,
            AssetManager.CardExpansions? syntheticSet)
        {
            int injected = 0;
            if (pm.weapons == null)
            {
                return 0;
            }
            foreach (WeaponManifest wm in pm.weapons)
            {
                try
                {
                    if (wm == null)
                    {
                        Log.LogError($"[PackLoader] {packName}: null weapon entry — skipped.");
                        continue;
                    }
                    if (tracked.Any(t => t.Manifest.cardID == wm.cardID))
                    {
                        continue; // already injected this process and still registered
                    }
                    if (AssetManager.allCards.Any(c => c != null && c.cardID == wm.cardID) ||
                        AssetManager.metacards.Any(c => c != null && c.cardID == wm.cardID))
                    {
                        Log.LogError($"[PackLoader] {packName}/{wm.name}: weapon cardID {wm.cardID} collides with an existing card — skipped.");
                        continue;
                    }
                    if (AssetManager.allCards.Any(c => c != null && string.Equals(c.name, wm.name, StringComparison.OrdinalIgnoreCase)) ||
                        AssetManager.metacards.Any(c => c != null && string.Equals(c.name, wm.name, StringComparison.OrdinalIgnoreCase)))
                    {
                        Log.LogError($"[PackLoader] {packName}/{wm.name}: weapon name collides with an existing card — skipped.");
                        continue;
                    }

                    InjectedCard ic = BuildCard(packName, packDir, wm, syntheticSet);
                    if (ic.Card.cardCategory != Card.CardCategory.BasicAttack)
                    {
                        Log.LogError($"[PackLoader] {packName}/{wm.name}: weapon category is {ic.Card.cardCategory}, must be BasicAttack — skipped.");
                        UnityEngine.Object.Destroy(ic.Card);
                        continue;
                    }
                    ic.Card.excludeFromRewards = true; // forced default for weapons
                    ic.IsWeapon = true;
                    ic.Classes = wm.classes;
                    AssetManager.allCards.Add(ic.Card); // NOT playercards (spec §5.2)
                    tracked.Add(ic);
                    injected++;
                }
                catch (ManifestError me)
                {
                    Log.LogError($"[PackLoader] {packName}/{wm?.name ?? "?"}: {me.Message} — weapon skipped.");
                }
                catch (Exception ex)
                {
                    Log.LogError($"[PackLoader] {packName}/{wm?.name ?? "?"}: unexpected error, weapon skipped: {ex}");
                }
            }
            return injected;
        }

        /// <summary>
        /// Weapon powers: tier-0 Talent ScriptableObjects, registered in
        /// AssetManager.allTalents (RefreshCaches rebuilds the talent lookup caches
        /// afterwards). Class gating happens purely via Profession.talents
        /// membership — requiredTalents/requiredProfessions stay empty (spec §5.1).
        /// </summary>
        private static int InjectWeaponPowers(PackManifest pm, string packName, string packDir,
            AssetManager.CardExpansions? syntheticSet)
        {
            int injected = 0;
            if (pm.weaponPowers == null)
            {
                return 0;
            }
            foreach (WeaponPowerManifest wp in pm.weaponPowers)
            {
                try
                {
                    if (wp == null)
                    {
                        Log.LogError($"[PackLoader] {packName}: null weaponPower entry — skipped.");
                        continue;
                    }
                    if (trackedTalents.Any(t => t.Manifest.talentID == wp.talentID))
                    {
                        continue; // already injected this process and still registered
                    }
                    if (AssetManager.allTalents.Any(t => t != null && t.ID == wp.talentID))
                    {
                        Log.LogError($"[PackLoader] {packName}/{wp.name}: talentID {wp.talentID} collides with an existing talent — skipped.");
                        continue;
                    }
                    if (AssetManager.allTalents.Any(t => t != null && string.Equals(t.name, wp.name, StringComparison.OrdinalIgnoreCase)))
                    {
                        Log.LogError($"[PackLoader] {packName}/{wp.name}: talent name collides with an existing talent — skipped.");
                        continue;
                    }

                    InjectedTalent it = BuildTalent(packName, packDir, wp, syntheticSet);
                    AssetManager.allTalents.Add(it.Talent);
                    trackedTalents.Add(it);
                    injected++;
                }
                catch (ManifestError me)
                {
                    Log.LogError($"[PackLoader] {packName}/{wp?.name ?? "?"}: {me.Message} — weapon power skipped.");
                }
                catch (Exception ex)
                {
                    Log.LogError($"[PackLoader] {packName}/{wp?.name ?? "?"}: unexpected error, weapon power skipped: {ex}");
                }
            }
            return injected;
        }

        private static InjectedTalent BuildTalent(string packName, string packDir,
            WeaponPowerManifest m, AssetManager.CardExpansions? syntheticSet)
        {
            if (string.IsNullOrEmpty(m.name))
            {
                throw new ManifestError("weapon power has no name");
            }
            if (m.talentID == 0)
            {
                throw new ManifestError("talentID is missing or 0");
            }

            var it = new InjectedTalent { PackName = packName, Manifest = m };

            Talent t = ScriptableObject.CreateInstance<Talent>();
            t.name = m.name;
            t.hideFlags = HideFlags.HideAndDontSave;
            t.ID = m.talentID;
            t.tier = 0; // weapon powers ARE the tier-0 pool (CreateCharacterFunctions.GetRandomWeaponPower)
            t.expansion = expansionOverride ?? syntheticSet ?? AssetManager.CardExpansions.Core;
            t.description = m.description ?? "";
            t.flavortext = m.flavortext ?? "";
            t.cooldown = m.cooldown;
            t.keywords = m.keywords != null ? new List<string>(m.keywords) : new List<string>();
            t.unique = true;
            t.storyTalent = false;
            t.excludeFromRandom = false;
            t.excludeFromSunforge = false;
            t.excludeFromCodex = false;
            t.infernalOffering = false;
            t.requiredTalents = new List<Talent>();
            t.requiredProfessions = new List<Profession>(); // gating is Profession.talents membership, not requirements
            if (m.requirements != null)
            {
                t.rDEX = m.requirements.rDEX;
                t.rINT = m.requirements.rINT;
                t.rSTR = m.requirements.rSTR;
            }

            t.effectList = new List<CardEffect>();
            if (m.effects != null)
            {
                foreach (EffectManifest fx in m.effects)
                {
                    // Talent codeLines may use the full SpellEffects DSL plus the
                    // TalentHandler.RunTalentEffect extras (falls through at
                    // TalentHandler.cs:510) — validate against the union vocabulary.
                    t.effectList.Add(BuildEffect(fx, m.name, it.Effects, talentCommands));
                }
            }

            // powerImage: prefer packs/<Pack>/<art> (or art/<Name>.png), else the
            // two-band placeholder. audioClip stays null — the only Talent usage
            // site null-checks it (PlayerUIHandler.cs:1631).
            string artPath = string.IsNullOrEmpty(m.art) ? $"art/{m.name}.png" : m.art;
            t.powerImage = LoadSpriteFile(packDir, artPath, m.name)
                ?? CreatePlaceholderSprite(PlaceholderColor(Card.ColorOverwrite.White));

            it.Talent = t;
            return it;
        }

        // ---- class attachment (spec §5.3) ----

        /// <summary>
        /// Appends this pack's tracked weapons/talents to the live Profession
        /// assets in AssetManager.allClasses. Idempotent and stale-safe: keyed on
        /// cardID/talentID + name, replacing any previous (wiped) instance so the
        /// character-creation UI always sees the registered object. Returns the
        /// distinct class names that carry at least one attachment.
        /// </summary>
        private static List<string> AttachPackToClasses(string packName)
        {
            var attached = new List<string>();
            bool anyContent = tracked.Any(t => t.IsWeapon && t.PackName == packName) ||
                              trackedTalents.Any(t => t.PackName == packName);
            if (!anyContent)
            {
                return attached;
            }
            if (AssetManager.allClasses == null || AssetManager.allClasses.Count == 0)
            {
                Log.LogWarning($"[PackLoader] {packName}: AssetManager.allClasses is empty — class attachment deferred to the next load pass.");
                return attached;
            }

            foreach (InjectedCard t in tracked)
            {
                if (!t.IsWeapon || t.PackName != packName)
                {
                    continue;
                }
                foreach (Profession prof in ResolveClasses(packName, t.Card.name, t.Classes))
                {
                    if (prof.weapons == null)
                    {
                        prof.weapons = new List<Card>();
                    }
                    if (!prof.weapons.Contains(t.Card))
                    {
                        prof.weapons.RemoveAll(w => w != null &&
                            (w.cardID == t.Card.cardID ||
                             string.Equals(w.name, t.Card.name, StringComparison.OrdinalIgnoreCase)));
                        prof.weapons.Add(t.Card);
                    }
                    if (!attached.Contains(prof.name))
                    {
                        attached.Add(prof.name);
                    }
                }
            }

            foreach (InjectedTalent t in trackedTalents)
            {
                if (t.PackName != packName)
                {
                    continue;
                }
                foreach (Profession prof in ResolveClasses(packName, t.Talent.name, t.Manifest.classes))
                {
                    if (prof.talents == null)
                    {
                        prof.talents = new List<Talent>();
                    }
                    if (!prof.talents.Contains(t.Talent))
                    {
                        prof.talents.RemoveAll(x => x != null &&
                            (x.ID == t.Talent.ID ||
                             string.Equals(x.name, t.Talent.name, StringComparison.OrdinalIgnoreCase)));
                        prof.talents.Add(t.Talent);
                    }
                    if (!attached.Contains(prof.name))
                    {
                        attached.Add(prof.name);
                    }
                }
            }

            attached.Sort(StringComparer.OrdinalIgnoreCase);
            return attached;
        }

        /// <summary>Manifest class names → live Professions. "all" = every class;
        /// unknown names log an error and skip only that attachment (spec §5.3).</summary>
        private static List<Profession> ResolveClasses(string packName, string owner, List<string> classes)
        {
            var result = new List<Profession>();
            if (classes == null || classes.Count == 0)
            {
                Log.LogError($"[PackLoader] {packName}/{owner}: no target classes declared — not offered to any Profession.");
                return result;
            }
            foreach (string cls in classes)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                if (string.Equals(cls, "all", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (Profession p in AssetManager.allClasses)
                    {
                        if (p != null && !result.Contains(p))
                        {
                            result.Add(p);
                        }
                    }
                    continue;
                }
                Profession prof = AssetManager.allClasses.FirstOrDefault(p => p != null &&
                    string.Equals(p.name, cls, StringComparison.OrdinalIgnoreCase));
                if (prof == null)
                {
                    Log.LogError($"[PackLoader] {packName}/{owner}: unknown class '{cls}' — that attachment skipped.");
                    continue;
                }
                if (!result.Contains(prof))
                {
                    result.Add(prof);
                }
            }
            return result;
        }

        /// <summary>Debug dump: per-class weapon/talent list sizes after injection,
        /// so live verification can confirm Profession lists actually grew.</summary>
        private static void LogClassCounts()
        {
            if (AssetManager.allClasses == null || AssetManager.allClasses.Count == 0 ||
                (!tracked.Any(t => t.IsWeapon) && trackedTalents.Count == 0))
            {
                return;
            }
            Log.LogInfo("[PackLoader] Class weapon/talent counts: " + string.Join(", ",
                AssetManager.allClasses.Where(p => p != null).Select(p =>
                    $"{p.name}={(p.weapons?.Count ?? 0)}w/{(p.talents?.Count ?? 0)}t")));
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

            var owners = tracked
                .Select(t => new { Label = $"{t.PackName}/{t.Card.name}", Bindings = t.Effects })
                .Concat(trackedTalents
                    .Select(t => new { Label = $"{t.PackName}/{t.Talent.name} (weapon power)", Bindings = t.Effects }));

            foreach (var owner in owners)
            {
                foreach (EffectBinding b in owner.Bindings)
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
                            unresolved.Add($"referenceStatus '{b.Manifest.referenceStatus}' on {owner.Label}");
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
                            unresolved.Add($"referenceCard '{names[i]}' on {owner.Label}");
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
                    card.CardEffectList.Add(BuildEffect(fx, m.name, ic.Effects, knownCommands));
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
                        ench.CardEffectList.Add(BuildEffect(fx, m.name, ic.Effects, knownCommands));
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

        private static CardEffect BuildEffect(EffectManifest m, string ownerName,
            List<EffectBinding> sink, HashSet<string> vocabulary)
        {
            if (m == null)
            {
                throw new ManifestError("null effect entry");
            }
            ValidateCodeLine(m.codeLine, vocabulary);
            var fx = new CardEffect
            {
                cardTrigger = ParseEnum<EventHandler.GameTriggers>(m.trigger, "trigger", ownerName),
                codeLine = m.codeLine ?? "",
                forecast = m.forecast,
                hideReferenceCards = m.hideReferenceCards,
                referenceCard = new Card[m.referenceCards?.Count ?? 0],
                effectConditions = BuildConditions(m.conditions, ownerName)
            };
            sink.Add(new EffectBinding { Effect = fx, Manifest = m });
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

        private static void ValidateCodeLine(string codeLine, HashSet<string> vocabulary)
        {
            if (vocabulary == null || string.IsNullOrEmpty(codeLine))
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
                if (command.Length > 0 && !vocabulary.Contains(command))
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
            Sprite fromFile = LoadSpriteFile(packDir, m.art, card.name);
            return fromFile != null
                ? fromFile
                : CreatePlaceholderSprite(PlaceholderColor(card.GetColor()));
        }

        /// <summary>PNG at packDir/relPath as a HideAndDontSave sprite, or null.</summary>
        private static Sprite LoadSpriteFile(string packDir, string relPath, string ownerName)
        {
            if (string.IsNullOrEmpty(relPath))
            {
                return null;
            }
            string pngPath = Path.Combine(packDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(pngPath))
            {
                return null;
            }
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
                Log.LogWarning($"[PackLoader] {ownerName}: failed to load art '{pngPath}' ({ex.Message}) — using placeholder.");
                return null;
            }
        }

        /// <summary>
        /// 512×512 two-band placeholder: flat base color (cards: cost-color identity
        /// via Card.GetColor(); weapon powers: white), with a slightly darker lower
        /// half so it is obviously placeholder art.
        /// </summary>
        private static Sprite CreatePlaceholderSprite(Color32 baseColor)
        {
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
