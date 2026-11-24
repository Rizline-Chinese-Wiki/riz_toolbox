namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Complete actual Canvas source vectors only. Keeps independent
    /// bounds and publishes each loaded reference before element inspection.</summary>
    internal NativeRootedReferenceArray ReadRootedCanvasSourceArray(RootHandle array, IntPtr arrayClass,
        IntPtr elementClass, int maximum, Action<int, IntPtr> publish, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard); guard();
        if (elementClass != Class("Assembly-CSharp.dll", "InLevel", "CanvasMove")
            && elementClass != Class("Assembly-CSharp.dll", "InLevel", "KeyPoint")
            && elementClass != Class("Assembly-CSharp.dll", "InLevel", "EaseInfor")
            && elementClass != Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform"))
            throw new UnsupportedSignatureException("Canvas reference vectors require their exact allowed source element class.");
        guard();
        return ReadRootedReferenceArrayCore(array, arrayClass, elementClass, maximum, 65536, publish, guard);
    }
}
