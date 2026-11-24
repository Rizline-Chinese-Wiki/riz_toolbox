using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Judgements;

internal enum JudgementNoteSource { RbxRdi, R14Rbx, TapList, DragList, HoldCoroutine }
internal sealed record JudgementCallSite(uint ReturnRva, string Id, JudgementNoteSource NoteSource,
    JudgementDeltaKind DeltaKind, float? Sentinel = null);
internal readonly record struct JudgementNoteSourceValue(long Owner, long Note);
internal interface IJudgementNoteSourceReader
{
    void RequireOwner(long owner);
    void RequireNote(long note);
    long ReadWaitingNote(long owner, bool drag, int index);
    long ReadHoldNote(long expectedOwner);
}
internal static class JudgementSourceResolver
{
    internal static readonly IReadOnlyList<JudgementCallSite> Sites = Array.AsReadOnly(new[] {
        new JudgementCallSite(0x3fd730,"auto.normal",JudgementNoteSource.RbxRdi,JudgementDeltaKind.MeasuredTimeDifference),
        new JudgementCallSite(0x3fd764,"auto.challenge-max",JudgementNoteSource.RbxRdi,JudgementDeltaKind.MeasuredTimeDifference),
        new JudgementCallSite(0x3fe18b,"tap-hold.timeout-normal",JudgementNoteSource.TapList,JudgementDeltaKind.Sentinel,1),
        new JudgementCallSite(0x3fe26a,"tap-hold.timeout-challenge",JudgementNoteSource.TapList,JudgementDeltaKind.Sentinel,1),
        new JudgementCallSite(0x3fe378,"drag.timeout",JudgementNoteSource.DragList,JudgementDeltaKind.Sentinel,-2),
        new JudgementCallSite(0x3fea0d,"tap-hold.head-max",JudgementNoteSource.R14Rbx,JudgementDeltaKind.MeasuredTimeDifference),
        new JudgementCallSite(0x3fef8f,"tap-hold.bad",JudgementNoteSource.R14Rbx,JudgementDeltaKind.Sentinel,-1),
        new JudgementCallSite(0x3ff2a8,"tap-hold.head",JudgementNoteSource.R14Rbx,JudgementDeltaKind.MeasuredTimeDifference),
        new JudgementCallSite(0x3ff56e,"drag.success-challenge",JudgementNoteSource.DragList,JudgementDeltaKind.Sentinel,-2),
        new JudgementCallSite(0x3ff741,"drag.success-normal",JudgementNoteSource.DragList,JudgementDeltaKind.Sentinel,-2),
        new JudgementCallSite(0x402cdc,"hold.continuous-miss",JudgementNoteSource.HoldCoroutine,JudgementDeltaKind.Sentinel,1),
        new JudgementCallSite(0x40338d,"hold.end",JudgementNoteSource.HoldCoroutine,JudgementDeltaKind.Sentinel,-2)
    });
    internal static JudgementCallSite? Find(ulong returnAddress, ulong moduleBase)
    {
        if (moduleBase == 0 || returnAddress < moduleBase || returnAddress - moduleBase > uint.MaxValue) return null;
        var rva = (uint)(returnAddress - moduleBase);
        return Sites.FirstOrDefault(x => x.ReturnRva == rva);
    }
    internal static JudgementNoteSourceValue Resolve(JudgementCallSite site, NativeJudgeFrame frame, IJudgementNoteSourceReader reader)
    {
        if (!Sites.Contains(site) || !float.IsFinite(frame.Delta)
            || site.Sentinel is float sentinel && BitConverter.SingleToInt32Bits(sentinel) != BitConverter.SingleToInt32Bits(frame.Delta))
            throw new InvalidOperationException("The caller or raw delta differs from the pinned judgement source.");
        var owner = unchecked((long)(site.NoteSource is JudgementNoteSource.RbxRdi or JudgementNoteSource.HoldCoroutine ? frame.Rdi : frame.Rbx));
        reader.RequireOwner(owner);
        var note = site.NoteSource switch {
            JudgementNoteSource.RbxRdi => unchecked((long)frame.Rbx),
            JudgementNoteSource.R14Rbx => unchecked((long)frame.R14),
            JudgementNoteSource.TapList => reader.ReadWaitingNote(owner,false,unchecked((int)frame.Rdi)),
            JudgementNoteSource.DragList => reader.ReadWaitingNote(owner,true,unchecked((int)frame.Rdi)),
            // RSI is overwritten with Vector3.Z bits by the challenge-tail VFX
            // path at RVA 4030AA. Only the retained MoveNext receiver is valid.
            JudgementNoteSource.HoldCoroutine => reader.ReadHoldNote(owner),
            _ => throw new InvalidOperationException("Unknown native note source.") };
        reader.RequireNote(note); return new(owner,note);
    }
}
