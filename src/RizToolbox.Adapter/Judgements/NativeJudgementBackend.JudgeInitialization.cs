using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private static INativeSeekJudgeInitializationAccess CreateJudgeInitializationAccess(NativeJudgementBackend owner,
        RunValue run, ISeekTargetNativeProfile profile) => NativeSeekNoteFlagsAccess.CreateInitializationSource(owner, run, profile);
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        internal static INativeSeekJudgeInitializationAccess CreateInitializationSource(NativeJudgementBackend owner,
            RunValue run, ISeekTargetNativeProfile profile)
        {
            try { return new InitializationAccess(owner, run, profile); }
            catch (Exception error)
            {
                if (!error.Data.Contains("SeekJudgeInitializationCleanupFailure") && error.Data["SeekNoteFlagsCleanupFailure"] is Exception cleanup)
                {
                    error.Data["SeekJudgeInitializationCleanupOwner"] = error.Data["SeekNoteFlagsCleanupOwner"];
                    error.Data["SeekJudgeInitializationCleanupFailure"] = cleanup;
                }
                throw;
            }
        }
        private sealed class InitializationAccess : INativeSeekJudgeInitializationAccess
        {
            private enum Kind { Single, Boolean, Int32 }
            private sealed record Binding(NativeRuntime.RootHandle Owner, NativeExactReadField Field, Kind Kind, int NoteOrdinal = -1);
            private readonly NativeSeekNoteFlagsAccess _core;
            private readonly ISeekTargetNativeProfile _profile;
            private readonly NativeRuntime.RootHandle _judge;
            private readonly long _judgeAddress;
            private readonly List<Binding> _bindings = new();
            private readonly IReadOnlyList<long> _references;
            private Action? _lifetime;
            private bool _withdrawn, _expired, _released;
            private Exception? _cleanup;
            internal InitializationAccess(NativeJudgementBackend owner, RunValue run, ISeekTargetNativeProfile profile)
            {
                ArgumentNullException.ThrowIfNull(profile); _profile = profile; _core = new(owner, run, profile);
                try
                {
                    _core.BindLifetimeGuard(OwnLifetime); var c = _core;
                    c.Native(() => { profile.VerifyCurrent(); return 0; });
                    if (profile.InformationClass != owner._info || profile.NoteClass != owner._note || profile.JudgeClass != owner._owner)
                        throw new UnsupportedSignatureException("Initialization profile has different exact source classes.");
                    _judge = c.TargetReference(c._information, owner._info, "noteJudgeControl", "InLevel.NoteJudgeControl", 0x58);
                    _judgeAddress = c.Target(_judge).ToInt64();
                    if (c.Native(() => owner._api.ObjectClass(c.Target(_judge))) != profile.JudgeClass)
                        throw new UnsupportedSignatureException("The retained Judge has another exact class.");
                    _references = Array.AsReadOnly(c._noteReferences.ToArray());
                    var windows = new[] { "_judgeTime", "_maxPerfectJudgeTimeInChallengeTime", "_normalPerfectJudgeTimeInChallengeTime", "badTime" };
                    for (var i = 0; i < windows.Length; ++i) Add(_judge, profile.JudgeClass, windows[i], "System.Single", 0x54 + i * 4, Kind.Single);
                    for (var i = 0; i < c._notes.Length; ++i) Add(c._notes[i], owner._note, "inChallengeTime", "System.Boolean", 0x56, Kind.Boolean, i);
                    Add(c._information, owner._info, "challengeTimeComboNum", "System.Int32", 0x94, Kind.Int32);
                    Add(_judge, profile.JudgeClass, "_instantiateReady", "System.Boolean", 0x6c, Kind.Boolean);
                    VerifyCurrent(true);
                }
                catch (Exception error)
                {
                    try { Dispose(); }
                    catch (Exception cleanup)
                    { error.Data["SeekJudgeInitializationCleanupOwner"] = this; error.Data["SeekJudgeInitializationCleanupFailure"] = cleanup; }
                    throw;
                }
            }
            private void Add(NativeRuntime.RootHandle owner, IntPtr cls, string name, string type, int offset, Kind kind, int note = -1)
            { _bindings.Add(new(owner, Mutable(cls, name, type, offset), kind, note)); }
            private NativeExactReadField Mutable(IntPtr cls, string name, string type, int offset)
            {
                var c = _core; var field = c.Native(() => c._owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset));
                if (field.IsStatic || field.DeclaringClass != cls || (field.Flags & (0x10u | 0x20u | 0x40u)) != 0
                    || field.Size != (type == "System.Boolean" ? 1 : 4))
                    throw new UnsupportedSignatureException("Initialization needs exact mutable declared scalar storage.");
                return field;
            }
            private NativeExactReadField Current(Binding binding)
            {
                var field = Mutable(binding.Field.DeclaringClass, binding.Field.Name, binding.Field.TypeName, binding.Field.Offset);
                if (field != binding.Field) { _expired = true; throw new UnsupportedSignatureException("An initialization field descriptor changed."); }
                return field;
            }
            public void VerifyOwnerThread() => _core.VerifyOwnerThread();
            private void OwnLifetime()
            {
                VerifyOwnerThread();
                if (_withdrawn) throw new ObjectDisposedException(nameof(InitializationAccess));
                if (_expired) throw new InvalidOperationException("The initialization source was observed expired.");
                _lifetime?.Invoke();
            }
            public void VerifyLocal() { OwnLifetime(); _core.VerifyLocal(); }
            public void BindLifetimeGuard(Action guard)
            { VerifyLocal(); ArgumentNullException.ThrowIfNull(guard); if (_lifetime is not null) throw new InvalidOperationException("Initialization lifetime attaches once."); _lifetime = guard; }
            public IReadOnlyList<long> References { get { VerifyLocal(); return _references; } }
            private void VerifyCurrent(bool fullGraph = false)
            {
                try
                {
                    VerifyLocal(); var c = _core;
                    c.Native(() => { _profile.VerifyCurrent(); return 0; }); c.VerifyCurrent(fullGraph);
                    if (_judge is null) return;
                    if (!c.Alive(_judge) || c.Target(_judge).ToInt64() != _judgeAddress
                        || c.ReferenceToken(c._information, c._owner._info, "noteJudgeControl", "InLevel.NoteJudgeControl", 0x58) != _judgeAddress
                        || c.ReferenceToken(_judge, _profile.JudgeClass, "levelInformation", "InLevel.LevelInformation", 0x20) != c._informationAddress)
                        throw new InvalidOperationException("The actual initialization Judge lost its source association.");
                }
                catch { _expired = true; throw; }
            }
            private int ReadOne(int slot)
            {
                if ((uint)slot >= (uint)_bindings.Count) throw new ArgumentOutOfRangeException(nameof(slot));
                var binding = _bindings[slot]; var c = _core;
                if (binding.NoteOrdinal >= 0) c.VerifyNote(binding.NoteOrdinal);
                var field = Current(binding);
                return binding.Kind switch
                {
                    Kind.Single => BitConverter.SingleToInt32Bits(c.Native(() => c._owner._api.ReadExactField<float>(binding.Owner, field))),
                    Kind.Boolean => c.Native(() => c._owner._api.ReadExactField<bool>(binding.Owner, field)) ? 1 : 0,
                    Kind.Int32 => c.Native(() => c._owner._api.ReadExactField<int>(binding.Owner, field)),
                    _ => throw new ArgumentOutOfRangeException(nameof(slot))
                };
            }
            public IReadOnlyList<int> ReadSnapshot()
            {
                VerifyCurrent(true); var first = Enumerable.Range(0, _bindings.Count).Select(ReadOne).ToArray();
                VerifyCurrent(true); var second = Enumerable.Range(0, _bindings.Count).Select(ReadOne).ToArray();
                VerifyCurrent(true);
                if (!first.SequenceEqual(second)) throw new InvalidOperationException("Complete initialization storage changed between observations.");
                return Array.AsReadOnly(second);
            }
            public int ReadSlot(int slot)
            {
                VerifyCurrent(); var first = ReadOne(slot); var second = ReadOne(slot); VerifyCurrent();
                if (first != second) throw new InvalidOperationException("Selected initialization storage changed between observations.");
                return second;
            }
            public void WriteSlot(int slot, int bits, int expectedBits, Action beforeDispatch)
            {
                ArgumentNullException.ThrowIfNull(beforeDispatch); VerifyCurrent();
                if ((uint)slot >= (uint)_bindings.Count) throw new ArgumentOutOfRangeException(nameof(slot));
                var binding = _bindings[slot]; Current(binding);
                if (ReadOne(slot) != expectedBits) throw new InvalidOperationException("The selected initialization scalar differs before its conditional store.");
                var c = _core;
                using var argument = binding.Kind switch
                {
                    Kind.Single => A.Value(BitConverter.Int32BitsToSingle(bits)),
                    Kind.Boolean when bits is 0 or 1 => A.Value(bits != 0),
                    Kind.Int32 => A.Value(bits),
                    _ => throw new InvalidDataException("An initialization Boolean target is not canonical.")
                };
                c.Native(() =>
                {
                    c._owner._api.WriteExactGuarded(binding.Owner, binding.Field.DeclaringClass, binding.Field.Name,
                        binding.Field.TypeName, argument, () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); }); return 0;
                });
            }
            public void RequestDispose() { VerifyOwnerThread(); _withdrawn = true; _core.RequestDispose(); }
            public void Dispose()
            {
                VerifyOwnerThread(); _withdrawn = true;
                if (!_released)
                { _released = true; try { _core.Dispose(); } catch (Exception error) { _cleanup = error; } }
                if (_cleanup is null) return;
                var failure = new AggregateException("Unknown initialization source cleanup is not retried.", _cleanup);
                failure.Data["SeekJudgeInitializationCleanupOwner"] = this; throw failure;
            }
        }
    }
}
