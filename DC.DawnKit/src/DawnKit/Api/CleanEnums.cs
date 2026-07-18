namespace DawnKit
{
    /// <summary>
    /// Maps the clean-spelled public enum mirrors (Enums.cs) to the game's exact
    /// member spellings. Every mirror member maps 1:1 by name except the entries
    /// listed here — the game's canonical typos (docs/CONVENTIONS.md: the enums
    /// are API surface; the public C# API exposes corrected spellings and maps
    /// internally). Data packs (pack.json) keep the game's exact spellings for
    /// 1:1 traceability, so this table is only used by the typed builder setters.
    /// </summary>
    internal static class CleanEnums
    {
        internal static string GameName(CardType v) => v.ToString();
        internal static string GameName(CardCategory v) => v.ToString();
        internal static string GameName(Rarity v) => v.ToString();
        internal static string GameName(Expansion v) => v.ToString();
        internal static string GameName(Trigger v) => v.ToString();

        internal static string GameName(Suffix v) =>
            v == Suffix.Physical ? "Phyisical" : v.ToString(); // Card.Suffix.Phyisical
    }
}
