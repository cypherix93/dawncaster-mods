using System;
using System.Collections.Generic;
using System.Linq;
using DawnKit.Core.Lifecycle;
using UnityEngine;

namespace DawnKit.Integration.Sets
{
    /// <summary>
    /// One entry per mod set that has at least one injected card carrying its
    /// synthetic CardExpansions value.
    ///
    /// Synthetic set formula (CARD-PACK-SPEC.md §3):
    ///   expansion = 1000 + (idBlock.start - 700,000,000) / 100
    /// Deterministic from the pack's ID-REGISTRY block, so the value is stable
    /// across sessions, saves and machines. The whole game pipeline stores
    /// CardExpansions as ints (JsonUtility save data, PlayerPrefs "lastExcludedSets"
    /// round-trips through Enum.TryParse which accepts numeric strings), so
    /// undefined enum members survive persistence fine.
    /// </summary>
    internal sealed class PackSetInfo
    {
        internal string DisplayName;
        internal AssetManager.CardExpansions Expansion;
        internal readonly List<Card> Cards = new List<Card>();

        private SetConfig config;

        // (primary=row/fade tint, secondary=border) pairs, indexed by set value.
        private static readonly Color[,] Palette =
        {
            { new Color(0.55f, 0.22f, 0.10f), new Color(1.00f, 0.62f, 0.25f) }, // ember
            { new Color(0.20f, 0.38f, 0.14f), new Color(0.62f, 0.85f, 0.32f) }, // venom
            { new Color(0.42f, 0.33f, 0.13f), new Color(0.95f, 0.82f, 0.45f) }, // brass
            { new Color(0.42f, 0.10f, 0.14f), new Color(0.92f, 0.30f, 0.35f) }, // crimson
            { new Color(0.16f, 0.20f, 0.45f), new Color(0.45f, 0.55f, 0.95f) }, // indigo
            { new Color(0.10f, 0.36f, 0.36f), new Color(0.30f, 0.80f, 0.80f) }, // teal
            { new Color(0.32f, 0.16f, 0.42f), new Color(0.72f, 0.45f, 0.95f) }, // violet
            { new Color(0.25f, 0.28f, 0.32f), new Color(0.65f, 0.72f, 0.80f) }, // slate
        };

        internal SetConfig GetOrCreateConfig()
        {
            if (config != null)
            {
                return config;
            }
            int paletteIndex = Math.Abs((int)Expansion - 1000) % (Palette.Length / 2);
            Color primary = Palette[paletteIndex, 0];
            Color secondary = Palette[paletteIndex, 1];

            config = ScriptableObject.CreateInstance<SetConfig>();
            config.name = DisplayName; // read by accessibility Say() on toggle
            config.hideFlags = HideFlags.HideAndDontSave;
            config.expansion = Expansion;
            config.canBeDisabled = true;
            config.setKeywords = new List<string>();
            config.setDescription = $"Mod pack \"{DisplayName}\" ({Cards.Count} cards), loaded by DawnKit.";
            config.setPrimaryColor = primary;
            config.setSecondaryColor = secondary;
            config.previewCards = BuildPreviewCards();
            config.setPromoImage = CreatePromoSprite(primary, secondary);
            return config;
        }

        /// <summary>
        /// SetPreviewPanel.SetDisplay indexes set.previewCards[i] for each of ITS
        /// CardDisplay slots, so the list must be at least as long as the panel's
        /// slot count (unknown here). Pad by cycling to a safe length.
        /// </summary>
        private List<Card> BuildPreviewCards()
        {
            var list = new List<Card>();
            if (Cards.Count == 0)
            {
                return list;
            }
            const int safeLength = 12;
            for (int i = 0; i < Math.Max(Cards.Count, safeLength); i++)
            {
                list.Add(Cards[i % Cards.Count]);
            }
            return list;
        }

        /// <summary>Vertical two-tone banner so the row doesn't render a flat white block.</summary>
        private static Sprite CreatePromoSprite(Color primary, Color secondary)
        {
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
            var pixels = new Color32[w * h];
            Color32 top = Color.Lerp(primary, secondary, 0.35f);
            Color32 bottom = Color.Lerp(primary, Color.black, 0.45f);
            for (int y = 0; y < h; y++)
            {
                Color32 c = Color32.Lerp(bottom, top, (float)y / (h - 1));
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    pixels[row + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }

    /// <summary>
    /// The synthetic-set view over the injected content, consumed by the
    /// set-screen/Codex patches. Rebuilt on every injection pass so re-injection
    /// after ForceReloadAssets stays consistent.
    /// </summary>
    internal static class ModSets
    {
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

        internal static void Rebuild()
        {
            PackSets.Clear();
            foreach (CardRegistration t in Registry.Cards)
            {
                if (t.Card == null || !IsModExpansion(t.Card.cardexpansion))
                {
                    continue;
                }
                PackSetInfo set = FindSet(t.Card.cardexpansion);
                if (set == null)
                {
                    set = new PackSetInfo
                    {
                        DisplayName = DawnKit.Sets.FindDisplayName((int)t.Card.cardexpansion) ?? t.Spec.Owner,
                        Expansion = t.Card.cardexpansion
                    };
                    PackSets.Add(set);
                }
                set.Cards.Add(t.Card);
            }
            PackSets.Sort((a, b) => ((int)a.Expansion).CompareTo((int)b.Expansion));
            if (PackSets.Count > 0)
            {
                DawnKitPlugin.Log.LogInfo("[DawnKit] Synthetic card sets: " +
                    string.Join(", ", PackSets.Select(s => $"{s.DisplayName}=(CardExpansions){(int)s.Expansion} [{s.Cards.Count} cards]")));
            }
        }
    }
}
