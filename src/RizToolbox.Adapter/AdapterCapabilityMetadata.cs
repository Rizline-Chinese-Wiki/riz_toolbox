using RizToolbox.Core;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter;

/// <summary>Documents the current operations, including sampled/experimental limitations.</summary>
internal static class AdapterCapabilityMetadata
{
    internal static void Declare(CapabilityRegistry capabilities)
    {
        var version = new Version(1, 0);
        CapabilityOperationInfo Op(string id, string description, ObservationMode mode = ObservationMode.Snapshot,
            CleanupResponsibility cleanup = CleanupResponsibility.None, double? interval = null, bool modifiesGameplay = false)
            => new(id, OperationThread.MainThread, mode, cleanup, modifiesGameplay, new[] { "*" }, description, interval);
        void Declare(string id, params CapabilityOperationInfo[] operations)
            => capabilities.DeclareMetadata(id, version, CapabilityStability.Experimental, operations);

        Declare(CapabilityIds.Catalog, Op("read", "Official chart catalog snapshot after game data is ready."));
        Declare(CapabilityIds.Scores, Op("read", "Official best scores snapshot; raw completion retains game units."));
        Declare(CapabilityIds.Ui, Op("create-panel", "Persistent simple tool UGUI panel; no input-routing API.",
            ObservationMode.NotApplicable, CleanupResponsibility.EnableScope));
        Declare(CapabilityIds.Resources, Op("override-tool-icon", "Only the tool's bounded PNG panel icon; not arbitrary game textures.",
            ObservationMode.NotApplicable, CleanupResponsibility.EnableScope));
        Declare(GameCapabilityIds.Resources,
            Op("enumerate", "Eligible live game Image.color and separately validated TMP_Text.color targets; instance IDs are session-local."),
            Op("override-color", "Priority color layers per live Image/TMP receiver; each owned baseline restores on scope release after getter read-back.",
                ObservationMode.NotApplicable, CleanupResponsibility.EnableScope));
        Declare(GameAppearanceCapabilityIds.Appearance,
            Op("describe-properties", "Build-specific game appearance roles, value types, external-write policy and evidence."),
            Op("enumerate", "Current run-bound track/note properties in the shared typed registry; missing objects return an empty result."),
            Op("override-color", "Priority RGBA overrides for one unique game role and observed run/scene generation, with scoped read-back and owned baseline restoration.",
                ObservationMode.NotApplicable, CleanupResponsibility.EnableScope));
        Declare(VisualCapabilityIds.Sprites,
            Op("resolve", "Resolve the adapter's existing eligible live UI Image sprite targets; queries do not scan the game."),
            Op("load-png", "Decode bounded RGB/RGBA8 noninterlaced PNG into a scope-owned Texture2D.", ObservationMode.NotApplicable, CleanupResponsibility.EnableScope),
            Op("create-sprite", "Create a scope-owned Sprite from this plugin's live Texture2D lease.", ObservationMode.NotApplicable, CleanupResponsibility.EnableScope),
            Op("override", "Apply a priority Sprite layer to one target generation through the common mutation coordinator.", ObservationMode.NotApplicable, CleanupResponsibility.EnableScope),
            Op("bind-override", "Maintain a same-scope Sprite override intent across verified discovery boundaries; missing and ambiguous matches wait, while provider, scope or asset withdrawal stops the intent.", ObservationMode.NotApplicable, CleanupResponsibility.EnableScope),
            Op("binding-state", "Read the current binding revision, semantic identity and target generation without discovery."));
        Declare(VisualCapabilityIds.Fonts,
            Op("resolve", "Resolve eligible live UI Text font targets from the existing adapter registry."),
            Op("load-builtin-font", "Acquire only the adapter-declared Arial.ttf or LegacyRuntime.ttf built-in font.", ObservationMode.NotApplicable, CleanupResponsibility.EnableScope),
            Op("override", "Apply a priority Font layer to one target generation through the common mutation coordinator.", ObservationMode.NotApplicable, CleanupResponsibility.EnableScope),
            Op("bind-override", "Maintain a same-scope Font intent with a fixed semantic identity; old handles never revive after target reconstruction.", ObservationMode.NotApplicable, CleanupResponsibility.EnableScope),
            Op("binding-state", "Read the last binding revision and structured waiting, ambiguity or stop reason without backend access."));
        Declare(VisualCapabilityIds.Materials,
            Op("resolve", "Resolve eligible live UI Graphic and separately verified TMP material slots."),
            Op("clone-material", "Clone a live source material; parameter changes require a verified shader schema and read-back.", ObservationMode.NotApplicable, CleanupResponsibility.EnableScope),
            Op("override", "Apply a priority Material layer to one target generation with native assignment lifetime tracking.", ObservationMode.NotApplicable, CleanupResponsibility.EnableScope),
            Op("bind-override", "Maintain a same-scope Material intent using the common coordinator; a failed old-layer release prevents acquiring a replacement.", ObservationMode.NotApplicable, CleanupResponsibility.EnableScope),
            Op("binding-state", "Read the current semantic identity, revision and target generation; an acquired layer need not win arbitration."));
        Declare(GameCapabilityIds.Scenes, Op("snapshot", "Sampled active Unity scene state."),
            Op("observe", "Scene transitions sampled on the main thread.", ObservationMode.Sampled, CleanupResponsibility.EnableScope, .25));
        Declare(GameCapabilityIds.Runs, Op("snapshot", "Sampled normal-mode state; start/finish correlation remains under gameplay validation."),
            Op("observe", "Normal-mode flags and new result objects sampled every 250 ms; short states can be missed.",
                ObservationMode.Sampled, CleanupResponsibility.EnableScope, .25));
        Declare(CapabilityIds.Extensions, Op("subscribe", "Exact type/version observation points; no argument or return-value mutation.",
            ObservationMode.NotApplicable, CleanupResponsibility.EnableScope));
        Declare(CapabilityIds.Practice);
        Declare(CapabilityIds.Metadata, Op("describe", "Current capability semantics and provider generations; does not activate services."));
        Declare(GameplayCapabilityIds.Clock,
            Op("read", "Per-reading game clock units, fixed-profile chart conversion and bounded observations of game, DSP and host clocks. Three continuous samples may describe a recent observed interval; runtime calibration and audio latency remain unverified."),
            Op("convert", "Convert chart coordinates and game progress seconds using the current run's verified formula and source identity."),
            Op("convert-observed", "Interpolate only within the latest observed game/DSP/host interval and its age limit. Pauses, changed sources and discontinuities require a new segment; no extrapolation or latency correction is implied."));
        Declare(RunModificationCapabilityIds.Read, Op("read", "Bounded read-only control/audio/content audit; only real lifecycle identities are associated. Active effects and null-RunId records are distinct; result/submission flags retain actual verification state."));
        Declare(GameplayCapabilityIds.Controls,
            Op("get-state", "Latest host control observation, with sample sequence and time. Returns NotReady before publication and TargetExpired after a known boundary."),
            Op("observe", "Atomic registration with the latest publication, followed by semantic changes after playback maintenance. Each revision includes sampled per-action support and unavailable reasons. Intermediate states can be missed.",
                ObservationMode.Sampled, CleanupResponsibility.EnableScope),
            Op("acquire-execute", "Explicit experimental, exclusive live-run control; per-action value, unit, generation and readiness guards. Each action reports its actual support; Dispose releases access and does not undo Retry/Skip.",
                ObservationMode.NotApplicable, CleanupResponsibility.EnableScope, modifiesGameplay: true),
            Op("submit-command", "Queue one command on the current control lease. Its immutable record distinguishes queued, executing and terminal outcomes. A queued command can be cancelled before host execution.",
                ObservationMode.NotApplicable, CleanupResponsibility.EnableScope, modifiesGameplay: true));
        Declare(GameSettingsCapabilityIds.Read, Op("describe-read", "Only verified static SettingsControl float getters; original game units and nullable live slider bounds."));
        Declare(GameSettingsCapabilityIds.Changes, Op("snapshot-subscribe", "Atomic initial settings snapshot and subsequent complete value/descriptor revisions; activation and provider-generation guarded. Intermediate setter calls between samples are not reported.",
            ObservationMode.Sampled, CleanupResponsibility.EnableScope, 1));
        Declare(GameSettingsCapabilityIds.Override, Op("override", "Scoped priority float overrides within actual live game Slider ranges; adopts game-owned baseline changes. Arbitrary save writes are unsupported.",
            ObservationMode.NotApplicable, CleanupResponsibility.EnableScope));
    }
}
