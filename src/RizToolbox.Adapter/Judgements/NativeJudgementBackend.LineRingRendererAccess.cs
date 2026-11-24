using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            // No new reader, child lease or source table. Renderer references
            // join the same deduplicated roots as the original endpoint writer.
            private sealed partial class LineRingReader : IRetainedSeekLineRendererAccess
            {
                private ISeekLineSetPositionsNativeProfile? _rendererProfile;
                private NativeLineRendererClasses? _rendererClasses;
                private long[] _rendererLines = Array.Empty<long>();
                private readonly Dictionary<(IntPtr, string), NativeExactReadField> _rendererFields = new();

                public void BindRendererProfile(ISeekLineSetPositionsNativeProfile profile,
                    IReadOnlyList<long> completeLines)
                {
                    VerifyLocal(); ArgumentNullException.ThrowIfNull(profile);
                    ArgumentNullException.ThrowIfNull(completeLines);
                    if (_rendererProfile is not null)
                        throw new InvalidOperationException("The original reader binds one renderer profile only.");
                    var actual = ReadSnapshot().Lines.Select(row => row.Reference).ToArray();
                    var owned = completeLines.ToArray(); VerifyLocal();
                    if (owned.Length > NativeSeekLineRingSourceCapture.MaximumLines
                        || owned.Any(value => value == 0) || owned.Distinct().Count() != owned.Length
                        || !owned.SequenceEqual(actual))
                        throw new InvalidOperationException("The renderer saved-state domain must include every original physical Line.");
                    N(() => { profile.VerifyCurrent(VerifyLocal); return 0; });
                    var classes = N(() => _source._core._owner._api.InspectLineRendererClasses(VerifyLocal));
                    var scratch = Field(_classes[3], "linePositions", "UnityEngine.Vector3[]", 0x78);
                    if (classes.Line != _classes[4] || classes.Transform != _classes[7]
                        || N(() => profile.LineClass) != classes.Line
                        || N(() => profile.Vector3ArrayClass) != scratch.FieldClass)
                        throw new UnsupportedSignatureException("The renderer profile differs from the retained Line/scratch source.");
                    _rendererClasses = classes; _rendererLines = owned; _rendererProfile = profile;
                    VerifyRendererProfile();
                }

                private void VerifyRendererProfile()
                {
                    VerifySource();
                    var profile = _rendererProfile ?? throw new InvalidOperationException("Bind the original renderer profile first.");
                    N(() => { profile.VerifyCurrent(VerifyLocal); return 0; });
                    var classes = N(() => _source._core._owner._api.InspectLineRendererClasses(VerifyLocal));
                    if (classes != _rendererClasses || N(() => profile.LineClass) != _classes[4]
                        || N(() => profile.Vector3ArrayClass) != Field(_classes[3], "linePositions", "UnityEngine.Vector3[]", 0x78).FieldClass)
                        throw Changed("The original renderer profile class identities changed.");
                    foreach (var pair in _rendererFields.ToArray())
                        if (RendererField(pair.Key.Item1, pair.Key.Item2) != pair.Value)
                            throw Changed("A saved renderer descriptor changed.");
                }

                private NativeExactReadField RendererField(IntPtr cls, string name)
                {
                    var current = N(() => _source._core._owner._api.InspectLineRendererField(cls, name, VerifyLocal));
                    var key = (cls, name);
                    if (_rendererFields.TryGetValue(key, out var expected))
                    {
                        if (current != expected) throw Changed("A saved renderer descriptor changed: " + name);
                    }
                    else _rendererFields.Add(key, current);
                    return current;
                }

                private NativeRuntime.RootHandle? RendererReference(NativeRuntime.RootHandle receiver,
                    IntPtr cls, string name, IntPtr? actualClass = null)
                {
                    var field = RendererField(cls, name);
                    NativeRuntime.RootHandle? result = null; var published = false;
                    N(() =>
                    {
                        _source._core._owner._api.PublishLineRendererReference(receiver, field, pointer =>
                        {
                            if (published) throw Changed("A renderer field published twice.");
                            published = true;
                            // Registration precedes native class inspection and
                            // the publisher's post-publication withdrawal guard.
                            if (pointer != IntPtr.Zero) result = AdoptPublication(pointer);
                        }, VerifyLocal); return 0;
                    });
                    if (!published) throw Changed("A renderer reference was not published.");
                    if (result is not null
                        && N(() => _source._core._owner._api.ObjectClass(_source.Target(result))) != (actualClass ?? field.FieldClass))
                        throw new UnsupportedSignatureException("A saved renderer reference has another exact class: " + name);
                    return result;
                }

                private T RendererScalar<T>(NativeRuntime.RootHandle receiver, IntPtr cls, string name) where T : struct
                    => N(() => _source._core._owner._api.ReadLineRendererField<T>(receiver, RendererField(cls, name), VerifyLocal));

                private SeekLineRendererSavedState[] ReadRendererOnce()
                {
                    var classes = _rendererClasses ?? throw new InvalidOperationException("The renderer classes are not bound.");
                    var arrayClass = _rendererProfile!.Vector3ArrayClass;
                    var arrays = new Dictionary<long, int>();
                    var result = new SeekLineRendererSavedState[_rendererLines.Length];
                    for (var i = 0; i < result.Length; ++i)
                    {
                        var line = Root(_rendererLines[i]);
                        var saved = RendererReference(line, classes.Line, "_positionsPlan");
                        var savedAddress = Address(saved);
                        var count = RendererScalar<int>(line, classes.Line, "_positionCountPlan");
                        int? length = null;
                        if (saved is not null)
                        {
                            if (!arrays.TryGetValue(savedAddress, out var actualLength))
                            {
                                var observed = N(() => _source._core._owner._api.ReadRootedLineVector3Buffer(
                                    saved, arrayClass, MaximumOriginalLinePositions, VerifyLocal));
                                if (observed.Array.ToInt64() != savedAddress || observed.Class != arrayClass)
                                    throw Changed("An original saved renderer array changed identity.");
                                arrays.Add(savedAddress, actualLength = observed.Length);
                            }
                            length = actualLength;
                        }
                        // Observe the raw saved Int32 even while reversing the
                        // generated count-then-reference order. Baseline bounds
                        // are admitted by the owner before the first effect.
                        var plan = RendererReference(line, classes.Line, "lineRendererPlan");
                        NativeRuntime.RootHandle? pool = null, child = null, transform = null, component = null, childPool = null, targetTransform = null;
                        var active = false;
                        if (plan is not null)
                        {
                            pool = RendererReference(plan, classes.Plan, "pool");
                            child = RendererReference(plan, classes.Plan, "poolChild");
                            transform = RendererReference(plan, classes.Plan, "poolChildTransformPlan");
                            active = RendererScalar<bool>(plan, classes.Plan, "active");
                            if (transform is not null) VerifyTransform(transform);
                            if (pool is not null && !Alive(pool)) throw Changed("The retained renderer pool died.");
                            if (child is not null)
                            {
                                if (!Alive(child)) throw Changed("The retained renderer child died.");
                                component = RendererReference(child, classes.Child, "targetComponent", classes.Renderer);
                                childPool = RendererReference(child, classes.Child, "parentPool");
                                if (component is not null)
                                {
                                    if (!Alive(component)) throw Changed("The retained Unity LineRenderer died.");
                                    targetTransform = TransformReference("get_transform", component, required: true);
                                    VerifyTransform(targetTransform!);
                                }
                            }
                        }
                        result[i] = new(_rendererLines[i], savedAddress, count, length, Address(plan),
                            Address(pool), Address(child), Address(transform), active,
                            Address(component), Address(childPool), Address(targetTransform));
                    }
                    VerifyLocal(); return result;
                }

                public IReadOnlyList<SeekLineRendererSavedState> ReadRendererSnapshot()
                {
                    VerifyRendererProfile();
                    var first = ReadRendererOnce(); var second = ReadRendererOnce();
                    if (!first.SequenceEqual(second))
                        throw new InvalidOperationException("The complete saved renderer fields/consumer associations changed between observations.");
                    VerifyRendererProfile();
                    return Array.AsReadOnly(second);
                }

                public void VerifyAppliedCanvasInOperation(NativeSeekCanvasTargetPlan plan)
                { VerifyLocal(); _basis.VerifyAppliedForLineInOperation(plan); VerifyLocal(); }

                public NativeVector3BufferWriteObservation WriteRendererPrefix(NativeRootedVector3Buffer expected,
                    IReadOnlyList<NativeVector3BufferBits> prefix, Action verifyExpected, Action beforeStore)
                    => WriteOriginalLinePositionsPrefixCore(expected, prefix, beforeStore, verifyExpected);

                public void InvokeRendererSetPositions(long line, int count, Action verifyExpected, Action beforeEntry)
                {
                    ArgumentNullException.ThrowIfNull(verifyExpected); ArgumentNullException.ThrowIfNull(beforeEntry);
                    VerifyRendererProfile();
                    if (!_rendererLines.Contains(line)) throw new InvalidOperationException("The renderer line is outside the complete original domain.");
                    var receiver = Root(line); var scratch = OriginalLinePositions(); var returned = false;
                    void Source() { VerifyRendererProfile(); VerifyLinePositionsAssociation(); verifyExpected(); VerifyLocal(); }
                    try
                    {
                        N(() =>
                        {
                            _rendererProfile!.InvokeExact(receiver, scratch, count, VerifyLocal, Source, beforeEntry);
                            returned = true; return 0;
                        });
                        VerifyRendererProfile(); VerifyLinePositionsAssociation();
                    }
                    catch (Exception error)
                    {
                        if (returned)
                        {
                            error.Data["LineSetPositionsOriginalLineRoot"] = receiver;
                            error.Data["LineSetPositionsOriginalArrayRoot"] = scratch;
                            error.Data["LineSetPositionsPositionCount"] = count;
                            error.Data["LineSetPositionsNativeEntered"] = true;
                            error.Data["LineSetPositionsReturned"] = true;
                        }
                        throw;
                    }
                }

                public void RestoreRendererField(long line, bool array, long expected, long target,
                    Action verifyExpected, Action beforeStore)
                {
                    ArgumentNullException.ThrowIfNull(verifyExpected); ArgumentNullException.ThrowIfNull(beforeStore);
                    VerifyRendererProfile();
                    if (!_rendererLines.Contains(line)) throw new InvalidOperationException("The saved-field inverse names a foreign Line.");
                    var receiver = Root(line);
                    var name = array ? "_positionsPlan" : "_positionCountPlan";
                    var field = RendererField(_classes[4], name); var returned = false;
                    var before = array && expected != 0 ? Root(expected) : null;
                    var after = array && target != 0 ? Root(target) : null;
                    void Source() { VerifyRendererProfile(); VerifyLinePositionsAssociation(); verifyExpected(); VerifyLocal(); }
                    try
                    {
                        N(() =>
                        {
                            _source._core._owner._api.WriteLineRendererSavedField(receiver, field, expected, target,
                                before, after, VerifyLocal, Source, beforeStore);
                            returned = true; return 0;
                        });
                        VerifyRendererProfile(); VerifyLinePositionsAssociation();
                    }
                    catch (Exception error)
                    {
                        if (returned)
                        {
                            error.Data["LineRendererSavedFieldStoreEntered"] = true;
                            error.Data["LineRendererSavedFieldOwner"] = receiver;
                            error.Data["LineRendererSavedFieldName"] = name;
                            error.Data["LineRendererSavedFieldExpected"] = expected;
                            error.Data["LineRendererSavedFieldTarget"] = target;
                        }
                        throw;
                    }
                }
            }
        }
    }
}
