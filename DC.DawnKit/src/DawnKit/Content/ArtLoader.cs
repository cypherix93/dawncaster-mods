using System;
using System.IO;
using UnityEngine;

namespace DawnKit.Content.Art
{
    /// <summary>
    /// PNG → Sprite loading (100 PPU, Clamp, HideAndDontSave) and the generated
    /// two-band placeholder (flat cost-identity base color with a darker lower
    /// half so it reads as placeholder art). See ART-PIPELINE.md for the art
    /// contract (512×512 full-bleed, frame drawn by the UI).
    /// </summary>
    internal static class ArtLoader
    {
        /// <summary>PNG at an absolute path as a HideAndDontSave sprite, or null.</summary>
        internal static Sprite LoadSpriteFile(string pngPath, string ownerName)
        {
            if (string.IsNullOrEmpty(pngPath) || !File.Exists(pngPath))
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
                DawnKitPlugin.Log.LogWarning($"[DawnKit] {ownerName}: failed to load art '{pngPath}' ({ex.Message}) — using placeholder.");
                return null;
            }
        }

        /// <summary>
        /// 512×512 two-band placeholder: flat base color (cards: cost-color
        /// identity via Card.GetColor(); weapon powers: white), with a slightly
        /// darker lower half so it is obviously placeholder art.
        /// </summary>
        internal static Sprite CreatePlaceholderSprite(Color32 baseColor)
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

        internal static Color32 PlaceholderColor(Card.ColorOverwrite color)
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
