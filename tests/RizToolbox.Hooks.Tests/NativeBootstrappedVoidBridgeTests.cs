using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Managed ABI and identity rejection checks. The actual native
/// pre-Attach invocation, observer snapshot and admission paths have a separate
/// joint fixture; these checks do not claim instruction publication coverage.</summary>
internal static class NativeBootstrappedVoidBridgeTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Bootstrap adoption preserves the exact native 56 byte information and 40 byte observer layouts", Layout),
        ("Bootstrap adoption refuses mismatched identities unknown flags and observer attachment states", Identity),
        ("Bootstrap adoption rejects incomplete caller identity before loading a native library", IncompleteIdentity)
    };
    private static void Check(bool value, string message = "Bootstrap adoption check failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Layout()
    {
        Check(Marshal.SizeOf<NativeVoidBootstrapInformation>() == 56);
        foreach (var (name, offset) in new[] { (nameof(NativeVoidBootstrapInformation.Slot), 8),
            (nameof(NativeVoidBootstrapInformation.Attached), 12), (nameof(NativeVoidBootstrapInformation.Target), 16),
            (nameof(NativeVoidBootstrapInformation.Original), 24), (nameof(NativeVoidBootstrapInformation.Entry), 32),
            (nameof(NativeVoidBootstrapInformation.Epoch), 40), (nameof(NativeVoidBootstrapInformation.Unknown), 48),
            (nameof(NativeVoidBootstrapInformation.Reserved), 52) })
            Check(Marshal.OffsetOf<NativeVoidBootstrapInformation>(name).ToInt32() == offset, "Bootstrap information offset changed: " + name);
        var observer = typeof(NativeBootstrappedVoidBridge).GetNestedType("Observer", BindingFlags.NonPublic)!;
        Check(Marshal.SizeOf(observer) == 40 && Marshal.OffsetOf(observer, "Enter").ToInt32() == 8
            && Marshal.OffsetOf(observer, "Exit").ToInt32() == 16 && Marshal.OffsetOf(observer, "Mid").ToInt32() == 24
            && Marshal.OffsetOf(observer, "User").ToInt32() == 32);
        Check(typeof(NativeBootstrappedVoidBridge).GetConstructors().Length == 0,
            "Bootstrap bridge acquired a public constructor that bypasses exact identity adoption.");
    }
    private static NativeVoidBootstrapInformation Information() => new() { Version = 1, Size = 56, Slot = 7,
        Target = new(0x12340), Original = new(0x23450), Entry = new(0x34560), Epoch = 123 };
    private static bool Matches(NativeVoidBootstrapInformation info, bool attached = false, bool pinEntry = true)
        => NativeBootstrappedVoidBridge.MatchesInformation(info, 7, new(0x12340), new(0x23450), 123,
            pinEntry ? new(0x34560) : IntPtr.Zero, attached);
    private static void Identity()
    {
        Check(Matches(Information()) && Matches(Information(), pinEntry: false));
        var attached = Information(); attached.Attached = 1;
        Check(!Matches(attached) && Matches(attached, attached: true) && !Matches(Information(), attached: true));
        foreach (var field in new[] { "version", "size", "slot", "attached", "target", "original", "entry", "epoch", "unknown", "unknownvalue", "reserved" })
        {
            var info = Information();
            switch (field) {
                case "version": info.Version = 2; break; case "size": info.Size = 48; break;
                case "slot": info.Slot++; break; case "attached": info.Attached = 2; break;
                case "target": info.Target += 1; break; case "original": info.Original += 1; break;
                case "entry": info.Entry += 1; break; case "epoch": info.Epoch++; break;
                case "unknown": info.Unknown = 1; break; case "unknownvalue": info.Unknown = 9; break;
                default: info.Reserved = 1; break;
            }
            Check(!Matches(info), "Mismatched bootstrap identity was accepted: " + field);
        }
        var missingEntry = Information(); missingEntry.Entry = IntPtr.Zero;
        Check(!Matches(missingEntry, pinEntry: false), "An initially null native entry was accepted.");
    }
    private static void IncompleteIdentity()
    {
        var path = Path.GetFullPath("bootstrap-helper-must-not-load.dll");
        foreach (var field in new[] { "slot", "target", "original", "epoch", "relative" })
        {
            var rejected = false;
            try { NativeBootstrappedVoidBridge.AdoptBootstrap(field == "relative" ? "relative-helper.dll" : path,
                field == "slot" ? 32u : 0u, field == "target" ? IntPtr.Zero : new(1),
                field == "original" ? IntPtr.Zero : new(2), field == "epoch" ? 0ul : 1ul, new Logger()); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Incomplete bootstrap identity reached native loading: " + field);
        }
    }
    private sealed class Logger : ILogger
    { public void Log(LogLevel level, string message, Exception? exception = null) { } }
}
