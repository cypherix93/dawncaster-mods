namespace DawnKit
{
    /// <summary>
    /// Result of a builder Register() call (SPEC.md §3.4). Per-item failure
    /// isolation: a failed registration is skipped with a rich, named error; the
    /// mod, the other items, and the game load on. Failures are also retained in
    /// the registration ledger (M1b surfaces them in the conflict report and the
    /// in-game status UI).
    /// </summary>
    public sealed class RegisterResult
    {
        public bool Ok { get; }
        /// <summary>Human-readable failure reason; null when Ok.</summary>
        public string Error { get; }
        /// <summary>"card" / "weapon" / "weaponPower" / "set".</summary>
        public string Kind { get; }
        public string Owner { get; }
        public string Name { get; }

        private RegisterResult(bool ok, string kind, string owner, string name, string error)
        {
            Ok = ok;
            Kind = kind;
            Owner = owner;
            Name = name;
            Error = error;
        }

        internal static RegisterResult Success(string kind, string owner, string name) =>
            new RegisterResult(true, kind, owner, name, null);

        internal static RegisterResult Failed(string kind, string owner, string name, string error) =>
            new RegisterResult(false, kind, owner, name, error);
    }

    /// <summary>
    /// One row of the inspectable registration surface (Cards.All etc.). Ownership
    /// metadata is the M1b conflict-report seam (SPEC.md §3.5).
    /// </summary>
    public sealed class RegistrationInfo
    {
        public string Owner { get; }
        public string Kind { get; }
        public long Id { get; }
        public string Name { get; }
        public bool Ok { get; }
        public string Error { get; }

        internal RegistrationInfo(string owner, string kind, long id, string name, bool ok, string error)
        {
            Owner = owner;
            Kind = kind;
            Id = id;
            Name = name;
            Ok = ok;
            Error = error;
        }
    }
}
