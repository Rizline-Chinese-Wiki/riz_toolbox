using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static class NativeTweenSourceRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Source journal V1 preserves actual operation identity and the fixed 22-site native wire", Wire),
        ("Source journal retention precedes its watermark and cannot capture historical rows retroactively", Watermark),
        ("Source journal copies continuous non-consuming rows with repeated rooted occurrence identities", Read),
        ("Source journal refuses unknown borrowed identities without allocating late roots", Unrooted),
        ("Source journal root validation and read failures retain all independent responsibility", Failure),
        ("Source journal rejects absent changed epoch unknown and incomplete native records", Changed),
        ("Source journal independent Play is never relabeled as an Update or production receipt", Play),
        ("Source journal worker and foreign root-set calls stop before native access", Admission),
        ("Source journal rejects a reentered batch even when its transport catches the rejection", ReenteredBatch),
        ("Source journal keeps a continuous reader usable beyond the retained ring capacity", ContinuousWindow),
        ("Source journal rejects lagged readers before interpreting overwritten native records", ExpiredWindow)
    };
    private static void Wire()
    {
        NativeTweenSource.ValidateLayout(); Check(Enum.GetValues<NativeTweenSourceSite>().Length == 22);
        using var f = new Fixture(); Check(NativeTweenSource.InformationShape(f.Native.Info));
        var row = f.Row(1); Check(NativeTweenSource.RecordShape(row, 7, 1));
        row.Operation = NativeTweenSourceOperation.Play; Check(!NativeTweenSource.RecordShape(row, 7, 1));
        row = f.Row(1); row.Flags |= NativeTweenSourceFlags.ReadFault; Check(!NativeTweenSource.RecordShape(row, 7, 1));
        var info = f.Native.Info; info.MetadataBound = 0; Check(!NativeTweenSource.InformationShape(info));
        info = f.Native.Info; info.BindingCount = 22; Check(!NativeTweenSource.InformationShape(info));
        Check(Marshal.OffsetOf<NativeTweenSourceRecord>(nameof(NativeTweenSourceRecord.Operation)).ToInt32() == 28);
    }
    private static void Watermark()
    {
        using var f = new Fixture(); f.Native.Add(f.Row(1)); var roots = f.Retain();
        Check(roots.RetainedAfterSequence == 1 && roots.LastReadSequence == 1 && f.Reader.RootRecords == 4);
        f.Native.Add(f.Row(2));
        Throws<ArgumentOutOfRangeException>(() => f.Reader.ReadAvailable(roots, 0, 8));
        var batch = f.Reader.ReadAvailable(roots, 1, 8);
        Check(batch.AfterSequence == 1 && batch.LastSequence == 2 && batch.Records.Count == 1
            && batch.Records[0].Record.EventSequence == 2 && f.Native.ReadSequences.SequenceEqual(new ulong[] { 2 }));
        Check(f.NewRoots == 8 && f.FreeRoots == 0 && !batch.Production && ReferenceEquals(batch.Roots.Owner, f.Owner));
    }
    private static void Read()
    {
        using var f = new Fixture(); var roots = f.Retain();
        var before = f.Row(1); var after = f.Row(2); after.Stage = NativeTweenSourceStage.After;
        after.Flags |= NativeTweenSourceFlags.Paired; after.Transaction = before.Transaction;
        f.Native.Add(before); f.Native.Add(after);
        var batch = f.Reader.ReadAvailable(roots, 0, 1);
        Check(batch.Records.Count == 1 && batch.LastSequence == 1 && batch.Records[0].Record.Tween == f.Tween);
        var next = f.Reader.ReadAvailable(roots, 1, 8);
        Check(next.Records.Count == 1 && next.Records[0].Record.Tween == f.Tween && next.Records[0].Record.Transaction == before.Transaction
            && next.Records[0].Record.Stage == NativeTweenSourceStage.After && f.Native.Records.Count == 2 && f.NewRoots == 8);
        var empty = f.Reader.ReadAvailable(roots, 2, 8); Check(empty.Records.Count == 0 && empty.LastSequence == 2);
    }
    private static void Unrooted()
    {
        using var f = new Fixture(); var roots = f.Retain(); var row = f.Row(1); row.ArrayAfter = new(0x9999); f.Native.Add(row);
        Throws<UnsupportedSignatureException>(() => f.Reader.ReadAvailable(roots, 0, 8));
        Check(f.Reader.Unknown && f.Reader.RootRecords == 4 && f.NewRoots == 8 && f.FreeRoots == 0 && roots.LastReadSequence == 0);
        using var foreign = new Fixture(); var foreignRoots = foreign.Retain(); row = foreign.Row(1); row.Manager = new(0x7777); row.Tween = new(0x8888);
        row.ArrayAfter = new(0x9999); foreign.Native.Add(row);
        var ignored = foreign.Reader.ReadAvailable(foreignRoots, 0, 8);
        Check(ignored.LastSequence == 1 && ignored.Records.Count == 0 && !foreign.Reader.Unknown && foreign.NewRoots == 8);
    }
    private static void Failure()
    {
        using var validation = new Fixture(); validation.FailValidate = true;
        Throws<UnsupportedSignatureException>(() => validation.Retain());
        Check(validation.Reader.Unknown && validation.Reader.RootRecords == 1 && validation.NewRoots == 5 && validation.FreeRoots == 0);
        using var source = new Fixture(); var roots = source.Retain(); source.Native.Add(source.Row(1));
        source.Native.OnRead = () => source.Native.Info.Unknown = 1;
        Throws<UnsupportedSignatureException>(() => source.Reader.ReadAvailable(roots, 0, 8));
        Check(source.Reader.Unknown && source.Reader.RootRecords == 4 && source.FreeRoots == 0);
    }
    private static void Changed()
    {
        foreach (var mode in new[] { "missing", "epoch", "parent", "fault", "record-sequence", "thread", "stage", "configuration" })
        {
            using var f = new Fixture(); var roots = f.Retain(); var row = f.Row(1);
            if (mode == "parent") row.Flags &= ~NativeTweenSourceFlags.ParentMatched;
            if (mode == "fault") row.Flags |= NativeTweenSourceFlags.Unknown;
            if (mode == "record-sequence") row.EventSequence = 2;
            if (mode == "thread") row.ThreadId = 0;
            if (mode == "stage") row.Stage = NativeTweenSourceStage.Decision;
            f.Native.Add(row); if (mode == "missing") f.Native.ReadStatus = 3;
            if (mode == "epoch") ++f.Native.Info.Epoch;
            if (mode == "configuration") f.Native.Info.Configured = 0;
            Throws<UnsupportedSignatureException>(() => f.Reader.ReadAvailable(roots, 0, 8));
            Check(f.Reader.Unknown && f.Reader.RootRecords == 4 && f.FreeRoots == 0);
        }
    }
    private static void Play()
    {
        using var f = new Fixture(); var roots = f.Retain(); var row = f.Row(1);
        row.Site = NativeTweenSourceSite.PlayOnStart; row.Operation = NativeTweenSourceOperation.Play;
        row.Manager = IntPtr.Zero; row.ParentUpdateSequence = 0; row.Container = IntPtr.Zero; row.ArrayBefore = row.ArrayAfter = IntPtr.Zero;
        f.Native.Add(row); var batch = f.Reader.ReadAvailable(roots, 0, 8);
        Check(batch.Records.Count == 1 && batch.Records[0].Record.Operation == NativeTweenSourceOperation.Play
            && batch.Records[0].Record.ParentUpdateSequence == 0 && batch.Records[0].Record.Manager == IntPtr.Zero
            && !batch.Records[0].Production && !f.Reader.Production);
        Throws<InvalidOperationException>(() => new NativeTweenSourceBoundRecord(f.Reader, roots, row, new object()));
    }
    private static void Admission()
    {
        using var f = new Fixture(); var roots = f.Retain(); var reads = f.Native.InfoReads;
        Task.Run(() => Throws<InvalidOperationException>(() => f.Reader.ReadAvailable(roots, 0, 8))).GetAwaiter().GetResult();
        Check(f.Native.InfoReads == reads && f.Native.ReadSequences.Count == 0);
        using var peer = new Fixture(); var peerRoots = peer.Retain();
        Throws<InvalidOperationException>(() => f.Reader.ReadAvailable(peerRoots, 0, 8));
        Check(f.Native.InfoReads == reads && f.NewRoots == 8 && f.FreeRoots == 0);
    }
    private static void ReenteredBatch()
    {
        using var f = new Fixture(); var roots = f.Retain();
        f.Native.Add(f.Row(1));
        var nested = 0;
        f.Native.OnRead = () =>
        {
            if (++nested == 1)
                Throws<InvalidOperationException>(() => f.Reader.ReadAvailable(roots, 0, 8));
        };
        Throws<InvalidOperationException>(() => f.Reader.ReadAvailable(roots, 0, 8));
        var reads = f.Native.InfoReads;
        Throws<InvalidOperationException>(() => f.Reader.ReadAvailable(roots, 0, 8));
        Check(nested == 1 && f.Reader.Unknown && roots.LastReadSequence == 0 && f.Native.InfoReads == reads
            && f.Native.ReadSequences.SequenceEqual(new ulong[] { 1 }) && f.Reader.RootRecords == 4 && f.FreeRoots == 0);
    }
    private static void ContinuousWindow()
    {
        using var f = new Fixture(); var roots = f.Retain();
        for (ulong sequence = 1; sequence <= 8200; ++sequence)
        {
            f.Native.Add(f.Row(sequence));
            var batch = f.Reader.ReadAvailable(roots, sequence - 1, 1);
            Check(batch.LastSequence == sequence && batch.Records.Count == 1
                && batch.Records[0].Record.EventSequence == sequence);
        }
        Check(!f.Reader.Unknown && roots.LastReadSequence == 8200 && f.Native.Info.RetainedRecords == 8192
            && f.Reader.RootRecords == 4 && f.FreeRoots == 0);
    }
    private static void ExpiredWindow()
    {
        using var f = new Fixture(); var roots = f.Retain();
        for (ulong sequence = 1; sequence <= 8193; ++sequence) f.Native.Add(f.Row(sequence));
        Throws<UnsupportedSignatureException>(() => f.Reader.ReadAvailable(roots, 0, 8192));
        Check(f.Reader.Unknown && roots.LastReadSequence == 0 && f.Native.ReadSequences.Count == 0
            && f.Reader.RootRecords == 4 && f.FreeRoots == 0);
    }
    private sealed class Owner : INativeTweenSourceOwner
    { private readonly Action _verify; internal Owner(Action verify) => _verify = verify; public void VerifyTweenSourceOwner() => _verify(); }
    private sealed class Transport : INativeTweenSourceTransport
    {
        internal NativeTweenSourceInformation Info = new() { Version = 1, Size = 96, Configured = 1, MetadataBound = 1,
            BindingCount = 43, JournalCapacity = 8192, Epoch = 7 };
        internal readonly Dictionary<ulong, NativeTweenSourceRecord> Records = new();
        internal readonly List<ulong> ReadSequences = new(); internal int InfoReads; internal uint ReadStatus; internal Action? OnRead;
        internal void Add(NativeTweenSourceRecord row)
        {
            var next = checked(Info.Events + 1); Records.Add(next, row); Info.Events = next;
            Info.RetainedRecords = Math.Min(next, NativeTweenSource.JournalCapacity);
            if (next > NativeTweenSource.JournalCapacity) Records.Remove(next - NativeTweenSource.JournalCapacity);
        }
        public uint GetInfo(ref NativeTweenSourceInformation information) { ++InfoReads; information = Info; return 0; }
        public uint ReadRecord(ulong epoch, ulong sequence, ref NativeTweenSourceRecord record)
        { ReadSequences.Add(sequence); OnRead?.Invoke(); if (ReadStatus != 0) return ReadStatus; if (!Records.TryGetValue(sequence, out record)) return 3; return 0; }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly NativeThreadAccess _thread = new(); private readonly Dictionary<uint, IntPtr> _roots = new(); private uint _next;
        internal readonly IntPtr Manager = new(0x1000), Tween = new(0x2000), Array = new(0x3000), Container = new(0x4000);
        internal readonly Transport Native = new(); internal readonly Owner Owner; internal readonly NativeTweenSource Reader;
        internal bool FailValidate; internal int NewRoots, FreeRoots;
        internal Fixture()
        {
            _thread.BindCurrentThread(); Owner = new(_thread.Verify);
            Reader = NativeTweenSource.CreateControlledForTests(Native, _thread.Verify, Root, (root, kind) => {
                if (FailValidate) throw new UnsupportedSignatureException("Controlled changed official metadata."); return root.Target;
            });
        }
        internal NativeTweenSourceRootSet Retain() => Reader.RetainRootedObjects(Owner, new[] {
            new NativeTweenSourceRootReference(Root(Manager), NativeTweenSourceObjectKind.Manager),
            new NativeTweenSourceRootReference(Root(Tween), NativeTweenSourceObjectKind.Tween),
            new NativeTweenSourceRootReference(Root(Array), NativeTweenSourceObjectKind.TweenArray),
            new NativeTweenSourceRootReference(Root(Container), NativeTweenSourceObjectKind.ReadyStack) });
        internal NativeTweenSourceRecord Row(ulong sequence) => new() { Version = 1, Size = 192, Site = NativeTweenSourceSite.ReadyPopFront,
            Stage = NativeTweenSourceStage.Before, Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched,
            ThreadId = 4, Depth = 1, Operation = NativeTweenSourceOperation.Update, Epoch = 7, UpdateSequence = 19,
            EventSequence = sequence, Transaction = 1, SourcePc = new(0x5000), RelayReturnPc = new(0x6000),
            Manager = Manager, Container = Container, ArrayBefore = Array, ArrayAfter = Array, Tween = Tween, SourceIndex = 0, DestinationIndex = 1 };
        private NativeRuntime.RootHandle Root(IntPtr value)
        { _thread.Verify(); var id = ++_next; _roots.Add(id, value); ++NewRoots; return new(id, key => { _roots.Remove(key); ++FreeRoots; }, key => _roots.GetValueOrDefault(key), _thread.Verify); }
        public void Dispose() { /* Independent roots and unfinished journal responsibility remain retained. */ }
    }
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Source journal invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
