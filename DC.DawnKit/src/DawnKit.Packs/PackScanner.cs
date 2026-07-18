using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace DawnKit.Packs
{
    /// <summary>
    /// Pack-folder scan → manifest parse → public-API registration. Pure
    /// data-to-builder mapping: all validation (enums, codeLine vocabulary, cost
    /// keys, flags, collisions) happens in the engine at Register(); one bad item
    /// is skipped with a named error and one bad manifest skips only that pack.
    /// String enum values are passed through with the game's exact spellings
    /// (CARD-PACK-SPEC.md rule — warn-and-accept case fixes happen engine-side).
    /// </summary>
    internal static class PackScanner
    {
        internal static void RegisterAll(string packsPath, string expansionOverride, bool autoDiscover)
        {
            try
            {
                if (string.IsNullOrEmpty(packsPath) || !Directory.Exists(packsPath))
                {
                    PacksPlugin.Log.LogWarning($"[DawnKit.Packs] Packs path not found: '{packsPath}' — nothing to load.");
                    return;
                }

                foreach (string packDir in Directory.GetDirectories(packsPath).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    RegisterPack(packDir, expansionOverride, autoDiscover);
                }
            }
            catch (Exception ex)
            {
                PacksPlugin.Log.LogError($"[DawnKit.Packs] Pack scan failed: {ex}");
            }
        }

        private static void RegisterPack(string packDir, string expansionOverride, bool autoDiscover)
        {
            string manifestFile = Path.Combine(packDir, "pack.json");
            if (!File.Exists(manifestFile))
            {
                return; // not a pack folder
            }

            PackManifest pm;
            try
            {
                pm = JsonConvert.DeserializeObject<PackManifest>(File.ReadAllText(manifestFile));
            }
            catch (Exception ex)
            {
                PacksPlugin.Log.LogError($"[DawnKit.Packs] Failed to parse {manifestFile}: {ex.Message} — pack skipped.");
                return;
            }

            if (pm == null)
            {
                PacksPlugin.Log.LogError($"[DawnKit.Packs] {manifestFile}: empty manifest — pack skipped.");
                return;
            }

            string packName = string.IsNullOrEmpty(pm.pack) ? Path.GetFileName(packDir) : pm.pack;

            // Manifest schemaVersion handshake (M2, SchemaGate doc): this check
            // runs BEFORE the content checks — a newer-schema pack may consist
            // entirely of content types this loader does not know about, so no
            // other conclusion about it is safe. Whole-pack refusal, reported to
            // the engine ledger so the boot report / status row count it as a
            // failed mod.
            if (!SchemaGate.IsSupported(pm.schemaVersion))
            {
                string msg = $"declares pack.json schemaVersion {pm.schemaVersion}, but this DawnKit.Packs " +
                             $"supports up to {SchemaGate.SupportedSchemaVersion} — pack refused entirely. " +
                             "Remedy: update DawnKit.Packs (and DawnKit) to a release that supports it.";
                PacksPlugin.Log.LogError($"[DawnKit.Packs] {packName}: {msg}");
                Mods.ReportFailedMod(packName, msg);
                return;
            }

            bool hasCards = pm.cards != null && pm.cards.Count > 0;
            bool hasWeapons = pm.weapons != null && pm.weapons.Count > 0;
            bool hasPowers = pm.weaponPowers != null && pm.weaponPowers.Count > 0;
            if (!hasCards && !hasWeapons && !hasPowers)
            {
                PacksPlugin.Log.LogError($"[DawnKit.Packs] {manifestFile}: no cards/weapons/weaponPowers in manifest — pack skipped.");
                return;
            }

            // Per-pack synthetic set from the ID block (CARD-PACK-SPEC.md §3);
            // an explicit ExpansionOverride wins and disables synthetic sets.
            SetHandle set = null;
            if (expansionOverride == null)
            {
                if (pm.idBlock == null || pm.idBlock.Count < 1)
                {
                    PacksPlugin.Log.LogWarning($"[DawnKit.Packs] {packName}: manifest has no idBlock — no synthetic card set, manifest expansion used as-is.");
                }
                else
                {
                    set = Sets.Register(packName, pm.idBlock[0], author: packName);
                }
            }

            int cards = 0, weapons = 0, powers = 0, failed = 0;

            foreach (CardManifest cm in pm.cards ?? new List<CardManifest>())
            {
                if (cm == null)
                {
                    PacksPlugin.Log.LogError($"[DawnKit.Packs] {packName}: null card entry — skipped.");
                    failed++;
                    continue;
                }
                CardBuilder b = Cards.Build(cm.name).Owner(packName);
                MapCard(b, cm, packDir, set, expansionOverride, autoDiscover);
                if (b.Register().Ok) cards++; else failed++;
            }

            foreach (WeaponManifest wm in pm.weapons ?? new List<WeaponManifest>())
            {
                if (wm == null)
                {
                    PacksPlugin.Log.LogError($"[DawnKit.Packs] {packName}: null weapon entry — skipped.");
                    failed++;
                    continue;
                }
                WeaponBuilder b = Weapons.Build(wm.name).Owner(packName).ForClasses(wm.classes);
                MapCard(b, wm, packDir, set, expansionOverride, autoDiscover);
                if (b.Register().Ok) weapons++; else failed++;
            }

            foreach (WeaponPowerManifest wp in pm.weaponPowers ?? new List<WeaponPowerManifest>())
            {
                if (wp == null)
                {
                    PacksPlugin.Log.LogError($"[DawnKit.Packs] {packName}: null weaponPower entry — skipped.");
                    failed++;
                    continue;
                }
                if (RegisterPower(wp, packName, packDir, set, expansionOverride).Ok) powers++; else failed++;
            }

            string failNote = failed > 0 ? $", {failed} failed validation" : "";
            PacksPlugin.Log.LogInfo($"[DawnKit.Packs] {packName}: registered {cards} cards, {weapons} weapons, {powers} weapon powers{failNote} (applied at asset load).");
        }

        /// <summary>Manifest → builder mapping shared by cards and weapons (a weapon IS a card + classes).</summary>
        private static void MapCard<T>(CardBuilderBase<T> b, CardManifest m, string packDir,
            SetHandle set, string expansionOverride, bool autoDiscover) where T : CardBuilderBase<T>
        {
            b.Id(m.cardID);

            // Precedence mirrors the pre-split loader: explicit config override >
            // per-pack synthetic set > manifest expansion value.
            if (expansionOverride != null)
            {
                b.Expansion(expansionOverride);
            }
            else if (set != null)
            {
                b.InSet(set);
            }
            else
            {
                b.Expansion(m.expansion);
            }

            b.Type(m.type).Category(m.category).Suffix(m.suffix).Rarity(m.rarity);
            if (m.cost != null)
            {
                b.Costs(m.cost);
            }
            b.Description(m.description).UtilityNumber(m.utilityNumber).Charges(m.charges);
            if (m.keywords != null)
            {
                b.Keywords(m.keywords);
            }
            if (m.cardKeywords != null)
            {
                b.CardKeywords(m.cardKeywords);
            }
            if (m.flags != null)
            {
                b.Flags(m.flags);
            }
            if (m.playConditions != null)
            {
                b.PlayConditions(m.playConditions.Select(MapCondition));
            }
            if (m.effects != null)
            {
                b.Effects(m.effects.Select(MapEffect));
            }
            if (m.enchantment != null)
            {
                b.Enchantment(new EnchantmentSpec
                {
                    Type = m.enchantment.type,
                    Text = m.enchantment.text,
                    Combat = m.enchantment.combat,
                    ShowStacks = m.enchantment.showstacks,
                    Effects = m.enchantment.effects?.Select(MapEffect).ToList()
                });
            }
            if (!string.IsNullOrEmpty(m.art))
            {
                b.Art(PackPath(packDir, m.art));
            }
            b.CodexDiscovery(autoDiscover);
        }

        private static RegisterResult RegisterPower(WeaponPowerManifest wp, string packName, string packDir,
            SetHandle set, string expansionOverride)
        {
            WeaponPowerBuilder b = WeaponPowers.Build(wp.name).Owner(packName)
                .Id(wp.talentID)
                .Description(wp.description)
                .Flavortext(wp.flavortext)
                .Cooldown(wp.cooldown)
                .ForClasses(wp.classes);
            if (expansionOverride != null)
            {
                b.Expansion(expansionOverride);
            }
            else if (set != null)
            {
                b.InSet(set);
            }
            // else: engine default (Core), matching the pre-split loader.
            if (wp.keywords != null)
            {
                b.Keywords(wp.keywords);
            }
            if (wp.requirements != null)
            {
                b.Requirements(wp.requirements.rDEX, wp.requirements.rINT, wp.requirements.rSTR);
            }
            if (wp.effects != null)
            {
                b.Effects(wp.effects.Select(MapEffect));
            }
            // powerImage: explicit manifest path, else art/<Name>.png, else the
            // engine's white placeholder (missing files fall through engine-side).
            string artRel = string.IsNullOrEmpty(wp.art) ? $"art/{wp.name}.png" : wp.art;
            if (!string.IsNullOrEmpty(wp.name) || !string.IsNullOrEmpty(wp.art))
            {
                b.Art(PackPath(packDir, artRel));
            }
            return b.Register();
        }

        private static EffectSpec MapEffect(EffectManifest m)
        {
            if (m == null)
            {
                return null; // engine reports "null effect entry" at Register()
            }
            return new EffectSpec
            {
                Trigger = m.trigger,
                CodeLine = m.codeLine,
                Forecast = m.forecast,
                ReferenceStatus = m.referenceStatus,
                ReferenceCards = m.referenceCards,
                HideReferenceCards = m.hideReferenceCards,
                Conditions = m.conditions?.Select(MapCondition).ToList()
            };
        }

        private static ConditionSpec MapCondition(ConditionManifest m)
        {
            if (m == null)
            {
                return null; // engine reports "null condition entry" at Register()
            }
            return new ConditionSpec
            {
                Value = m.value,
                Op = m.op,
                Target = m.target,
                IgnoreForDisplay = m.ignoreForDisplay
            };
        }

        private static string PackPath(string packDir, string relPath)
        {
            return Path.Combine(packDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
