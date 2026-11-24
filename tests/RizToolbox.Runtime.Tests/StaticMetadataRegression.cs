using RizToolbox.Runtime;

internal static class StaticMetadataRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Exact static inspection reports a declared signature without invoking or claiming an executable entry", Declared),
        ("Exact static inspection rejects an instance method and preserves caller objects", Instance),
        ("Exact static inspection rejects a different native return type", ReturnType),
        ("Exact static inspection rejects a different declaring class", DeclaringClass),
        ("Exact static inspection refuses generic and inflated shared methods", Generic),
        ("Exact static inspection rejects foreign threads before consulting metadata", Thread)
    };
    private static NativeMetadataMethod Inspect(FakeNative f, IntPtr? cls = null, string returns = "System.Void")
    {
        f.NamedMethodAvailable = true;
        return f.Api.InspectDeclaredStaticMethod(cls ?? f.ClassOf(f.Receiver), "Call", Array.Empty<string>(), returns, IntPtr.Zero);
    }
    private static void Declared()
    {
        using var f = new FakeNative { IsStatic = true };
        var evidence = Inspect(f);
        Check(evidence.MethodInfo == f.Method && evidence.ReturnType == "System.Void" && (evidence.Flags & 0x10) != 0
            && evidence.Entry is null && evidence.EntryStatus.StartsWith("Unverified"));
        Check(f.Invocations == 0 && f.NewRoots == 0);
    }
    private static void Instance()
    { using var f = new FakeNative(); Throws<MissingMethodException>(() => Inspect(f)); Check(f.Invocations == 0 && f.NewRoots == 0); }
    private static void ReturnType()
    { using var f = new FakeNative { IsStatic = true }; Throws<UnsupportedSignatureException>(() => Inspect(f, returns: "System.Object")); Check(f.Invocations == 0); }
    private static void DeclaringClass()
    { using var f = new FakeNative { IsStatic = true }; Throws<UnsupportedSignatureException>(() => Inspect(f, f.ClassOf(f.String))); Check(f.Invocations == 0); }
    private static void Generic()
    {
        using (var f = new FakeNative { IsStatic = true, GenericMethod = true }) Throws<UnsupportedSignatureException>(() => Inspect(f));
        using (var f = new FakeNative { IsStatic = true, InflatedMethod = true }) Throws<UnsupportedSignatureException>(() => Inspect(f));
    }
    private static void Thread()
    {
        using var f = new FakeNative { IsStatic = true };
        Task.Run(() => Throws<InvalidOperationException>(() => Inspect(f))).GetAwaiter().GetResult();
        Check(f.Invocations == 0 && f.NewRoots == 0); Inspect(f);
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Static metadata assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
