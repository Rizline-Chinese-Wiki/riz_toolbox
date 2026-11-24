namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackLowerPitchTarget
{
    // The one current owned pair is fixed from the independently built DLLs
    // and corrected static-profile-r2. Markers do not select another profile.
    // Stock file pins are independent of these owned fixture identities.
    internal const string OwnedWrapperImageSha256 = OwnedPlaybackFixtureProfile.WrapperSha256;
    internal const string OwnedLowerImageSha256 = OwnedPlaybackFixtureProfile.LowerSha256;
    internal const string LegacyOwnedWrapperImageSha256 = "b2d72147e825fb4dd1d9ebbcc9e259a961817a41f252c426d598dd12b30acb91";
    internal const string LegacyOwnedLowerImageSha256 = "78dcda29a0e2f897d5c7e4c796b1e2e39a38bcafeaf0850fbedc4295cd6fefeb";
    private const string OwnedLowerBodySha256 = OwnedPlaybackFixtureProfile.LowerPitchBodySha256;
    private const string OwnedLowerUnwindHex = OwnedPlaybackFixtureProfile.LowerPitchUnwindHex;
}
