using BepInEx;
using DawnKit;

namespace MyFirstMod
{
    [BepInPlugin("com.example.myfirstmod", "My First Mod", "1.0.0")]
    [BepInDependency(DawnKitPlugin.Guid)] // load after the DawnKit engine
    public sealed class MyFirstModPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            SetHandle set = Sets.Register("My First Mod", author: "dcmods.example");

            RegisterResult result = Cards.Build("Practice Strike")
                .InSet(set)                     // our own toggleable card set row
                .AutoId()                       // deterministic ID from the set's block
                .Type(CardType.Melee)
                .Category(CardCategory.Action)
                .Rarity(Rarity.Common)
                .Cost("STR", 1)
                .Description("Deal 6 damage.")
                .Effect(Trigger.PlayAction, "damage:6")
                .Register();

            if (!result.Ok)
                Logger.LogError($"[MyFirstMod] Practice Strike refused: {result.Error}");
        }
    }
}
