using BepInEx;
using BepInEx.Configuration;

namespace Dawncaster.Sandbox
{
    /// <summary>
    /// Thin dev sandbox (SPEC.md §3.1): the SandboxStrike hello-world card,
    /// registered through the DawnKit PUBLIC API only — no Harmony, no game
    /// assembly, no engine internals. Experiments only; never shipped. Also the
    /// living proof of the clean-spelled enum mirrors (typed builder overloads).
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(DawnKit.DawnKitPlugin.Guid)]
    public sealed class SandboxPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.dawncastermods.sandbox";
        public const string PluginName = "Dawncaster Sandbox";
        public const string PluginVersion = "0.7.0";

        private ConfigEntry<bool> injectSandboxCard;

        private void Awake()
        {
            injectSandboxCard = Config.Bind("Sandbox", "InjectSandboxCard",
                false,
                "Inject the SandboxStrike hello-world test card (id 900001).");

            if (!injectSandboxCard.Value)
            {
                Logger.LogInfo($"[Sandbox] {PluginName} {PluginVersion} loaded (InjectSandboxCard=false — nothing registered).");
                return;
            }

            // Registration is declarative and lifecycle-safe: DawnKit applies it
            // at the right load phase, re-applies after ForceReloadAssets, and
            // refreshes caches/run-lists. This plugin runs zero patches.
            DawnKit.RegisterResult result = DawnKit.Cards.Build("SandboxStrike")
                .Owner("Sandbox")
                .Id(900001)
                .Expansion(DawnKit.Expansion.Core)
                .Type(DawnKit.CardType.Melee)
                .Category(DawnKit.CardCategory.Action)
                .Rarity(DawnKit.Rarity.Common) // clean spelling — engine maps to the game's Card.CardRariry
                .Cost("STR", 1)
                .Description("Deal 6 damage. (Sandbox mod test)")
                .Effect(DawnKit.Trigger.PlayAction, "damage:6")
                .Register();

            Logger.LogInfo(result.Ok
                ? $"[Sandbox] {PluginName} {PluginVersion} loaded — SandboxStrike registered (applied at asset load)."
                : $"[Sandbox] {PluginName} {PluginVersion} loaded — SandboxStrike registration FAILED: {result.Error}");
        }
    }
}
