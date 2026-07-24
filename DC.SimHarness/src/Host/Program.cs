using Dc.SimHarness.Engine;

// v1 CLI: load the real pool (optionally a modded pack) headless and report.
// Combat-driver subcommands land here once the fight loop is verified.
GameBootstrap.EnsureInitialized();
ContentLoader.LoadAll();
Console.WriteLine($"Loaded headless: {AssetManager.allCards.Count} cards, " +
    $"{AssetManager.allStatusEffects.Count} statuses, {AssetManager.allBattles.Count} battles.");
