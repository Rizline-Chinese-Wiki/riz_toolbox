using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenBackend
{
    private NativeTweenSource? _sourceJournal;
    private void InitializeSourceJournal(NativeLocalTweenWorkOwner owner)
    {
        RequireKnown();
        if (!ReferenceEquals(owner, _owner) || _sourceJournal is not null)
            throw new InvalidOperationException("Only this retained backend may bind its source journal once.");
        // These objects already have independent graph roots. A journal row
        // cannot introduce a raw object and ask this path to retain it later.
        var metadata = _api.InspectTweenSourcesMetadata(_module);
        _sourceJournal = _api.BindTweenSources();
        if (!_sourceJournal.Production || _sourceJournal.Unknown)
            throw new InvalidOperationException("The backend has no actual complete source publication.");
        var references = new List<NativeTweenSourceRootReference>();
        foreach (var root in _roots.Where(root => root is not null))
        {
            var value = Native(() => root!.Target); var cls = ObjectClass(value);
            NativeTweenSourceObjectKind? role = cls == metadata.ManagerClass ? NativeTweenSourceObjectKind.Manager
                : cls == metadata.TweenClass ? NativeTweenSourceObjectKind.Tween
                : cls == metadata.Playing.FieldClass ? NativeTweenSourceObjectKind.TweenArray
                : cls == metadata.Scheduled.FieldClass ? NativeTweenSourceObjectKind.ScheduledList
                : cls == metadata.Ready.FieldClass ? NativeTweenSourceObjectKind.ReadyStack
                : cls == metadata.OnStart.FieldClass ? NativeTweenSourceObjectKind.UnityEvent
                : cls == metadata.OnTween.FieldClass ? NativeTweenSourceObjectKind.SingleUnityEvent : null;
            // The shared manager graph also retains foreign UI tweens. Their
            // roots keep borrowed references safe without granting this run
            // ownership of their Play/Execute journal records.
            if (role is { } actual) references.Add(new(root!, actual,
                actual != NativeTweenSourceObjectKind.Tween || owner.OwnsTweenSource(value.ToInt64())));
        }
        if (references.Count == 0) throw new InvalidOperationException("No actual graph roots were retained before source binding.");
        var roots = _sourceJournal.RetainRootedObjects(owner, references);
        owner.AttachSourceJournal(_sourceJournal, roots); RequireKnown();
    }
}
