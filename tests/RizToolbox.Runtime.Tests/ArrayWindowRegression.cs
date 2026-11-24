using RizToolbox.Runtime;

internal static class ArrayWindowRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Native rotating windows discover stable-array tail after 4096 without skipped-prefix reads", TailAndBudget),
        ("Native window wraps at array end without duplicate reads within a pass", Wrap),
        ("Native cursor normalizes after shortening empty and null arrays", LengthChanges),
        ("Native window advances past failed GetValue and releases every array root", FailedRead),
        ("Disposing a partial native window resumes at the next attempted index", EarlyDispose),
        ("Native windows validate array metadata size and budgets before element reads", Rejections),
        ("Native window maximum and one-element bounds remain exact", Bounds),
        ("Existing complete array enumeration retains its ABI and full sequential results", Legacy),
        ("Cursor integer boundaries never overflow when wrapping or normalizing", IntegerBounds)
    };
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Array-window invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void TailAndBudget()
    {
        using var native = new FakeNative(); var cursor = new NativeArrayScanCursor();
        var seen = new HashSet<int>(); var tailCandidateSeen = false;
        for (var pass = 0; pass < 3; pass++)
        {
            // FindObjectsOfTypeAll creates a fresh array each pass. Only index
            // position persists, so changing array identity must not restart it.
            var array = native.ReferenceArray(10003); native.ArrayReadIndices.Clear();
            var values = native.Api.ArrayItems(array, cursor, 4096).ToArray();
            // Only the tail element stands in for an eligible visual target;
            // all earlier values consume scanning budget without admission.
            tailCandidateSeen |= values.Any(value => value == new IntPtr(110002));
            Check(values.Length == 4096 && native.ArrayReadIndices.Count == 4096);
            Check(native.ArrayReadIndices.First() == (pass * 4096) % 10003);
            foreach (var index in native.ArrayReadIndices) seen.Add(index);
            Check(native.ActiveRoots == 0 && native.AllArrayReadsRooted);
        }
        Check(seen.Count == 10003 && seen.Contains(10002) && tailCandidateSeen && native.ArrayRankReads == 3 && native.ArrayLengthReads == 3);
    }
    private static void Wrap()
    {
        using var native = new FakeNative(); var cursor = new NativeArrayScanCursor(); var array = native.ReferenceArray(7);
        _ = native.Api.ArrayItems(array, cursor, 5).ToArray(); native.ArrayReadIndices.Clear();
        _ = native.Api.ArrayItems(array, cursor, 5).ToArray();
        Check(native.ArrayReadIndices.SequenceEqual(new[] { 5, 6, 0, 1, 2 }) && native.ActiveRoots == 0);
        native.ArrayReadIndices.Clear(); _ = native.Api.ArrayItems(array, cursor, 100).ToArray();
        Check(native.ArrayReadIndices.SequenceEqual(new[] { 3, 4, 5, 6, 0, 1, 2 }));
    }
    private static void LengthChanges()
    {
        using var native = new FakeNative(); var cursor = new NativeArrayScanCursor();
        _ = native.Api.ArrayItems(native.ReferenceArray(9), cursor, 8).ToArray();
        native.ArrayReadIndices.Clear(); _ = native.Api.ArrayItems(native.ReferenceArray(3), cursor, 2).ToArray();
        Check(native.ArrayReadIndices.SequenceEqual(new[] { 2, 0 }));
        native.ArrayReadIndices.Clear(); Check(!native.Api.ArrayItems(native.ReferenceArray(0), cursor, 4).Any());
        Check(native.ArrayReadIndices.Count == 0);
        _ = native.Api.ArrayItems(native.ReferenceArray(5), cursor, 2).ToArray();
        Check(native.ArrayReadIndices.SequenceEqual(new[] { 0, 1 }));
        native.ArrayReadIndices.Clear(); Check(!native.Api.ArrayItems(IntPtr.Zero, cursor, 4).Any());
        _ = native.Api.ArrayItems(native.ReferenceArray(2), cursor, 1).ToArray();
        Check(native.ArrayReadIndices.SequenceEqual(new[] { 0 }) && native.ActiveRoots == 0);
    }
    private static void FailedRead()
    {
        using var native = new FakeNative(); var cursor = new NativeArrayScanCursor(); var array = native.ReferenceArray(10);
        native.FailArrayIndex = 2;
        Throws<InvalidOperationException>(() => native.Api.ArrayItems(array, cursor, 4).ToArray());
        Check(native.ArrayReadIndices.SequenceEqual(new[] { 0, 1, 2 }) && native.ActiveRoots == 0);
        native.ArrayReadIndices.Clear(); native.FailArrayIndex = null;
        _ = native.Api.ArrayItems(array, cursor, 4).ToArray();
        Check(native.ArrayReadIndices.SequenceEqual(new[] { 3, 4, 5, 6 }) && native.ActiveRoots == 0 && native.AllArrayReadsRooted);
    }
    private static void EarlyDispose()
    {
        using var native = new FakeNative(); var cursor = new NativeArrayScanCursor(); var array = native.ReferenceArray(10);
        using (var enumerator = native.Api.ArrayItems(array, cursor, 4).GetEnumerator())
        { Check(enumerator.MoveNext()); Check(native.ActiveRoots == 1 && native.ArrayReadIndices.SequenceEqual(new[] { 0 })); }
        Check(native.ActiveRoots == 0); native.ArrayReadIndices.Clear();
        _ = native.Api.ArrayItems(array, cursor, 2).ToArray();
        Check(native.ArrayReadIndices.SequenceEqual(new[] { 1, 2 }) && native.ActiveRoots == 0);
    }
    private static void Rejections()
    {
        using var native = new FakeNative(); var cursor = new NativeArrayScanCursor(); var array = native.ReferenceArray(2);
        Throws<ArgumentOutOfRangeException>(() => native.Api.ArrayItems(array, cursor, -1).ToArray());
        Throws<ArgumentNullException>(() => native.Api.ArrayItems(array, null!, 1).ToArray());
        Throws<UnsupportedSignatureException>(() => native.Api.ArrayItems(native.OtherObject, cursor, 2).ToArray());
        Throws<UnsupportedSignatureException>(() => native.Api.ArrayItems(native.ReferenceArray(2, 2), cursor, 2).ToArray());
        foreach (var length in new[] { -1, 50001, int.MaxValue })
        {
            native.ArrayReportedLength = length;
            Throws<InvalidOperationException>(() => native.Api.ArrayItems(array, cursor, 2).ToArray());
        }
        Check(native.ArrayReadIndices.Count == 0 && native.ActiveRoots == 0);
        native.ArrayReportedLength = null;
        Check(!native.Api.ArrayItems(array, cursor, 0).Any() && native.ArrayReadIndices.Count == 0);
        Exception? wrongThread = null;
        var thread = new Thread(() => { try { native.Api.ArrayItems(array, cursor, 2).ToArray(); } catch (Exception error) { wrongThread = error; } });
        thread.Start(); thread.Join();
        Check(wrongThread is InvalidOperationException && native.ArrayReadIndices.Count == 0 && native.ActiveRoots == 0);
    }
    private static void Bounds()
    {
        using var native = new FakeNative(); var cursor = new NativeArrayScanCursor();
        var array = native.ReferenceArray(50000);
        Check(native.Api.ArrayItems(array, cursor, 50001).Count() == 50000 && native.ArrayReadIndices.Count == 50000);
        Check(native.ArrayReadIndices.First() == 0 && native.ArrayReadIndices.Last() == 49999 && native.ActiveRoots == 0);
        native.ArrayReadIndices.Clear(); var single = native.ReferenceArray(1);
        for (var pass = 0; pass < 3; pass++) Check(native.Api.ArrayItems(single, cursor, int.MaxValue).Count() == 1);
        Check(native.ArrayReadIndices.SequenceEqual(new[] { 0, 0, 0 }) && native.ActiveRoots == 0);
    }
    private static void Legacy()
    {
        using var native = new FakeNative();
        Check(native.Api.ArrayItems(native.ReferenceArray(7)).Count() == 7);
        Check(native.ArrayReadIndices.SequenceEqual(Enumerable.Range(0, 7)) && native.ActiveRoots == 0 && native.AllArrayReadsRooted);
    }
    private static void IntegerBounds()
    {
        var cursor = new NativeArrayScanCursor();
        // Boundary injection avoids allocating or walking two billion objects.
        // Production native arrays keep the stricter 50000-element limit.
        typeof(NativeArrayScanCursor).GetField("_position", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(cursor, int.MaxValue - 1);
        Check(cursor.Read(int.MaxValue, 2, index => index).SequenceEqual(new[] { int.MaxValue - 1, 0 }));
        Check(cursor.Read(1, int.MaxValue, index => index).Single() == 0);
        Throws<ArgumentOutOfRangeException>(() => cursor.Read(-1, 1, index => index).ToArray());
        Throws<ArgumentOutOfRangeException>(() => cursor.Read(1, -1, index => index).ToArray());
        Throws<ArgumentNullException>(() => cursor.Read<int>(1, 1, null!).ToArray());
    }
}
