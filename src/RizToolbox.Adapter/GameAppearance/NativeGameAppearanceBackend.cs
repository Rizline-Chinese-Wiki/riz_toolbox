using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.GameAppearance;

/// <summary>Fixed game-owned paths only. No global UI renderer scan, shader-name lookup or field write exists here.</summary>
internal sealed partial class NativeGameAppearanceBackend : IGameAppearanceBackend
{
    internal const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    internal const string ValidationEvidence = "Experimental Steam 25977441/Unity 2022.3.62f2 Windows x64. Common run and pooled-object ownership are required; each note/start/end/ring property is admitted only after its own exact fields and accessors resolve. Loaded-file SHA256 and available game setter MethodInfo RVAs/bodies are checked. Ring color uses LinesGroupInformation.set_color and the actual SpriteRenderer.get_color. A missing property remains unsupported without disabling independently resolved roles. Visible changes/restoration in a running game remain unverified.";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly Func<long, long, string, long, bool> _matchesRun;
    private readonly Dictionary<string, IntPtr> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeExactReadField> _fields = new(StringComparer.Ordinal);
    private readonly IntPtr _resources, _unityObject, _scene, _sceneManager, _controller;
    private readonly List<TrackSlot> _tracks = new();
    private readonly Dictionary<string, string> _unavailable = new(StringComparer.Ordinal);
    private NativeAppearanceColorAccessors? _noteColor;
    private NativeAppearanceColorAccessors? _ringColor;
    private readonly IReadOnlyList<GameAppearanceProperty> _properties;
    private sealed record TrackSlot(string Name, string Role, string Cache, NativeAppearanceColorAccessors Accessors,
        GameAppearanceProperty Property);

    // The module owns one query service. Property-specific metadata is resolved
    // below, so an absent line accessor cannot prevent note discovery.
    internal static IReadOnlyList<AdapterSymbolRequirement> Requirements { get; } = Array.AsReadOnly(new[] {
        Field("appearance.controller.info", "", "InLevelController", "LevelInformation", "InLevel.LevelInformation"),
        Field("appearance.info.chart", "InLevel", "LevelInformation", "chart", "InLevel.Chart"),
        Field("appearance.info.progress", "InLevel", "LevelInformation", "progressControl", "InLevel.ProgressControl"),
        Field("appearance.progress.ready", "InLevel", "ProgressControl", "timeReady", "System.Boolean"),
        Field("appearance.info.loaded", "InLevel", "LevelInformation", "loadOver", "System.Boolean"),
        Field("appearance.info.over", "InLevel", "LevelInformation", "levelOver", "System.Boolean"),
        Field("appearance.info.crashed", "InLevel", "LevelInformation", "crashed", "System.Boolean"),
        Field("appearance.child.target", "InLevel", "EasyPoolChild", "targetComponent", "UnityEngine.Component"),
        Field("appearance.child.pool", "InLevel", "EasyPoolChild", "parentPool", "InLevel.EasyPool"),
        Method("appearance.scene.current", "UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "SceneManager", "GetActiveScene", "UnityEngine.SceneManagement.Scene", true),
        Method("appearance.controller.active", "UnityEngine.CoreModule.dll", "UnityEngine", "Behaviour", "get_isActiveAndEnabled", "System.Boolean"),
        Method("appearance.controllers.scan", "UnityEngine.CoreModule.dll", "UnityEngine", "Resources", "FindObjectsOfTypeAll", "UnityEngine.Object[]", true, "System.Type")
    });
    private static AdapterSymbolRequirement Field(string id, string ns, string type, string name, string value)
        => new(id, "Assembly-CSharp.dll", ns, type, AdapterSymbolKind.Field, name) { ExpectedType = value };
    private static AdapterSymbolRequirement Method(string id, string assembly, string ns, string type, string name,
        string value, bool isStatic = false, params string[] parameters)
        => new(id, assembly, ns, type, AdapterSymbolKind.Method, name, isStatic, Array.AsReadOnly(parameters)) { ExpectedType = value };

    internal NativeGameAppearanceBackend(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        Func<long, long, string, long, bool> matchesRun)
    {
        _api = api; _main = main; _log = log; _matchesRun = matchesRun ?? throw new ArgumentNullException(nameof(matchesRun)); _main.VerifyAccess();
        var module = VerifyLoadedBuild();
        _resources = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Resources");
        _unityObject = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        _scene = api.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene");
        _sceneManager = api.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "SceneManager");
        _controller = GameType("controller", "NormalModeInLevelController", "");
        var controllerBase = GameType("controllerBase", "InLevelController", "");
        var information = GameType("info", "LevelInformation");
        var progress = GameType("progress", "ProgressControl");
        _types.Add("chart", api.Class("Assembly-CSharp.dll", "InLevel", "Chart"));
        Add("controller.info", controllerBase, "LevelInformation", "InLevel.LevelInformation", 0x28);
        Add("info.chart", information, "chart", "InLevel.Chart", 0x88);
        Add("info.progress", information, "progressControl", "InLevel.ProgressControl", 0x28);
        Add("progress.ready", progress, "timeReady", "System.Boolean", 0x65);
        Add("info.over", information, "levelOver", "System.Boolean", 0xC0);
        Add("info.loaded", information, "loadOver", "System.Boolean", 0xBE);
        Add("info.crashed", information, "crashed", "System.Boolean", 0xC1);
        var child = GameType("child", "EasyPoolChild"); GameType("pool", "EasyPool");
        Add("child.target", child, "targetComponent", "UnityEngine.Component", 0x20);
        Add("child.pool", child, "parentPool", "InLevel.EasyPool", 0x28);
        if (TryBindProperty(BindLineControlSource, out var sourceFailure))
        {
            if (TryBindProperty(BindTrackSource, out var trackFailure))
            {
                foreach (var name in new[] { "start", "end" })
                    BindRole(name == "start" ? GameAppearanceRoles.TrackStartColor : GameAppearanceRoles.TrackEndColor,
                        () => BindTrackProperty(module, name));
            }
            else
            {
                Unsupported(GameAppearanceRoles.TrackStartColor, trackFailure!);
                Unsupported(GameAppearanceRoles.TrackEndColor, trackFailure!);
            }
            BindRole(GameAppearanceRoles.TrackRingColor, () => BindRings(module));
        }
        else
        {
            Unsupported(GameAppearanceRoles.TrackStartColor, sourceFailure!);
            Unsupported(GameAppearanceRoles.TrackEndColor, sourceFailure!);
            Unsupported(GameAppearanceRoles.TrackRingColor, sourceFailure!);
        }
        BindRole(GameAppearanceRoles.NoteRendererColor, BindNotes);
        _properties = Array.AsReadOnly(_tracks.Select(x => x.Property).Concat(_ringColor is null
            ? Array.Empty<GameAppearanceProperty>() : new[] { RingProperty }).Concat(_noteColor is null
            ? Array.Empty<GameAppearanceProperty>() : new[] { NoteProperty }).ToArray());
    }

    public IReadOnlyList<GameAppearanceProperty> Properties => _properties;
    public string? UnsupportedReason(string role) => _unavailable.GetValueOrDefault(role);
    private void BindRole(string role, Action bind)
    { if (!TryBindProperty(bind, out var failure)) Unsupported(role, failure!); }
    private void Unsupported(string role, string failure)
    {
        _unavailable.Add(role, failure);
        _log.Log(LogLevel.Warning, "Experimental appearance property unsupported: " + role + "; " + failure);
    }
    internal static bool TryBindProperty(Action bind, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(bind);
        try { bind(); failure = null; return true; }
        // Runtime readiness, thread and unexpected failures still propagate.
        catch (Exception error) when (error is MissingMemberException or NotSupportedException or System.Reflection.AmbiguousMatchException)
        { failure = error.Message; return false; }
    }
    private static GameAppearanceProperty NoteProperty { get; } = new(GameAppearanceRoles.NoteRendererColor, "color",
        "NormalModeInLevelController.LevelInformation.noteControl.notes -> NoteInformation.noteRenderers[] (active) -> poolChild.targetComponent (exact UnityEngine.SpriteRenderer).color",
        "Fixed dump; NoteInformation.Appear@3FCDC0 publishes EasyPool.GetItem into NoteRenderer.poolChild; EasyPoolChild.Reset@3F7C00 uses exact SpriteRenderer.set_color. Main targetComponent is admitted only when its actual metadata is SpriteRenderer; pool/node ownership is rechecked on every access. Real visible acceptance remains unverified.",
        ExternalWritePolicy.AdoptAndReapply);
    private IntPtr GameType(string key, string type, string ns = "InLevel")
    { var cls = _api.Class("Assembly-CSharp.dll", ns, type); _types.Add(key, cls); return cls; }
    private void Add(string key, IntPtr cls, string name, string value, int offset)
        => _fields.Add(key, _api.InspectExactDeclaredReadField(cls, name, value, false, offset));
    private void BindLineControlSource()
    {
        var control = GameType("lineControl", "LineControl"); GameType("group", "LinesGroupInformation");
        Add("info.lines", _types["info"], "lineControl", "InLevel.LineControl", 0x38);
        Add("lines.groups", control, "lineGroups", "InLevel.LinesGroupInformation[]", 0x30);
    }
    private void BindTrackSource()
    {
        var control = _types["lineControl"]; var group = _types["group"]; var line = GameType("line", "LineInformation");
        var plan = _api.NestedClass(line, "LineRenderer"); _types.Add("linePlan", plan);
        _types.Add("unityLine", _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "LineRenderer"));
        Add("lines.dynamic", control, "dynamicLines", "InLevel.LineInformation[]", 0x28);
        Add("group.lines", group, "lines", "InLevel.LineInformation[]", 0x68);
        Add("line.data", line, "asData", "System.Boolean", 0x28);
        Add("line.visible", line, "isVisible", "System.Boolean", 0x51);
        Add("line.plan", line, "lineRendererPlan", "InLevel.LineInformation.LineRenderer", 0x48);
        Add("linePlan.active", plan, "active", "System.Boolean", 0x28);
        Add("linePlan.child", plan, "poolChild", "InLevel.EasyPoolChild", 0x18);
        Add("linePlan.pool", plan, "pool", "InLevel.EasyPool", 0x10);
    }
    private void BindTrackProperty(IntPtr module, string name)
    {
        var line = _types["line"];
        var cache = "_" + name + "ColorPlan"; Add("line." + name, line, cache, "UnityEngine.Color", name == "start" ? 0x68 : 0x78);
        var accessors = NativeAppearanceColorAccessors.Bind(_api, _types["unityLine"], "UnityEngine", "LineRenderer", "get_" + name + "Color",
            line, "InLevel", "LineInformation", "set_" + name + "Color");
        var expectedRva = name == "start" ? 0x3FB510L : 0x3FB460L;
        var method = _api.InspectDeclaredMethod(line, "set_" + name + "Color", new[] { "UnityEngine.Color" }, "System.Void", module);
        if (method.Entry?.Rva != expectedRva) throw new UnsupportedSignatureException("Game appearance setter differs from the pinned MethodInfo RVA.");
        VerifyBody(module, checked((int)expectedRva), name == "start"
            ? "094a9a412fd933be2af67e01ce61dc32cc215718aa8487018f77d9f5b1e73c82"
            : "d2b07dbe9ab2e2198f3e2ccbb11c06457d3121648e1840e5110fce55ce260883");
        var role = name == "start" ? GameAppearanceRoles.TrackStartColor : GameAppearanceRoles.TrackEndColor;
        var property = new GameAppearanceProperty(role, "color",
            "LevelInformation.lineControl.{dynamicLines|lineGroups[].lines} -> LineInformation." + cache
            + " / set_" + name + "Color -> active lineRendererPlan.poolChild.targetComponent (exact UnityEngine.LineRenderer)." + name + "Color",
            $"Fixed setter RVA {expectedRva:X}; cached Color at +{(name == "start" ? 0x68 : 0x78):X}; native getter verifies the rendered value. LineControl.SetLineColor and LineInformation.Appear consume this property. Real visible acceptance remains unverified.",
            ExternalWritePolicy.AdoptAndReapply);
        _tracks.Add(new(name, role, "line." + name, accessors, property));
    }
    private void BindNotes()
    {
        var control = GameType("noteControl", "NoteControl"); var note = GameType("note", "NoteInformation");
        var data = GameType("noteData", "Note"); var plan = _api.NestedClass(note, "NoteRenderer"); _types.Add("notePlan", plan);
        _types.Add("sprite", _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "SpriteRenderer"));
        Add("info.notes", _types["info"], "noteControl", "InLevel.NoteControl", 0x40);
        Add("notes.array", control, "notes", "InLevel.NoteInformation[]", 0x28);
        Add("note.data", note, "noteData", "InLevel.Note", 0x28);
        Add("note.active", note, "isActive", "System.Boolean", 0x30);
        Add("note.destroyed", note, "destroyed", "System.Boolean", 0x31);
        Add("note.index", note, "_index", "System.Int32", 0x68);
        Add("note.plans", note, "noteRenderers", "InLevel.NoteInformation.NoteRenderer[]", 0x60);
        Add("noteData.type", data, "type", "System.Int32", 0x10);
        Add("notePlan.active", plan, "active", "System.Boolean", 0x30);
        Add("notePlan.child", plan, "poolChild", "InLevel.EasyPoolChild", 0x20);
        Add("notePlan.pool", plan, "pool", "InLevel.EasyPool", 0x18);
        Add("notePlan.index", plan, "poolIndex", "System.Int32", 0x10);
        _noteColor = NativeAppearanceColorAccessors.Bind(_api, _types["sprite"], "UnityEngine", "SpriteRenderer", "get_color",
            _types["sprite"], "UnityEngine", "SpriteRenderer", "set_color");
    }

    public IEnumerable<GameAppearanceCandidate> Discover(GameAppearanceRun run, int maximumScanned)
    {
        _main.VerifyAccess(); if (maximumScanned <= 0 || _properties.Count == 0) yield break;
        using var graph = CaptureRun(run);
        var budget = new ScanBudget(maximumScanned);
        var seenLines = new HashSet<IntPtr>(); var count = 0;
        // No unsupported track source is inspected while discovering notes.
        foreach (var linePath in _tracks.Count == 0 ? Enumerable.Empty<(NativeGraph Graph, int Node)>()
            : Lines(graph, graph.Reference(1, "info.lines"), budget))
        {
            using var lineGraph = linePath.Graph;
            var line = linePath.Node;
            if (!seenLines.Add(lineGraph.Pointer(line)) || lineGraph.Read<bool>(line, "line.data")
                || !lineGraph.Read<bool>(line, "line.visible")) continue;
            lineGraph.Expect(line, "line.data", false);
            var plan = lineGraph.Reference(line, "line.plan");
            if (!lineGraph.Read<bool>(plan, "linePlan.active")) continue;
            lineGraph.Expect(plan, "linePlan.active", true);
            var child = lineGraph.Reference(plan, "linePlan.child");
            var pool = lineGraph.Reference(plan, "linePlan.pool");
            lineGraph.ExpectReference(child, "child.pool", pool);
            var renderer = lineGraph.Reference(child, "child.target");
            if (_api.ObjectClass(lineGraph.Pointer(renderer)) != _types["unityLine"]) continue;
            var id = InstanceId(lineGraph.Pointer(line)); var renderId = InstanceId(lineGraph.Pointer(renderer));
            foreach (var slot in _tracks)
            {
                if (++count > maximumScanned) throw new InvalidOperationException("Appearance property count exceeds its complete-source bound.");
                var owned = lineGraph.Fork();
                try
                {
                    var captured = new NativeBinding(owned, () => Alive(owned.Pointer(line)) && Alive(owned.Pointer(renderer)),
                        () => Active(owned.Pointer(line)) && owned.Read<bool>(line, "line.visible"),
                        () => slot.Accessors.ReadTrack(_api, owned.Handle(line), _fields[slot.Cache], owned.Handle(renderer)),
                        value => slot.Accessors.WriteColor(_api, owned.Handle(line), value));
                    var alias = $"game.track/segment/{id}/{slot.Name}-color";
                    yield return Candidate(run, slot.Property, $"track/{id}/{slot.Name}-color", $"unity-line:{renderId}:{slot.Name}",
                        new[] { alias, $"game.appearance/track/{id}/{slot.Name}-color" }, captured,
                        $"Live InLevel.LineInformation {id} / pooled LineRenderer {renderId} {slot.Name}Color");
                }
                finally { owned.DisposeUnlessRetained(); }
            }
        }
        foreach (var ring in Rings(graph, run, budget))
        {
            if (++count > maximumScanned) throw new InvalidOperationException("Appearance property count exceeds its complete-source bound.");
            yield return ring;
        }
        if (_noteColor is null) yield break;
        var noteControl = graph.Reference(1, "info.notes");
        var notes = graph.Reference(noteControl, "notes.array");
        foreach (var pointer in ReadArray(graph.Pointer(notes), maximumScanned, budget))
        {
            using var noteGraph = graph.Fork(); var note = noteGraph.Member(notes, pointer);
            if (!noteGraph.Read<bool>(note, "note.active") || noteGraph.Read<bool>(note, "note.destroyed")) continue;
            noteGraph.Expect(note, "note.destroyed", false);
            var data = noteGraph.Reference(note, "note.data");
            var plans = noteGraph.Reference(note, "note.plans"); var index = noteGraph.Read<int>(note, "note.index");
            var noteType = noteGraph.Read<int>(data, "noteData.type"); var rendererIndex = 0;
            foreach (var planPointer in ReadArray(noteGraph.Pointer(plans), 32, budget))
            {
                var slotIndex = rendererIndex++;
                using var planGraph = noteGraph.Fork(); var plan = planGraph.Member(plans, planPointer);
                if (!planGraph.Read<bool>(plan, "notePlan.active")) continue;
                planGraph.Expect(plan, "notePlan.active", true);
                var child = planGraph.Reference(plan, "notePlan.child"); var pool = planGraph.Reference(plan, "notePlan.pool");
                planGraph.ExpectReference(child, "child.pool", pool); var renderer = planGraph.Reference(child, "child.target");
                if (_api.ObjectClass(planGraph.Pointer(renderer)) != _types["sprite"]) continue;
                if (++count > maximumScanned) throw new InvalidOperationException("Appearance property count exceeds its complete-source bound.");
                var owned = planGraph.Fork();
                try
                {
                    var color = _noteColor!; var id = InstanceId(owned.Pointer(note)); var renderId = InstanceId(owned.Pointer(renderer));
                    var poolIndex = owned.Read<int>(plan, "notePlan.index");
                    var captured = new NativeBinding(owned, () => Alive(owned.Pointer(note)) && Alive(owned.Pointer(renderer)),
                        () => Active(owned.Pointer(note)) && owned.Read<bool>(note, "note.active")
                            && Active(owned.Pointer(renderer)),
                        () => color.ReadRendered(_api, owned.Handle(renderer)),
                        value => color.WriteColor(_api, owned.Handle(renderer), value));
                    yield return Candidate(run, NoteProperty, $"note/{id}/pool/{poolIndex}/renderer/{renderId}/color", $"sprite:{renderId}:color",
                        new[] { $"game.note/{index}/pool/{poolIndex}/color", $"game.appearance/note/{id}/renderer/{renderId}/color" }, captured,
                        $"Live NoteInformation {id} (index={index}, rawType={noteType}) renderer={slotIndex}, poolIndex={poolIndex}, SpriteRenderer={renderId}");
                }
                finally { owned.DisposeUnlessRetained(); }
            }
        }
    }

    private GameAppearanceCandidate Candidate(GameAppearanceRun run, GameAppearanceProperty property, string key,
        string physical, string[] aliases, NativeBinding binding, string description)
        => new(run.RunId + ":" + run.SceneGeneration + ":" + key + ":" + physical, "game.appearance/" + key, physical, run,
            new(Scene: run.Scene, Role: property.Role, ContentId: run.ChartId, ResourceId: property.Role,
                Description: description), property, aliases, binding.Read, binding.Write, binding.Current, binding.Eligible, binding.Retain);

    private NativeGraph CaptureRun(GameAppearanceRun run)
    {
        if (!CurrentScene(run.Scene)) throw new TargetExpiredException("The active scene differs from the observed appearance run.");
        var candidates = new List<NativeRuntime.RootHandle>();
        try
        {
            using var array = _api.Root(_api.CallStatic(_resources, "FindObjectsOfTypeAll", A.Ref(_api.Type(_controller))));
            foreach (var pointer in ReadArray(array.Target, 64))
            {
                var root = _api.Root(pointer);
                try
                {
                    if (_api.ObjectClass(root.Target) == _controller && Loaded(root.Target) && ActiveController(root.Target))
                    { candidates.Add(root); root = null!; }
                }
                finally { root?.Dispose(); }
            }
            if (candidates.Count != 1) throw new InvalidOperationException(candidates.Count == 0
                ? "TargetMissing: no active current NormalMode controller." : "AmbiguousTarget: multiple active current NormalMode controllers.");
            var graph = new NativeGraph(this);
            try
            {
                var controller = graph.Object(candidates[0].Target);
                var information = graph.Reference(controller, "controller.info");
                if (!_matchesRun(graph.Pointer(information).ToInt64(), graph.Pointer(controller).ToInt64(), run.RunId, run.SceneGeneration))
                    throw new TargetExpiredException("The concrete controller/information graph does not belong to the currently observed run.");
                graph.Run = run;
                graph.Reference(information, "info.chart");
                var progress = graph.Reference(information, "info.progress");
                // Existing real-play evidence shows levelBegan can remain false. The host's
                // observed run/Playing-or-Paused phase owns identity; native readiness uses
                // the actual loaded, nonterminal progress source. Paused appearance is allowed.
                if (!graph.Read<bool>(information, "info.loaded") || graph.Read<bool>(information, "info.over")
                    || graph.Read<bool>(information, "info.crashed") || !graph.Read<bool>(progress, "progress.ready"))
                    throw new InvalidOperationException("TargetMissing: the concrete game run/progress is not ready or is terminal.");
                graph.Expect(information, "info.over", false); graph.Expect(information, "info.crashed", false);
                graph.Expect(information, "info.loaded", true); graph.Expect(progress, "progress.ready", true);
                graph.Scene = run.Scene; return graph;
            }
            catch { graph.Dispose(); throw; }
        }
        finally { foreach (var root in candidates) root.Dispose(); }
    }

    private IEnumerable<(NativeGraph Graph, int Node)> Lines(NativeGraph graph, int control, ScanBudget budget)
    {
        var dynamic = graph.Reference(control, "lines.dynamic");
        foreach (var pointer in ReadArray(graph.Pointer(dynamic), GameAppearanceService.MaximumScanned, budget))
        {
            var clone = graph.Fork(); int line;
            try { line = clone.Member(dynamic, pointer); }
            catch { clone.Dispose(); throw; }
            yield return (clone, line);
        }
        var groups = graph.Reference(control, "lines.groups");
        foreach (var pointer in ReadArray(graph.Pointer(groups), GameAppearanceService.MaximumScanned, budget))
        {
            using var group = graph.Fork(); var node = group.Member(groups, pointer); var lines = group.Reference(node, "group.lines");
            foreach (var linePointer in ReadArray(group.Pointer(lines), GameAppearanceService.MaximumScanned, budget))
            {
                var clone = group.Fork(); int line;
                try { line = clone.Member(lines, linePointer); }
                catch { clone.Dispose(); throw; }
                yield return (clone, line);
            }
        }
    }
    private IEnumerable<IntPtr> ReadArray(IntPtr pointer, int maximum, ScanBudget? budget = null)
    {
        var length = _api.Unbox<int>(_api.Call(pointer, "get_Length"));
        if (length < 0 || length > maximum) throw new InvalidOperationException($"QuotaExceeded: appearance source array length {length} exceeds its complete-source bound {maximum}; partial uniqueness is not accepted.");
        budget?.Consume(length);
        foreach (var item in _api.ArrayItems(pointer)) if (item != IntPtr.Zero) yield return item;
    }
    private sealed class ScanBudget
    {
        private int _remaining;
        internal ScanBudget(int maximum) => _remaining = maximum;
        internal void Consume(int count)
        {
            if (count > _remaining) throw new InvalidOperationException("QuotaExceeded: complete game appearance graph exceeds the bounded discovery source.");
            _remaining -= count;
        }
    }
    private bool Alive(IntPtr value) => value != IntPtr.Zero && _api.Unbox<bool>(_api.CallStatic(_unityObject, "op_Implicit", A.Ref(value)));
    private bool Active(IntPtr value)
    {
        if (!Alive(value)) return false;
        using var go = _api.Root(_api.Call(value, "get_gameObject"));
        return _api.Unbox<bool>(_api.Call(go.Target, "get_activeInHierarchy"));
    }
    private bool ActiveController(IntPtr value)
        => Active(value) && _api.Unbox<bool>(_api.Call(value, "get_isActiveAndEnabled"));
    private bool Loaded(IntPtr value)
    {
        if (!Alive(value)) return false;
        using var go = _api.Root(_api.Call(value, "get_gameObject"));
        using var sceneValue = _api.Root(_api.Call(go.Target, "get_scene"));
        var handle = _api.Unbox<int>(sceneValue.Target);
        return _api.Unbox<bool>(_api.CallStatic(_scene, "IsValidInternal", A.Value(handle)))
            && _api.Unbox<bool>(_api.CallStatic(_scene, "GetIsLoadedInternal", A.Value(handle)));
    }
    private bool CurrentScene(string expected)
    {
        using var sceneValue = _api.Root(_api.CallStatic(_sceneManager, "GetActiveScene"));
        var handle = _api.Unbox<int>(sceneValue.Target);
        return _api.Unbox<bool>(_api.CallStatic(_scene, "IsValidInternal", A.Value(handle)))
            && _api.Unbox<bool>(_api.CallStatic(_scene, "GetIsLoadedInternal", A.Value(handle)))
            && _api.Text(_api.CallStatic(_scene, "GetNameInternal", A.Value(handle))) == expected;
    }
    private int InstanceId(IntPtr value) => _api.Unbox<int>(_api.Call(value, "GetInstanceID"));

    /// <summary>Roots an exact reference graph. Reordering preserves membership; replacing an array or pool child expires the old binding.</summary>
    private sealed class NativeGraph : IDisposable
    {
        private readonly NativeGameAppearanceBackend _owner;
        private readonly List<(NativeRuntime.RootHandle Root, IntPtr Class)> _nodes = new();
        private readonly List<(int Owner, string Field, int Target)> _links = new();
        private readonly List<(int Array, int Item)> _members = new();
        private readonly List<(int Node, string Field, bool Expected)> _flags = new();
        private bool _retained, _disposed;
        internal string Scene = "";
        internal GameAppearanceRun? Run;
        internal NativeGraph(NativeGameAppearanceBackend owner) => _owner = owner;
        internal IntPtr Pointer(int node) => _nodes[node].Root.Target;
        internal NativeRuntime.RootHandle Handle(int node) => _nodes[node].Root;
        internal int Object(IntPtr pointer)
        {
            var root = _owner._api.Root(pointer);
            try { _nodes.Add((root, _owner._api.ObjectClass(root.Target))); return _nodes.Count - 1; }
            catch { root.Dispose(); throw; }
        }
        internal int Reference(int owner, string field)
        {
            NativeRuntime.RootHandle? root = null;
            _owner._api.PublishExactReferenceField(_nodes[owner].Root, _owner._fields[field], pointer =>
            { if (pointer != IntPtr.Zero) root = _owner._api.Root(pointer); });
            if (root is null) throw new TargetExpiredException("Appearance graph reference is null: " + field);
            try
            {
                var node = _nodes.Count; _nodes.Add((root, _owner._api.ObjectClass(root.Target)));
                _links.Add((owner, field, node)); return node;
            }
            catch { root.Dispose(); throw; }
        }
        internal int Member(int array, IntPtr pointer) { var node = Object(pointer); _members.Add((array, node)); return node; }
        internal void ExpectReference(int owner, string field, int expected)
        {
            var actual = Reference(owner, field);
            if (Pointer(actual) != Pointer(expected)) throw new TargetExpiredException("Appearance pool belongs to another owner.");
        }
        internal T Read<T>(int node, string field) where T : struct => _owner._api.ReadExactField<T>(_nodes[node].Root, _owner._fields[field]);
        internal void Expect(int node, string field, bool expected) => _flags.Add((node, field, expected));
        internal NativeGraph Fork()
        {
            var clone = new NativeGraph(_owner) { Scene = Scene, Run = Run };
            try
            {
                foreach (var node in _nodes) clone.Object(node.Root.Target);
                clone._links.AddRange(_links); clone._members.AddRange(_members); clone._flags.AddRange(_flags); return clone;
            }
            catch { clone.Dispose(); throw; }
        }
        internal bool Current()
        {
            if (_disposed) return false;
            if (Run is null || !_owner._matchesRun(Pointer(1).ToInt64(), Pointer(0).ToInt64(), Run.RunId, Run.SceneGeneration)) return false;
            foreach (var node in _nodes)
                if (_owner._api.ObjectClass(node.Root.Target) != node.Class) return false;
            if (!_owner.CurrentScene(Scene) || !_owner.Loaded(Pointer(0)) || !_owner.ActiveController(Pointer(0))) return false;
            foreach (var link in _links)
            {
                NativeRuntime.RootHandle? actual = null;
                try
                {
                    _owner._api.PublishExactReferenceField(_nodes[link.Owner].Root, _owner._fields[link.Field], pointer =>
                    { if (pointer != IntPtr.Zero) actual = _owner._api.Root(pointer); });
                    if (actual is null || actual.Target != Pointer(link.Target)) return false;
                }
                finally { actual?.Dispose(); }
            }
            foreach (var membership in _members)
                if (!_owner.ReadArray(Pointer(membership.Array), GameAppearanceService.MaximumScanned).Contains(Pointer(membership.Item))) return false;
            return _flags.All(flag => Read<bool>(flag.Node, flag.Field) == flag.Expected);
        }
        internal IDisposable Retain()
        { if (_retained || _disposed) throw new InvalidOperationException("Appearance graph roots already transferred or released."); _retained = true; return this; }
        internal void DisposeUnlessRetained() { if (!_retained) Dispose(); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            foreach (var node in _nodes.AsEnumerable().Reverse()) node.Root.Dispose(); _nodes.Clear();
        }
    }
    private sealed class NativeBinding
    {
        private readonly NativeGraph _graph; private readonly Func<bool> _alive, _eligible;
        private readonly Func<Rgba> _read; private readonly Action<Rgba> _write;
        internal NativeBinding(NativeGraph graph, Func<bool> alive, Func<bool> eligible, Func<Rgba> read, Action<Rgba> write)
        { _graph = graph; _alive = alive; _eligible = eligible; _read = read; _write = write; }
        internal bool Current() { try { return _graph.Current() && _alive(); } catch { return false; } }
        internal bool Eligible() => Current() && _eligible();
        private void Require() { if (!Current()) throw new TargetExpiredException("Concrete appearance run/object/pool ownership has changed."); }
        internal ResourceColor Read()
        { Require(); var value = _read(); return new(value.R, value.G, value.B, value.Alpha); }
        internal void Write(ResourceColor value)
        { Require(); _write(new Rgba(value.R, value.G, value.B, value.A)); }
        internal IDisposable Retain() => _graph.Retain();
    }

    private static IntPtr VerifyLoadedBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Game appearance supports the fixed Windows x64 build only.");
        var module = GetModuleHandle("GameAssembly.dll");
        if (module == IntPtr.Zero) throw new UnsupportedSignatureException("GameAssembly is not loaded.");
        var path = new StringBuilder(32768);
        if (GetModuleFileName(module, path, path.Capacity) == 0) throw new UnsupportedSignatureException("Loaded GameAssembly path is unavailable.");
        using var stream = File.OpenRead(path.ToString());
        using var hasher = SHA256.Create();
        if (Convert.ToHexString(hasher.ComputeHash(stream)).ToLowerInvariant() != Fingerprint)
            throw new UnsupportedSignatureException("Loaded GameAssembly file differs from the pinned appearance fingerprint.");
        return module;
    }
    private static void VerifyBody(IntPtr module, int rva, string expected)
    {
        var bytes = new byte[176]; Marshal.Copy(IntPtr.Add(module, rva), bytes, 0, bytes.Length);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expected)
            throw new UnsupportedSignatureException("Loaded game appearance setter body differs from the fixed read-only evidence.");
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern int GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
