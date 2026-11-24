using RizToolbox.Adapter.AudioContent;
using static Assert;

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    internal static readonly (string Name, Action Test)[] JudgeDrainCases = {
        ("Native Judge unknown or in-flight activity blocks LOCAL graph admission before roots", JudgeAdmission),
        ("LOCAL termination reads aggregate native Judge activity without presenting it as ended", JudgeSnapshot),
        ("Native Judge activity is checked again after mapping withdrawal before any root free", JudgeWithdrawal),
        ("Native Judge activity blocks graph retirement and binding stop until actually drained", JudgeRetirement)
    };
    private static void JudgeAdmission()
    {
        using var f = new Fixture(register: false);
        f.Backend.JudgeKnown = false; Check(!f.Bridge.IsReadyForHost(f.Host.Sessions));
        Throws<InvalidOperationException>(() => f.Register()); Equal(0, f.Backend.Roots);
        f.Backend.JudgeKnown = true; f.Backend.JudgeInFlight = 1;
        Check(!f.Bridge.IsReadyForHost(f.Host.Sessions)); Throws<InvalidOperationException>(() => f.Register()); Equal(0, f.Backend.Roots);
        f.Backend.JudgeInFlight = 0; f.Register(); Equal(3, f.Backend.Roots);
    }
    private static void JudgeSnapshot()
    {
        using var f = new Fixture(); f.Backend.JudgeInFlight = 2; f.Transport.InFlight = 1;
        var state = f.Bridge.Read(f.Run.Session, f.Run.Controller);
        Equal(7, state.InFlightNativeBoundaries); Check(state.NativeBoundaryStateKnown);
        f.Backend.JudgeKnown = false; Check(!f.Bridge.Read(f.Run.Session, f.Run.Controller).NativeBoundaryStateKnown);
    }
    private static void JudgeWithdrawal()
    {
        using var f = new Fixture(); f.Retire(f.Run); var reads = 0;
        f.Backend.JudgeActivityOverride = () => new(++reads == 1 ? 0 : 1, true);
        Check(!f.Bridge.TryRetire(f.Run.Session)); Equal(0, f.Backend.Frees);
        Equal(0, f.Bridge.Describe().PublishedMappings); Equal(3, f.Backend.Roots);
        f.Backend.JudgeActivityOverride = null; Check(f.Bridge.TryRetire(f.Run.Session)); Equal(3, f.Backend.Frees);
    }
    private static void JudgeRetirement()
    {
        using var f = new Fixture(); f.Retire(f.Run); f.Backend.JudgeInFlight = 1;
        Check(!f.Bridge.TryRetire(f.Run.Session)); Equal(3, f.Backend.Roots);
        f.Backend.JudgeInFlight = 0; f.Backend.JudgeKnown = false;
        Check(!f.Bridge.TryRetire(f.Run.Session)); Equal(0, f.Backend.Frees);
        f.Backend.JudgeKnown = true; Check(f.Bridge.TryRetire(f.Run.Session)); Equal(3, f.Backend.Frees);
        f.Backend.JudgeInFlight = 1; Check(!f.Bridge.TryStop());
        f.Backend.JudgeInFlight = 0; Check(f.Bridge.TryStop());
    }
}
