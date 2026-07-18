using DawnKit.Core.Ownership;

namespace DawnKit
{
    /// <summary>
    /// Mod-level reporting surface (M2). Item-level failures are already
    /// captured by the builders' <see cref="RegisterResult"/> path; this is the
    /// seam for failures that happen BEFORE any item can be registered — e.g.
    /// DawnKit.Packs refusing a whole pack over a newer-than-supported manifest
    /// schemaVersion. Reporting here puts the mod into the registration ledger
    /// as a failed registration, so the boot report gains a per-mod row
    /// ("&lt;owner&gt;: 1 registered, 0 applied, 1 failed"), the in-game status
    /// row's error count includes it, and the DiagnosticsDump names the reason.
    /// </summary>
    public static class Mods
    {
        /// <summary>
        /// Record that an entire mod/pack failed to load. <paramref name="owner"/>
        /// is the mod's display/owner name (falls back to the calling plugin's
        /// GUID when null/empty); <paramref name="error"/> is the human-readable
        /// reason shown in diagnostics. Safe to call at any time before or
        /// during boot; never throws.
        /// </summary>
        public static void ReportFailedMod(string owner, string error)
        {
            if (string.IsNullOrEmpty(owner))
            {
                owner = OwnerResolver.ResolveCallingOwner();
            }
            RegistrationLedger.Record(new RegistrationInfo(owner, "pack", 0, owner, false, error ?? "(no reason given)"));
        }
    }
}
