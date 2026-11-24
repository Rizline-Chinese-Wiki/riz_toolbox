using System.Reflection;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime.Hooks;
using static Assert;

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    internal static readonly (string Name, Action Test)[] PublicationCases = {
        ("Launch reservation owns complete fixed publication storage before native adoption and never exposes a partial token", ReservedPublicationStorage),
        ("Published reserved tokens retain value equality and dictionary hashes after rejected identity rewrites", ReservedIdentityStability),
        ("Retired graph slots publish fresh identities without changing tokens already retained by a reader", PublicationSlotReuse)
    };
    private static object GraphRecord(NativeLocalRunTerminationBoundaryState bridge)
        => FieldValue<Array>(bridge, "_records").Cast<object?>().First(item => item is not null)!;
    private static T FieldValue<T>(object owner, string name)
        => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static ExactHookBroker.LocalInitializationIdentity? ControllerToken(NativeLocalRunTerminationBoundaryState bridge, long pointer)
        => (ExactHookBroker.LocalInitializationIdentity?)bridge.GetType()
            .GetMethod("ClassifyInitializationController", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bridge, new object[] { new IntPtr(pointer) });
    private static void ReservedPublicationStorage()
    {
        using var f = new PendingFixture(initialization: true);
        var entry = GraphRecord(f.Bridge);
        var controller = FieldValue<ExactHookBroker.LocalInitializationIdentity>(entry, "ReservedInitializationIdentity");
        var progress = FieldValue<ExactHookBroker.LocalProgressIdentity>(entry, "ReservedProgressIdentity");
        var progressSlots = FieldValue<Array>(f.Bridge, "_published");
        var controllerSlots = FieldValue<Array>(f.Bridge, "_initializationControllers");
        Equal(32, progressSlots.Length); Equal(32, controllerSlots.Length);
        Equal(0L, controller.ControllerIdentity); Equal(0L, progress.ControllerIdentity); Equal(0L, progress.ProgressIdentity);
        Equal(0, f.Backend.Roots); Equal(0, f.Backend.TargetReads); Check(!f.Launch.HasNativeAttempt);
        f.Backend.OnRoot = pointer => {
            if (pointer != f.Controller && pointer != f.Controller + 1 && pointer != f.Controller + 2) return;
            var reads = f.Backend.TargetReads;
            Task.Run(() => {
                Check(f.Backend.Classifier!(new(f.Controller + 2)) is null);
                var token = ControllerToken(f.Bridge, f.Controller);
                Check(pointer == f.Controller ? token is null : ReferenceEquals(controller, token));
            }).GetAwaiter().GetResult();
            Equal(reads, f.Backend.TargetReads);
        };
        Initialize(f); f.Backend.OnRoot = null;
        Check(ReferenceEquals(progressSlots, FieldValue<Array>(f.Bridge, "_published")));
        Check(ReferenceEquals(controllerSlots, FieldValue<Array>(f.Bridge, "_initializationControllers")));
        Check(ReferenceEquals(controller, ControllerToken(f.Bridge, f.Controller)));
        Check(ReferenceEquals(progress, f.Backend.Classifier!(new(f.Controller + 2))));
        Equal(f.Controller, controller.ControllerIdentity); Equal(f.Controller, progress.ControllerIdentity);
        Equal(f.Controller + 2, progress.ProgressIdentity); Equal(1, f.Bridge.Describe().PublishedMappings);
        Check(f.Bridge.VerifyInitializedGraph(f.Launch));
    }
    private static void ReservedIdentityStability()
    {
        using var f = new Fixture();
        var progress = f.Backend.Classifier!(new(f.Run.Progress))!;
        var expected = new ExactHookBroker.LocalProgressIdentity(progress.SessionId, progress.RunGeneration, progress.ControllerIdentity, progress.ProgressIdentity);
        Check(progress == expected); Equal(expected.GetHashCode(), progress.GetHashCode());
        var hash = progress.GetHashCode();
        f.Backend.OnProgress = _ => {
            Check(f.Backend.Binding!.Read(progress).InFlight > 0);
            Throws<InvalidOperationException>(() => progress.InitializeReservedGraph(f.Run.Controller + 99, f.Run.Progress + 99));
            Check(f.Backend.Binding.Read(expected).InFlight > 0); Equal(hash, progress.GetHashCode());
        };
        f.Backend.Progress(f.Run.Progress); f.Backend.OnProgress = null;
        Equal(0L, f.Backend.Binding!.Read(expected).InFlight);
        var reserved = new ExactHookBroker.LocalInitializationIdentity("reserved", 7, 0);
        Throws<InvalidOperationException>(() => reserved.InitializeReservedController(0));
        reserved.InitializeReservedController(123);
        var ordinary = new ExactHookBroker.LocalInitializationIdentity("reserved", 7, 123);
        Check(reserved == ordinary); Equal(ordinary.GetHashCode(), reserved.GetHashCode());
        Throws<InvalidOperationException>(() => reserved.InitializeReservedController(456));
        Equal(123L, reserved.ControllerIdentity); Check(reserved == ordinary);
    }
    private static void PublicationSlotReuse()
    {
        using var f = new Fixture();
        var oldRecord = GraphRecord(f.Bridge);
        var slots = FieldValue<Array>(f.Bridge, "_published");
        var oldToken = f.Backend.Classifier!(new(f.Run.Progress))!; var hash = oldToken.GetHashCode();
        f.Retire(f.Run); Check(f.Bridge.TryRetire(f.Run.Session));
        Check(f.Backend.Classifier(new(f.Run.Progress)) is null); Equal(0, f.Bridge.Describe().PublishedMappings);
        var next = f.AddRun(); f.Bridge.RegisterCommitted(next.Session, next.Borrowed);
        var nextRecord = GraphRecord(f.Bridge); var nextToken = f.Backend.Classifier(new(next.Progress))!;
        Equal(FieldValue<int>(oldRecord, "Slot"), FieldValue<int>(nextRecord, "Slot"));
        Check(!ReferenceEquals(oldRecord, nextRecord) && !ReferenceEquals(oldToken, nextToken));
        Check(ReferenceEquals(slots, FieldValue<Array>(f.Bridge, "_published")));
        Equal(f.Run.Progress, oldToken.ProgressIdentity); Equal(hash, oldToken.GetHashCode());
        Check(oldToken != nextToken); Check(f.Backend.Classifier(new(f.Run.Progress)) is null);
        var reads = f.Backend.TargetReads;
        Task.Run(() => Check(ReferenceEquals(nextToken, f.Backend.Classifier(new(next.Progress))))).GetAwaiter().GetResult();
        Equal(reads, f.Backend.TargetReads);
    }
}
