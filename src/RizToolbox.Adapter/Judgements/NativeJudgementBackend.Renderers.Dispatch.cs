using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekRendererAccess
    {
        public void Dispatch(int ordinal, SeekRendererCommandKind command, SeekRendererGraphSnapshot expected, Action beforeDispatch)
        {
            ArgumentNullException.ThrowIfNull(beforeDispatch);
            if ((uint)ordinal >= (uint)_notes.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
            if (command is not (SeekRendererCommandKind.Appear or SeekRendererCommandKind.Disappear))
                throw new ArgumentOutOfRangeException(nameof(command), "Preserve has no native dispatch.");
            VerifyReady(); Native(() => { _profile.VerifyCurrent(); return 0; });
            if (!SeekRendererState.Same(ReadSnapshot(ordinal), expected))
                throw new InvalidOperationException("The complete selected renderer/pool source changed before official native dispatch.");
            var method = Native(() => command == SeekRendererCommandKind.Appear ? _profile.AppearMethod : _profile.DisappearMethod);
            var name = command == SeekRendererCommandKind.Appear ? "Appear" : "Disappear";
            var declared = Native(() => _owner._api.InspectDeclaredMethod(_owner._note, name, Array.Empty<string>(), "System.Void", IntPtr.Zero));
            if (declared.MethodInfo != method || declared.DeclaringClass.Class != _owner._note
                || declared.GenericMethod || declared.InflatedMethod
                || (declared.Flags & 0x10u) != 0 || declared.ReturnType != "System.Void")
                throw new InvalidOperationException("The actual renderer command differs from its exact fixed-profile method.");
            VerifyRendererNote(ordinal); var receiver = Target(_notes[ordinal]);
            Native(() =>
            {
                _owner._api.InvokeVoidWithPublicationGuarded(method, receiver, () =>
                {
                    // All native metadata/root/marshal work precedes this
                    // guard. Record entry last; no native getter or second
                    // lifecycle check follows the caller's entry callback.
                    VerifyLocal(); beforeDispatch();
                });
                return 0;
            });
        }
    }
}
