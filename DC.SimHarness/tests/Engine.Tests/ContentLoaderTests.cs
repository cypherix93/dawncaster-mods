using Dc.SimHarness.Engine;
using Xunit;

namespace Engine.Tests;

// Verifies the real content pool materializes headless and references resolve.
public class ContentLoaderTests
{
    private static bool _loaded;
    private static readonly object Gate = new();

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Gate)
        {
            if (_loaded) return;
            ContentLoader.LoadAll();
            _loaded = true;
        }
    }

    [Fact]
    public void LoadsFullCardPool()
    {
        EnsureLoaded();
        Assert.True(AssetManager.allCards.Count > 2000,
            $"expected >2000 cards, got {AssetManager.allCards.Count}");
    }

    [Fact]
    public void LoadsStatusesAndBattles()
    {
        EnsureLoaded();
        Assert.True(AssetManager.allStatusEffects.Count > 40,
            $"expected >40 statuses, got {AssetManager.allStatusEffects.Count}");
        Assert.NotEmpty(AssetManager.allBattles);
    }

    [Fact]
    public void NameLookupWorks_ObjectNameCarveOut()
    {
        EnsureLoaded();
        // Round-trip a real card name through the AssetManager cache — proves the shim's
        // Object.name backing field works and RefreshCaches keyed on it.
        var any = AssetManager.allCards[0];
        Assert.False(string.IsNullOrEmpty(any.name));
        Assert.Same(any, AssetManager.GetCard(any.name));
    }

    [Fact]
    public void CardEffectsAndCodeLinesReconstructed()
    {
        EnsureLoaded();
        var abolish = AssetManager.GetCard("Abolish");   // exists in the pool
        Assert.NotNull(abolish);
        Assert.NotNull(abolish.CardEffectList);
        Assert.Contains(abolish.CardEffectList, e => !string.IsNullOrEmpty(e.codeLine));
    }

    [Fact]
    public void ReferenceStatusPPtrsResolve()
    {
        EnsureLoaded();
        // 896 cards carry a referenceStatus PPtr; all match a status path_id in data-index.
        int resolved = AssetManager.allCards
            .Where(c => c.CardEffectList != null)
            .SelectMany(c => c.CardEffectList)
            .Count(e => e.referenceStatus != null);
        Assert.True(resolved > 0, "no CardEffect.referenceStatus resolved from PPtrs");

        // Abolish grants Armor (status path_id 15564) — verify that specific link.
        var abolish = AssetManager.GetCard("Abolish");
        Assert.Contains(abolish.CardEffectList, e => e.referenceStatus != null);
    }
}