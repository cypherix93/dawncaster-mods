using System.Collections.Generic;
using DawnKit.Core.Lifecycle;

namespace DawnKit
{
    /// <summary>
    /// Opportunity events (EVENT-SPEC.md): Ink-scripted map encounters that join
    /// the same global fill pool as vanilla roadside events. Registration is
    /// declarative and durable — the engine constructs the Dialogue + story
    /// TextAsset at the WORLD-asset load phase, and a StartDialogue patch serves
    /// the story when the node is picked. Events are name-keyed: the event name
    /// becomes Dialogue.name, TextAsset.name and the persistent doneEvents key —
    /// unique (case-insensitive) vs shipped events and every other mod.
    /// </summary>
    public static class Events
    {
        public static EventBuilder Build(string name) => new EventBuilder(name);

        /// <summary>Every event registration attempt (including failed ones), with ownership metadata.</summary>
        public static IReadOnlyList<RegistrationInfo> All => Core.Ownership.RegistrationLedger.OfKind("event");
    }

    public sealed class EventBuilder
    {
        internal readonly EventDraft Draft = new EventDraft();

        internal EventBuilder(string name)
        {
            Draft.Name = name;
        }

        /// <summary>Owning mod/pack display name — log lines and the ownership registry.</summary>
        public EventBuilder Owner(string owner) { Draft.Owner = owner; return this; }

        /// <summary>Compiled Ink JSON (inkVersion 18-20; inklecate v1.0.0 emits 20).</summary>
        public EventBuilder StoryJson(string compiledInkJson) { Draft.StoryJson = compiledInkJson; return this; }

        /// <summary>Absolute path to a compiled Ink JSON file — read at Register().</summary>
        public EventBuilder StoryFile(string absoluteJsonPath) { Draft.StoryFilePath = absoluteJsonPath; return this; }

        /// <summary>Area-level gate: Dialogue.minimumLevel / maxLevel (max 0 = uncapped).</summary>
        public EventBuilder Levels(int min, int max) { Draft.MinLevel = min; Draft.MaxLevel = max; return this; }

        /// <summary>Never re-offered after being picked once (persisted in the save's doneEvents).</summary>
        public EventBuilder Unique(bool unique = true) { Draft.Unique = unique; return this; }

        /// <summary>Validate and register. The engine injects at the next world-asset load phase.</summary>
        public RegisterResult Register() => Registry.RegisterEvent(Draft);
    }
}
