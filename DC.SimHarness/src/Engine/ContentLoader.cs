using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;
using UnityObject = UnityEngine.Object;

namespace Dc.SimHarness.Engine;

/// Reconstructs real Card/StatusEffect/Battle instances from the extracted
/// tools/out/data JSON and populates AssetManager's static lists + caches — the
/// headless equivalent of the game's Resources.LoadAll, bypassing Addressables.
public static class ContentLoader
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// path_id -> asset name, per class prefix (e.g. "StatusEffect/") from data-index.json.
    private static Dictionary<long, string> PathIdMap(JObject index, string classPrefix)
    {
        var map = new Dictionary<long, string>();
        foreach (var (key, val) in index)
            if (key.StartsWith(classPrefix, StringComparison.Ordinal) && val?["path_id"] != null)
                map[(long)val["path_id"]!] = key[classPrefix.Length..];
        return map;
    }

    public static void LoadAll(string? dataRoot = null, string? dataIndexPath = null)
    {
        GameBootstrap.EnsureInitialized();
        dataRoot ??= GameBootstrap.DataRoot!;
        dataIndexPath ??= GameBootstrap.DataIndex!;
        var index = JObject.Parse(File.ReadAllText(dataIndexPath));

        // 1. Statuses first (cards reference them). Track by path_id for PPtr resolution.
        var statusPath = PathIdMap(index, "StatusEffect/");
        var statusByName = new Dictionary<string, StatusEffect>(StringComparer.Ordinal);
        var statusByPathId = new Dictionary<long, StatusEffect>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(dataRoot, "StatusEffect"), "*.json"))
        {
            var raw = JObject.Parse(File.ReadAllText(f));
            var se = (StatusEffect)Build(typeof(StatusEffect), raw);
            se.name = (string)raw["m_Name"]!;
            AssetManager.allStatusEffects.Add(se);
            statusByName[se.name] = se;
        }
        foreach (var (pid, sName) in statusPath)
            if (statusByName.TryGetValue(sName, out var se)) statusByPathId[pid] = se;

        // 2. Cards. Resolve each CardEffect.referenceStatus PPtr (a status path_id) post-build.
        foreach (var f in Directory.EnumerateFiles(Path.Combine(dataRoot, "Card"), "*.json"))
        {
            var raw = JObject.Parse(File.ReadAllText(f));
            var card = (Card)Build(typeof(Card), raw);
            card.name = (string)raw["m_Name"]!;
            ResolveReferenceStatuses(card, raw, statusByPathId);
            AssetManager.allCards.Add(card);
            if (card.cardexpansion != AssetManager.CardExpansions.None
                && card.cardexpansion != AssetManager.CardExpansions.Metaprogress)
                AssetManager.playercards.Add(card);
            else if (card.cardexpansion == AssetManager.CardExpansions.Metaprogress)
                AssetManager.metacards.Add(card);
        }

        // 3. Battles (monster decks / enemy stats).
        var battleDir = Path.Combine(dataRoot, "Battle");
        if (Directory.Exists(battleDir))
            foreach (var f in Directory.EnumerateFiles(battleDir, "*.json"))
            {
                var raw = JObject.Parse(File.ReadAllText(f));
                var b = (Battle)Build(typeof(Battle), raw);
                b.name = (string)raw["m_Name"]!;
                AssetManager.allBattles.Add(b);
            }

        // RefreshCaches only builds the lookup caches when these flags are set (normally by
        // the real asset-load path we bypass). Set them so GetCard/GetStatus/GetBattle work.
        foreach (var flag in new[] { "playerAssetsLoaded", "worldAssetsLoaded" })
            typeof(AssetManager).GetField(flag, BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, true);

        AssetManager.RefreshCaches();
        GameBootstrap.EnsurePlayerData();   // CreateRunLists reads thePlayerData.excludedsets/bannedCards
        AssetManager.CreateRunLists();
    }

    private static void ResolveReferenceStatuses(Card card, JObject raw, Dictionary<long, StatusEffect> byPathId)
    {
        if (raw["CardEffectList"] is not JArray effects || card.CardEffectList == null) return;
        for (int i = 0; i < effects.Count && i < card.CardEffectList.Count; i++)
        {
            var pid = (long?)effects[i]?["referenceStatus"]?["m_PathID"] ?? 0;
            if (pid != 0 && byPathId.TryGetValue(pid, out var se))
                card.CardEffectList[i].referenceStatus = se;
        }
    }

    // ---- recursive JSON -> managed object populator ---------------------------------

    private static object? Build(Type type, JToken token)
    {
        var obj = FormatterServices.GetUninitializedObject(type);
        if (token is not JObject jo) return obj;
        foreach (var field in type.GetFields(PublicInstance))
        {
            if (!jo.TryGetValue(field.Name, out var val) || val == null || val.Type == JTokenType.Null) continue;
            try
            {
                var converted = Convert(field.FieldType, val);
                if (converted != null) field.SetValue(obj, converted);
            }
            catch { /* skip a field we can't map; combat correctness is validated separately */ }
        }
        return obj;
    }

    private static object? Convert(Type t, JToken token)
    {
        // UnityEngine references (Sprite/AudioClip/StatusEffect PPtrs, etc.) — cannot
        // materialize headless; leave null. referenceStatus is resolved in a post-pass.
        if (typeof(UnityObject).IsAssignableFrom(t)) return null;
        // UnityEngine value types (Color/Vector/…) — not needed for combat math; default.
        if (t.IsValueType && t.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) == true) return null;

        if (t.IsEnum)
            return token.Type is JTokenType.Integer or JTokenType.Float
                ? Enum.ToObject(t, (int)token)
                : Enum.Parse(t, token.ToString());

        if (t == typeof(string)) return token.Type == JTokenType.Null ? null : token.ToString();
        if (t == typeof(bool)) return token.Type == JTokenType.Boolean ? (bool)token : (int)token != 0; // Unity stores bool as 0/1
        if (t == typeof(int)) return (int)token;
        if (t == typeof(long)) return (long)token;
        if (t == typeof(float)) return (float)token;
        if (t == typeof(double)) return (double)token;

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elem = t.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(t)!;
            if (token is JArray arr)
                foreach (var e in arr) { var c = Convert(elem, e); if (c != null || !elem.IsValueType) list.Add(c); }
            return list;
        }

        // Nested [Serializable] data class (CardEffect, Condition, Enchantment, Status, …).
        if (t.IsClass) return Build(t, token);

        return null; // unknown value-type shape — leave default
    }
}
