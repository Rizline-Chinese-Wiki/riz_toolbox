namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeTweenSingleEventBridge
{
    // Only a transport fixture: it does not enroll in AdoptReady's singleton, manufacture
    // NativeFirstInstall, configure native memory or satisfy RequireProduction. The same
    // native ReadCurrent, frame validation, Delivery and parent-loan code is exercised.
    internal static NativeTweenSingleEventBridge CreateControlledForTests(IntPtr helper,
        NativeTweenSingleEventIdentity identity, Action verifyFixture)
    {
        ArgumentNullException.ThrowIfNull(verifyFixture);
        if (helper == IntPtr.Zero || !identity.IsComplete)
            throw new ArgumentException("A controlled Single fixture needs its actual loaded helper and complete native identity.");
        lock (Gate) return new(null, helper, identity, verifyFixture);
    }
}
