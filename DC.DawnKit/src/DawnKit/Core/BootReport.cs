using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using DawnKit.Core.Lifecycle;
using DawnKit.Core.Ownership;

namespace DawnKit.Core.Status
{
    /// <summary>
    /// The consolidated boot conflict report (SPEC.md §3.5) and the
    /// DiagnosticsDump file (SPEC.md §5). Emitted after every injection pass and
    /// after the authoritative reference-resolution pass; the log block is
    /// deduplicated by content so a clean boot prints it exactly once. The same
    /// numbers feed the player-facing status row on the run-settings set screen.
    /// </summary>
    internal static class BootReport
    {
        private static string lastReport;
        private static bool diagnosticsAnnounced;

        internal static void Emit(string source)
        {
            try
            {
                if (RegistrationLedger.All.Count == 0)
                {
                    return; // engine installed alone — nothing changes, nothing to report
                }
                // The report text deliberately excludes the hook name: the final
                // ref-resolution pass re-emits only when the numbers changed.
                string report = BuildReport();
                if (report != lastReport)
                {
                    lastReport = report;
                    foreach (string line in report.Split('\n'))
                    {
                        DawnKitPlugin.Log.LogInfo("[DawnKit] " + line);
                    }
                }
                if (DawnKitPlugin.DiagnosticsDump)
                {
                    WriteDiagnostics();
                }
            }
            catch (Exception ex)
            {
                DawnKitPlugin.Log.LogError($"[DawnKit] Boot report failed: {ex}");
            }
        }

        // ------------------------------------------------------------------
        // Per-mod stats. "Registered" counts every Register() attempt,
        // "applied" the live injected instances, "failed" the difference
        // (validation refusals, cross-mod conflicts, pool collisions).
        // ------------------------------------------------------------------

        private static int AppliedCount(string owner)
        {
            return Registry.Cards.Count(r => r.Spec.Owner == owner && r.Card != null) +
                   Registry.Talents.Count(r => r.Spec.Owner == owner && r.Talent != null) +
                   Registry.Events.Count(r => r.Spec.Owner == owner && r.Event != null);
        }

        internal static int TotalApplied()
        {
            return Registry.Cards.Count(r => r.Card != null) +
                   Registry.Talents.Count(r => r.Talent != null) +
                   Registry.Events.Count(r => r.Event != null);
        }

        /// <summary>
        /// The status-row text (SPEC.md §2): "DawnKit: N mods, M items loaded",
        /// plus the error count when nonzero.
        /// </summary>
        internal static string StatusText()
        {
            List<string> owners = RegistrationLedger.ItemOwners();
            int applied = TotalApplied();
            int registered = owners.Sum(RegistrationLedger.RegisteredCount);
            int errors = Math.Max(0, registered - applied) +
                         RegistrationLedger.OfKind("set").Count(s => !s.Ok);
            string text = $"DawnKit: {owners.Count} mod{(owners.Count == 1 ? "" : "s")}, {applied} item{(applied == 1 ? "" : "s")} loaded";
            if (errors > 0)
            {
                text += $" — {errors} error{(errors == 1 ? "" : "s")}, see BepInEx/LogOutput.log";
            }
            return text;
        }

        private static string BuildReport()
        {
            var sb = new StringBuilder();
            List<string> owners = RegistrationLedger.ItemOwners();
            int totalRegistered = owners.Sum(RegistrationLedger.RegisteredCount);
            int totalApplied = TotalApplied();
            int conflicts = RegistrationLedger.ConflictCount;

            sb.Append("── Boot report ").Append('─', 30).Append('\n');
            sb.Append($"{owners.Count} mods · {totalRegistered} items registered, {totalApplied} applied, " +
                      $"{Math.Max(0, totalRegistered - totalApplied)} failed · {conflicts} conflict{(conflicts == 1 ? "" : "s")}\n");
            foreach (string owner in owners)
            {
                int registered = RegistrationLedger.RegisteredCount(owner);
                int applied = AppliedCount(owner);
                sb.Append($"  {owner}: {registered} registered, {applied} applied, {Math.Max(0, registered - applied)} failed\n");
            }
            if (conflicts > 0)
            {
                sb.Append("Conflicts (both claimants named):\n");
                foreach (string message in RegistrationLedger.ConflictMessages)
                {
                    sb.Append("  ✗ ").Append(message).Append('\n');
                }
            }
            else
            {
                sb.Append("Conflicts: none\n");
            }
            sb.Append("── end boot report ").Append('─', 26);
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // DiagnosticsDump ([Engine] DiagnosticsDump=true): the full ledger →
        // BepInEx/DawnKit-diagnostics.txt, overwritten each boot (P19's
        // bug-report channel).
        // ------------------------------------------------------------------

        private static void WriteDiagnostics()
        {
            string path = Path.Combine(Paths.BepInExRootPath, "DawnKit-diagnostics.txt");
            var sb = new StringBuilder();
            sb.Append($"DawnKit diagnostics — {DateTime.Now:yyyy-MM-dd HH:mm:ss} (DawnKit {DawnKitPlugin.Version})\n");
            sb.Append($"Status: {StatusText()}\n\n");

            foreach (string owner in RegistrationLedger.ItemOwners())
            {
                sb.Append($"== {owner} ==\n");
                foreach (SetHandle set in DawnKit.Sets.All)
                {
                    if ((set.Author ?? set.DisplayName) == owner || set.DisplayName == owner)
                    {
                        sb.Append($"  set          {set.SetValue,-10} \"{set.DisplayName}\" (ID block {set.IdBlockStart})\n");
                    }
                }
                foreach (RegistrationInfo e in RegistrationLedger.All)
                {
                    if (e.Owner != owner || e.Kind == "set")
                    {
                        continue;
                    }
                    string status = e.Ok ? (IsApplied(e) ? "applied" : "not applied") : $"FAILED: {e.Error}";
                    string extra = DescribeItem(e);
                    sb.Append($"  {e.Kind,-12} {e.Id,-10} \"{e.Name}\"{extra} — {status}\n");
                }
                sb.Append('\n');
            }

            sb.Append("== Conflicts ==\n");
            if (RegistrationLedger.ConflictCount == 0)
            {
                sb.Append("(none)\n");
            }
            else
            {
                foreach (string message in RegistrationLedger.ConflictMessages)
                {
                    sb.Append(message).Append('\n');
                }
            }

            sb.Append("\n== Unresolved references ==\n");
            if (RefResolver.ReferenceResolver.LastUnresolved.Count == 0)
            {
                sb.Append("(none)\n");
            }
            else
            {
                foreach (string u in RefResolver.ReferenceResolver.LastUnresolved)
                {
                    sb.Append(u).Append('\n');
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            if (!diagnosticsAnnounced)
            {
                diagnosticsAnnounced = true;
                DawnKitPlugin.Log.LogInfo($"[DawnKit] Diagnostics dump written: {path}");
            }
        }

        private static bool IsApplied(RegistrationInfo e)
        {
            if (e.Kind == "weaponPower")
            {
                return Registry.Talents.Any(r => r.Spec.Owner == e.Owner && r.Spec.TalentId == e.Id && r.Talent != null);
            }
            if (e.Kind == "event")
            {
                return Registry.Events.Any(r => r.Spec.Owner == e.Owner &&
                    string.Equals(r.Spec.Name, e.Name, StringComparison.OrdinalIgnoreCase) && r.Event != null);
            }
            return Registry.Cards.Any(r => r.Spec.Owner == e.Owner && r.Spec.CardId == e.Id && r.Card != null);
        }

        private static string DescribeItem(RegistrationInfo e)
        {
            if (!e.Ok)
            {
                return "";
            }
            if (e.Kind == "weaponPower")
            {
                TalentRegistration t = Registry.Talents.FirstOrDefault(r => r.Spec.Owner == e.Owner && r.Spec.TalentId == e.Id);
                if (t == null)
                {
                    return "";
                }
                string set = DawnKit.Sets.FindDisplayName((int)t.Spec.Expansion);
                string classes = t.Spec.Classes != null && t.Spec.Classes.Count > 0 ? string.Join("/", t.Spec.Classes) : null;
                return (set != null ? $" set={set}" : "") + (classes != null ? $" classes={classes}" : "");
            }
            if (e.Kind == "event")
            {
                EventRegistration ev = Registry.Events.FirstOrDefault(r => r.Spec.Owner == e.Owner &&
                    string.Equals(r.Spec.Name, e.Name, StringComparison.OrdinalIgnoreCase));
                if (ev == null)
                {
                    return "";
                }
                string levels = ev.Spec.MaxLevel == 0 ? $"{ev.Spec.MinLevel}+" : $"{ev.Spec.MinLevel}-{ev.Spec.MaxLevel}";
                return $" levels={levels}{(ev.Spec.Unique ? " unique" : "")}";
            }
            CardRegistration c = Registry.Cards.FirstOrDefault(r => r.Spec.Owner == e.Owner && r.Spec.CardId == e.Id);
            if (c == null)
            {
                return "";
            }
            string cardSet = DawnKit.Sets.FindDisplayName((int)c.Spec.Expansion) ?? c.Spec.Expansion.ToString();
            string cardClasses = c.Spec.Kind != CardKind.Card && c.Spec.Classes != null && c.Spec.Classes.Count > 0
                ? $" classes={string.Join("/", c.Spec.Classes)}" : "";
            return $" set={cardSet}{cardClasses}";
        }
    }
}
