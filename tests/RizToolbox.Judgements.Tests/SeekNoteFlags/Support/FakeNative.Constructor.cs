#pragma warning disable CS0649 // Reused synthetic fixture fault knobs intentionally retain default values.
namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    private readonly IntPtr _constructor = new(32001);
    internal bool ConstructorAvailable, FailObjectAllocation;
    internal IntPtr ConstructorDeclaringClass, LastObject;
    internal uint ConstructorExtraFlags;
    internal int ObjectAllocations;
    internal bool AllConstructorCallsRooted = true;
    internal Action? OnObjectAllocation;
    private IntPtr ConstructorClass => ConstructorDeclaringClass == IntPtr.Zero ? TypeClass(Receiver) : ConstructorDeclaringClass;

    private IntPtr ClassParent(IntPtr cls)
    {
        if (_gameplayControllerType != IntPtr.Zero && cls == GameplayControllerClass) return _gameplayControllerParent;
        return ConstructorAvailable && cls == TypeClass(Receiver) && ConstructorClass != cls ? ConstructorClass : IntPtr.Zero;
    }

    private IntPtr NewObject(IntPtr cls)
    {
        ObjectAllocations++;
        OnObjectAllocation?.Invoke();
        if (FailObjectAllocation) return IntPtr.Zero;
        LastObject = Box(ClassType(cls), new byte[IntPtr.Size * 2]);
        return LastObject;
    }

    internal bool HasRootFor(IntPtr value) => _handles.Values.Contains(value);
}
