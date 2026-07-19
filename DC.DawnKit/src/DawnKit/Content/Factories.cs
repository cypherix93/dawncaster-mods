using System.Collections.Generic;
using DawnKit.Content.Art;
using DawnKit.Core.Lifecycle;
using UnityEngine;

namespace DawnKit.Content.Factories
{
    /// <summary>
    /// Safe ScriptableObject construction from parsed specs (SPEC.md §3.2, P5):
    /// HideAndDontSave (survive scene loads, hide from Unity teardown), every
    /// list non-null, non-null CardEnchantments (Codex display code dereferences
    /// it), audioClip left null (every game usage site null-checks it). Pool
    /// routing mirrors AssetManager.ProcessCard. Reference fields are left
    /// unresolved here — the RefResolver fills them at the right load phase.
    /// </summary>
    internal static class CardFactory
    {
        /// <summary>Construct the Card and its effect bindings (registration.Card is set; nothing is added to AssetManager here).</summary>
        internal static void Build(CardRegistration reg)
        {
            ParsedCard m = reg.Spec;

            Card card = ScriptableObject.CreateInstance<Card>();
            card.name = m.Name;
            card.hideFlags = HideFlags.HideAndDontSave;
            card.cardID = m.CardId;
            card.cardexpansion = m.Expansion;
            card.cardType = m.Type;
            card.cardCategory = m.Category;
            card.cardSuffix = m.Suffix;
            card.cardRarity = m.Rarity;

            ApplyCosts(card, m.Costs);
            card.cardDescription = m.Description;
            card.utilityNumber = m.UtilityNumber;
            card.charges = m.Charges;

            card.keywords = new List<Card.CardProperties>(m.Keywords);
            card.cardKeywords = new List<string>(m.CardKeywords);

            foreach (string flag in m.Flags)
            {
                ApplyFlag(card, flag);
            }
            if (m.IsWeapon)
            {
                card.excludeFromRewards = true; // forced default for weapons (WEAPON-SPEC §5.2)
            }
            // Starting cards are deliberately NOT reward-excluded: 62/63 shipped
            // starting cards are normal acquirable reward-pool cards (WEAPON-SPEC §1).

            card.playConditions = BuildConditions(m.PlayConditions);

            reg.Bindings.Clear();
            card.CardEffectList = new List<CardEffect>();
            foreach (ParsedEffect fx in m.Effects)
            {
                card.CardEffectList.Add(BuildEffect(fx, reg.Bindings));
            }

            // Always a non-null Enchantment payload (Codex display code dereferences it).
            if (m.Enchantment == null)
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
                    theType = m.Enchantment.Type,
                    enchantmentText = m.Enchantment.Text,
                    combatEnchantment = m.Enchantment.Combat,
                    showstacks = m.Enchantment.ShowStacks,
                    CardEffectList = new List<CardEffect>()
                };
                foreach (ParsedEffect fx in m.Enchantment.Effects)
                {
                    ench.CardEffectList.Add(BuildEffect(fx, reg.Bindings));
                }
                card.CardEnchantments = ench;
            }

            // Art: shipped PNG when present, else a placeholder colored by the
            // card's cost-color identity. audioClip stays null (all usage sites
            // null-check: CodexUI.cs:1229, CombatUIHandler.cs:1224, SpellEffects.cs:260).
            card.artwork = ArtLoader.LoadSpriteFile(m.ArtPath, card.name)
                ?? ArtLoader.CreatePlaceholderSprite(ArtLoader.PlaceholderColor(card.GetColor()));

            reg.Card = card;
        }

        /// <summary>
        /// Pool routing, mirroring AssetManager.ProcessCard. Weapons register in
        /// allCards ONLY; starting cards route like normal cards (playercards-
        /// eligible per ProcessCard rules — WEAPON-SPEC §1 corpus rule).
        /// </summary>
        internal static void RegisterInPools(Card card, bool isWeapon)
        {
            if (isWeapon)
            {
                AssetManager.allCards.Add(card); // NOT playercards (WEAPON-SPEC §5.2)
                return;
            }
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

        internal static CardEffect BuildEffect(ParsedEffect m, List<EffectBinding> sink)
        {
            var fx = new CardEffect
            {
                cardTrigger = m.Trigger,
                codeLine = m.CodeLine,
                forecast = m.Forecast,
                hideReferenceCards = m.HideReferenceCards,
                referenceCard = new Card[m.ReferenceCards?.Count ?? 0],
                effectConditions = BuildConditions(m.Conditions)
            };
            sink.Add(new EffectBinding { Effect = fx, Spec = m });
            return fx;
        }

        internal static List<Condition> BuildConditions(List<ParsedCondition> list)
        {
            var result = new List<Condition>();
            if (list == null)
            {
                return result;
            }
            foreach (ParsedCondition c in list)
            {
                result.Add(new Condition
                {
                    valueToCheck = c.Value,
                    conditonOperator = c.Op,
                    targetValue = c.Target,
                    ignoreForDisplay = c.IgnoreForDisplay
                });
            }
            return result;
        }

        private static void ApplyCosts(Card card, Dictionary<string, int> cost)
        {
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
            }
        }
    }

    /// <summary>
    /// Weapon-power Talent factory: tier forced 0 (weapon powers ARE the tier-0
    /// pool — CreateCharacterFunctions.GetRandomWeaponPower), unique, all
    /// exclusion flags false, requiredTalents/requiredProfessions empty (class
    /// gating is purely Profession.talents membership — WEAPON-SPEC §5.1).
    /// </summary>
    internal static class TalentFactory
    {
        internal static void Build(TalentRegistration reg)
        {
            ParsedTalent m = reg.Spec;

            Talent t = ScriptableObject.CreateInstance<Talent>();
            t.name = m.Name;
            t.hideFlags = HideFlags.HideAndDontSave;
            t.ID = m.TalentId;
            t.tier = 0;
            t.expansion = m.Expansion;
            t.description = m.Description;
            t.flavortext = m.Flavortext;
            t.cooldown = m.Cooldown;
            t.keywords = new List<string>(m.Keywords);
            t.unique = true;
            t.storyTalent = false;
            t.excludeFromRandom = false;
            t.excludeFromSunforge = false;
            t.excludeFromCodex = false;
            t.infernalOffering = false;
            t.requiredTalents = new List<Talent>();
            t.requiredProfessions = new List<Profession>();
            t.rDEX = m.RDex;
            t.rINT = m.RInt;
            t.rSTR = m.RStr;

            reg.Bindings.Clear();
            t.effectList = new List<CardEffect>();
            foreach (ParsedEffect fx in m.Effects)
            {
                t.effectList.Add(CardFactory.BuildEffect(fx, reg.Bindings));
            }

            // powerImage: shipped PNG when present, else the white two-band
            // placeholder. audioClip stays null (only usage site null-checks,
            // PlayerUIHandler.cs:1631).
            t.powerImage = ArtLoader.LoadSpriteFile(m.ArtPath, m.Name)
                ?? ArtLoader.CreatePlaceholderSprite(ArtLoader.PlaceholderColor(Card.ColorOverwrite.White));

            reg.Talent = t;
        }
    }

    /// <summary>
    /// Safe Dialogue construction from a parsed event spec (EVENT-SPEC §3):
    /// HideAndDontSave, non-null eventConditions (the P5 landmine class), and the
    /// naming rule Dialogue.name == TextAsset.name == event name — the area deck
    /// keys entries by textFile.name (AreaHandler.cs:508) while eventLookupCache
    /// keys by Dialogue.name (AssetManager.cs:317); equality makes every lookup
    /// path agree and keeps doneEvents entries human-readable.
    /// </summary>
    internal static class EventFactory
    {
        /// <summary>Construct the Dialogue + story TextAsset (nothing is added to AssetManager here).</summary>
        internal static void Build(EventRegistration reg)
        {
            ParsedEvent m = reg.Spec;

            var text = new TextAsset(m.StoryJson)
            {
                name = m.Name,
                hideFlags = HideFlags.HideAndDontSave,
            };

            Dialogue ev = ScriptableObject.CreateInstance<Dialogue>();
            ev.name = m.Name;
            ev.hideFlags = HideFlags.HideAndDontSave;
            ev.eventType = AreaHandler.EventTypes.opportunity;
            ev.nameOverwrite = "";
            ev.description = "";
            ev.eventConditions = new List<AreaCondition>();
            ev.textFile = text;
            ev.minimumLevel = m.MinLevel;
            ev.maxLevel = m.MaxLevel;
            ev.unique = m.Unique;
            // rarity stays default (Common=0, like shipped Mimic) — the rarity
            // filter only applies to shrines; opportunity map nodes render the
            // generic label + defaultOpportunityImage (EventDisplay.cs:783-789).

            reg.Text = text;
            reg.Event = ev;
        }
    }
}
