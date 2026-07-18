namespace DawnKit.Packs
{
    /// <summary>
    /// The pack-side manifest schemaVersion handshake (M2; MOD-TOOLKIT §2.2,
    /// SPEC.md §11 #6). Pure decision logic — no logging, no I/O — so the boot
    /// self-check and offline harnesses exercise exactly the shipped rule:
    ///
    ///   effective = declared ?? 1   (absent means 1)
    ///   effective &gt;  Supported  → the WHOLE pack is refused (fail-safe: a
    ///                              newer schema may carry fields or content
    ///                              types this loader cannot see, so partial
    ///                              loading could silently ship half a mod)
    ///   effective &lt;= Supported  → loads normally
    ///
    /// Bump <see cref="SupportedSchemaVersion"/> only when DawnKit.Packs
    /// actually understands the new manifest schema (see src/README.md
    /// VERSIONING — a bump here is at least a minor engine release).
    /// </summary>
    internal static class SchemaGate
    {
        /// <summary>Highest pack.json schemaVersion this loader understands.</summary>
        internal const int SupportedSchemaVersion = 1;

        /// <summary>The version a manifest is treated as: declared, or 1 when absent.</summary>
        internal static int Effective(int? declared) => declared ?? 1;

        /// <summary>True when a manifest declaring this version may be loaded.</summary>
        internal static bool IsSupported(int? declared) => Effective(declared) <= SupportedSchemaVersion;

        /// <summary>
        /// Boot self-check (the M1b AutoId pattern): pins the gate's decision
        /// table so a regression is visible in the log without a debugger.
        /// Returns true when all reference cases agree.
        /// </summary>
        internal static bool SelfCheck(out int passed, out int total)
        {
            // (declared, expected effective, expected supported)
            var cases = new (int? declared, int effective, bool supported)[]
            {
                (null, 1, true),                      // absent → 1 → loads
                (0, 0, true),                         // lower than supported → loads
                (1, 1, true),                         // exactly supported → loads
                (2, 2, false),                        // newer → refused entirely
                (int.MaxValue, int.MaxValue, false),  // absurdly newer → refused
            };
            passed = 0;
            total = cases.Length;
            foreach (var c in cases)
            {
                if (Effective(c.declared) == c.effective && IsSupported(c.declared) == c.supported)
                {
                    passed++;
                }
            }
            return passed == total;
        }
    }
}
