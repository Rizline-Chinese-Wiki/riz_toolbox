using System.Runtime.InteropServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static class NativeJudgementChartLineChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Wide decorative chart retains original ordinals past line 4096", WideChart),
        ("Wide chart refuses count beyond the validated backing vector", PhysicalBound),
        ("Wide chart refuses an altered exact list version and drains roots", ChangedVersion),
        ("Wide chart retains its note-bearing line bound", NoteBearingBound),
    };

    private static void WideChart()
    {
        using var f = new Fixture(6567, 8192, ordinal => ordinal is 0 or 4096 or 6566 ? 1 : 0);
        using (var run = f.Backend.CaptureRun() ?? throw new InvalidOperationException("No retained run was captured."))
        {
            var locations = run.Graph.Select(row => (row.LineOrdinal, row.NoteOrdinal)).ToArray();
            // CaptureRun retains the discovery array, controller, information
            // and chart alongside the three independently rooted note rows.
            if (!locations.SequenceEqual(new[] { (0, 0), (4096, 0), (6566, 0) }) || run.Graph.Any(row => row.Reference == 0)
                || f.Native.ActiveRoots != 7)
                throw new InvalidOperationException($"Observed locations {string.Join(", ", locations)} and {f.Native.ActiveRoots} retained roots.");
        }
        Check(f.Native.ActiveRoots == 0 && f.Native.JudgementBackingReads >= 3);
    }

    private static void PhysicalBound()
    {
        using var f = new Fixture(4097, 4096, _ => 0);
        Refuse(f, "backing capacity 4096");
        Check(f.Native.SeekListItems == 0);
    }

    private static void ChangedVersion()
    {
        using var f = new Fixture(4097, 8192, ordinal => ordinal == 4096 ? 1 : 0);
        f.Native.AfterSeekInvocation = (name, instance) =>
        {
            if (name == "get_Item" && instance == f.Native.SeekLines)
            {
                f.Native.ReviseJudgementLines();
                f.Native.AfterSeekInvocation = null;
            }
        };
        Refuse(f, "backing storage changed");
        Check(f.Native.SeekListItems == 4098);
    }

    private static void NoteBearingBound()
    {
        using var f = new Fixture(4097, 8192, _ => 1);
        Refuse(f, "note-bearing line bound");
    }

    private static void Refuse(Fixture f, string reason)
    {
        try
        {
            using var unexpected = f.Backend.CaptureRun();
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains(reason, StringComparison.Ordinal) && f.Native.ActiveRoots == 0);
            return;
        }
        throw new InvalidOperationException("The invalid chart was accepted.");
    }

    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("The bounded judgement chart observation differed."); }

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        internal readonly NativeJudgementBackend Backend;
        internal Fixture(int lines, int capacity, Func<int, int> noteCount)
        {
            Native.ConfigureJudgementChartLines(lines, capacity, noteCount);
            var state = new GameEventState();
            state.SetAvailability(scenes: true, runs: true, pause: true);
            state.Scene("Play");
            state.Controller(Native.GameplayInformation.ToInt64(), true, false, "controlled-wide-chart",
                Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, state);
        }
        public void Dispose() { Backend.Dispose(); Native.Dispose(); }
    }
}

sealed partial class FakeNative
{
    private IntPtr _judgementLineArray, _judgementArrayRank, _judgementArrayLength;
    private int _judgementLineCapacity;
    internal int JudgementBackingReads { get; private set; }

    internal void ConfigureJudgementChartLines(int count, int capacity, Func<int, int> noteCount)
    {
        ConfigureSeekChart();
        _stableBorrowedMetadataText ??= new();
        var lineArrayType = AddType("InLevel.Line[]", 29, false, IntPtr.Size);
        _judgementLineArray = SeekObject(lineArrayType);
        _judgementLineCapacity = capacity;
        _judgementArrayRank = GameplayMethodAdd(lineArrayType, "get_Rank", false, Int32);
        _judgementArrayLength = GameplayMethodAdd(lineArrayType, "get_Length", false, Int32);
        var sampleNote = SeekNotes[0];
        _seekLists[SeekLines] = Enumerable.Range(0, count).Select(ordinal => (object)SeekLine(ordinal + 2,
            Enumerable.Repeat(sampleNote, noteCount(ordinal)).ToArray())).ToArray();
        SeekField("judgement.backing", _seekLineListType, "_items", lineArrayType, SeekLines, _judgementLineArray, 0x10);
        SeekField("judgement.size", _seekLineListType, "_size", Int32, SeekLines, count, 0x18);
        SeekField("judgement.version", _seekLineListType, "_version", Int32, SeekLines, 1, 0x1C);
        Add("il2cpp_class_instance_size", "ArraySize", nameof(JudgementInstanceSize));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(JudgementInvoke));
        GameplayApi = new NativeRuntime(ExportPointer);
        GameplayApi.BindCurrentThread();
    }

    internal void ReviseJudgementLines() => SetGameplayField("judgement.version", 2);
    private uint JudgementInstanceSize(IntPtr cls) => 0x100;
    private IntPtr JudgementInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        if (instance != _judgementLineArray || method != _judgementArrayRank && method != _judgementArrayLength)
            return SeekInvoke(method, instance, arguments, exception);
        ++JudgementBackingReads;
        Marshal.WriteIntPtr(exception, IntPtr.Zero);
        return Box(Int32, BitConverter.GetBytes(method == _judgementArrayRank ? 1 : _judgementLineCapacity));
    }
}
