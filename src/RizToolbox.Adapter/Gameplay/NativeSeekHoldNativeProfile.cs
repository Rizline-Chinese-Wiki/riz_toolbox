using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>The two Hold fields must name the same actual closed NoteInformation List.</summary>
internal interface ISeekHoldNativeProfile : ISeekTargetNativeProfile
{
    IntPtr NoteListClass { get; }
    IntPtr ClearMethod { get; }
    IntPtr AddMethod { get; }
}

/// <summary>Composes the frozen target profile and gates exact closed List metadata
/// against complete native bodies. Construction does not invoke methods.</summary>
internal sealed class NativeSeekHoldNativeProfile : ISeekHoldNativeProfile
{
    internal const string Fingerprint = NativeSeekTargetNativeProfile.Fingerprint;
    internal const bool RuntimeHoldStateVerified = false;
    internal const bool OfflineClosedMethodEntryAssociationObserved = false;
    internal const bool EngineConsumerClosureKnown = false;
    internal const bool ProductionAdmissionKnown = false;
    internal const string Evidence = "Pinned GameAssembly " + Fingerprint
        + "; composes the five complete target bodies, 74 fields and two constants."
        + " Both actual Hold fields must yield one exact closed mscorlib List<InLevel.NoteInformation>,"
        + " with official backing/count/version layout and exact current Clear/Add MethodInfo."
        + " Closed Clear must match the stock MethodInfo RVA and slot. Add must match the pinned reference candidate."
        + " Both entries and complete bodies are checked at construction and every verification."
        + " No invocation, Transform, Hold.Update, event, engine-exit or complete Seek closure is established.";

    private const string ListType = "System.Collections.Generic.List<InLevel.NoteInformation>";
    private const int ClearRva = 0x9800f0;
    private const ushort ClearSlot = 22;
    private const int AddRva = 0x9d7710;
    private const ushort AddSlot = 11;
    private readonly NativeRuntime _api;
    private readonly ISeekTargetNativeProfile _target;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IntPtr _module;
    private readonly string _path;
    private readonly NativeMetadataField _holdNotes, _runningHoldNotes;
    private readonly NativeMetadataClass _listClass, _arrayClass;
    private readonly NativeNoteJudgeListLayout _layout;
    private readonly NativeMetadataMethod _clear, _add;
    private readonly List<NativeExactReadField> _fields = new();
    private int _checking, _faulted;

    public IntPtr ChartClass => _target.ChartClass;
    public IntPtr ProgressClass => _target.ProgressClass;
    public IntPtr InformationClass => _target.InformationClass;
    public IntPtr NoteClass => _target.NoteClass;
    public IntPtr JudgeClass => _target.JudgeClass;
    public IntPtr HoldClass => _target.HoldClass;
    public IntPtr ChallengeTimeClass => _target.ChallengeTimeClass;
    public IntPtr NoteListClass { get; }
    public IntPtr ClearMethod => _clear.MethodInfo;
    public IntPtr AddMethod => _add.MethodInfo;

    internal NativeSeekHoldNativeProfile(NativeRuntime api, ISeekTargetNativeProfile targetProfile)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(targetProfile);
        _api = api; _target = targetProfile;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new UnsupportedSignatureException("Hold source evidence requires the pinned Windows x64 image.");
        _module = GetModuleHandle("GameAssembly.dll");
        if (_module == IntPtr.Zero) throw new UnsupportedSignatureException("The actual Hold source image is absent.");
        var path = new StringBuilder(32768);
        var count = GetModuleFileName(_module, path, path.Capacity);
        if (count == 0 || count >= path.Capacity)
            throw new UnsupportedSignatureException("The mapped Hold source image path is unavailable.");
        _path = path.ToString();
        using (var file = File.OpenRead(_path))
        using (var hash = SHA256.Create())
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The Hold source image differs from the pinned evidence.");

        _target.VerifyCurrent();
        _holdNotes = _api.InspectMetadataFieldClass(HoldClass, "holdNotes", ListType);
        _runningHoldNotes = _api.InspectMetadataFieldClass(HoldClass, "runningHoldNotes", ListType);
        NoteListClass = _holdNotes.FieldClass.Class;
        if (_runningHoldNotes.FieldClass.Class != NoteListClass)
            throw new UnsupportedSignatureException("The two actual Hold fields name different closed List classes.");
        _listClass = _api.InspectMetadataClass(NoteListClass);
        _layout = _api.InspectNoteJudgeListClass(NoteListClass);
        _arrayClass = _api.InspectMetadataClass(_layout.ArrayClass);
        if (_layout.ElementClass != NoteClass || _layout.BackingField.Offset != 0x10
            || _layout.SizeField.Offset != 0x18 || _layout.VersionField.Offset != 0x1c)
            throw new UnsupportedSignatureException("The actual Hold List layout differs from the pinned shared bodies.");
        Field(HoldClass, "holdNotes", ListType, 0x28);
        Field(HoldClass, "runningHoldNotes", ListType, 0x30);
        Field(HoldClass, "_nowTime", "System.Single", 0x38);
        Field(HoldClass, "_speed", "System.Single", 0x3c);
        Field(HoldClass, "_holdLineLength", "System.Single", 0x40);
        Field(NoteClass, "lastFrameHoldLineLength", "System.Single", 0x50);
        Field(NoteClass, "isStaticNote", "System.Boolean", 0x54);
        Field(NoteListClass, "_items", "InLevel.NoteInformation[]", 0x10);
        Field(NoteListClass, "_size", "System.Int32", 0x18);
        Field(NoteListClass, "_version", "System.Int32", 0x1c);

        // Bind both methods to the closed NoteInformation list observed in the stock game.
        _clear = InspectListMethod("Clear", ClearRva, Array.Empty<string>(), ClearSlot);
        _add = InspectListMethod("Add", AddRva, new[] { "InLevel.NoteInformation" }, AddSlot);
        VerifyCurrent();
    }

    private void Field(IntPtr cls, string name, string type, int offset)
        => _fields.Add(_api.InspectExactDeclaredReadField(cls, name, type, false, offset));

    private NativeMetadataMethod InspectListMethod(string name, int pinnedRva, string[] parameters, ushort? pinnedSlot = null)
    {
        var method = _api.InspectDeclaredMethod(NoteListClass, name, parameters, "System.Void", _module);
        if (method.DeclaringClass != _listClass)
            throw new UnsupportedSignatureException($"Hold List.{name} is declared on a different closed class ({method.DeclaringClass.TypeName}).");
        if ((method.Flags & (0x10u | 0x400u)) != 0 || method.GenericMethod && !method.InflatedMethod)
            throw new UnsupportedSignatureException($"Hold List.{name} has incompatible metadata (flags=0x{method.Flags:x}, generic={method.GenericMethod}, inflated={method.InflatedMethod}).");
        if (!Bodies.Any(body => body.Rva == pinnedRva))
            throw new UnsupportedSignatureException($"Hold List.{name} has no pinned body for RVA 0x{pinnedRva:x}.");
        if (method.Entry is not { } entry)
            throw new UnsupportedSignatureException($"Hold List.{name} has no verified entry for pinned RVA 0x{pinnedRva:x}. {method.EntryStatus}");
        if (entry.Rva != pinnedRva)
            throw new UnsupportedSignatureException($"Hold List.{name} entry RVA 0x{entry.Rva:x} differs from pinned RVA 0x{pinnedRva:x} (slot={entry.MethodInfoSlot}).");
        if (pinnedSlot is ushort expectedSlot && entry.MethodInfoSlot != expectedSlot)
            throw new UnsupportedSignatureException($"Hold List.{name} MethodInfo slot {entry.MethodInfoSlot} differs from pinned slot {expectedSlot}.");
        if (parameters.Length == 1
            && _api.InspectMethodParameterClass(method.MethodInfo, 0, "InLevel.NoteInformation").Class != NoteClass)
            throw new UnsupportedSignatureException("The actual closed Hold List.Add parameter has another exact class.");
        return method;
    }

    public void VerifyCurrent()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Hold source profile must be verified on its owner thread.");
        if (Volatile.Read(ref _faulted) != 0)
            throw new UnsupportedSignatureException("The Hold source profile has been withdrawn.");
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            throw new InvalidOperationException("Hold source verification cannot reenter.");
        try
        {
            _target.VerifyCurrent(); VerifyModule();
            if (_api.InspectMetadataFieldClass(HoldClass, "holdNotes", ListType) != _holdNotes
                || _api.InspectMetadataFieldClass(HoldClass, "runningHoldNotes", ListType) != _runningHoldNotes
                || _api.InspectMetadataClass(NoteListClass) != _listClass
                || _api.InspectMetadataClass(_layout.ArrayClass) != _arrayClass
                || _api.InspectNoteJudgeListClass(NoteListClass) != _layout)
                throw new UnsupportedSignatureException("The exact closed Hold List class/layout changed.");
            foreach (var expected in _fields)
                if (_api.InspectExactDeclaredReadField(expected.DeclaringClass, expected.Name, expected.TypeName,
                    false, expected.Offset) != expected)
                    throw new UnsupportedSignatureException("Hold source field metadata changed: " + expected.Name);
            VerifyMethod(_clear, ClearRva, ClearSlot);
            VerifyMethod(_add, AddRva, AddSlot);
            foreach (var body in Bodies)
            {
                var address = IntPtr.Add(_module, body.Rva);
                HookTargetValidation.VerifyExecutable(address, _module);
                HookTargetValidation.VerifyReadable(address, (nuint)body.Length);
                var bytes = new byte[body.Length]; Marshal.Copy(address, bytes, 0, bytes.Length);
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(body.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("A complete Hold List source body changed: " + body.Name);
            }
            VerifyModule(); _target.VerifyCurrent();
        }
        catch { Interlocked.Exchange(ref _faulted, 1); throw; }
        finally { Volatile.Write(ref _checking, 0); }
    }

    private void VerifyMethod(NativeMetadataMethod expected, int pinnedRva, ushort? pinnedSlot = null)
    {
        var current = InspectListMethod(expected.Name, pinnedRva, expected.ParameterTypes.ToArray(), pinnedSlot);
        if (current.MethodInfo != expected.MethodInfo || current.DeclaringClass != expected.DeclaringClass
            || current.Entry != expected.Entry || current.Flags != expected.Flags
            || current.ImplementationFlags != expected.ImplementationFlags
            || current.GenericMethod != expected.GenericMethod || current.InflatedMethod != expected.InflatedMethod)
            throw new UnsupportedSignatureException("The exact closed Hold List MethodInfo changed: " + expected.Name);
    }

    private void VerifyModule()
    {
        if (GetModuleHandle("GameAssembly.dll") != _module || GetModuleHandle(_path) != _module)
            throw new UnsupportedSignatureException("The Hold source module changed or was unloaded.");
    }

    internal sealed record Body(string Name, int Rva, int Length, string Sha256);
    // Clear is a frameless body through the return at RVA 0x980111. It tail-jumps
    // to the pinned System.Array.Clear for nonempty Lists. The other spans use
    // PE runtime-function bounds. See scripts/verify-seek-hold-clear.py.
    internal static IReadOnlyList<Body> Bodies { get; } = Array.AsReadOnly(new Body[]
    {
        new("ClosedNoteList.Clear", 0x9800f0, 34, "6a2c4b70d127ba1bfb0988c205d8bb210cb4e622a967501a667a3d17d61fe951"),
        new("ClosedNoteList.Add", 0x9d7710, 97, "0596c0f5ab09172e65a0b7786a5d7026573dd4175183f2f24a9d4997122f4ea7"),
        new("SharedList.ReferenceAddWithResize", 0x9d73f0, 213, "747508cf69ce0ae7e90041724baebf0dcae60e3759476f458c7e697d035c6477"),
        new("SharedList.ReferenceSetCapacity", 0xb1600, 280, "5bf50a16f106dbbbabd8559378697bbd3b3c3d6634a1efdcfdcd91a54e79600e"),
        new("System.Array.Clear", 0xf29590, 390, "6cb1af4f3bff505b268e84f2567dacba10105a9d346e4ec8d83ca60005ec61ed"),
    });

    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
