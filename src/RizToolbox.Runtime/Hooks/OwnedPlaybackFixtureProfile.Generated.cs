// Generated from the reviewed native69 source family and complete owned PE facts.
// The native initial Class identities are checked before fixture bootstrap.
// Existing bootstrap may remap only MethodInfo.Class before target capture.
using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal static class OwnedPlaybackFixtureProfile
{
    internal const string WrapperSha256 = "f9c18f7fe4162e519c20b3304913911710a08b903c830ce0db5deb0c46df972a";
    internal const string LowerSha256 = "e836b4d816d3ce7f7e4dcad65c2a4cafffbf18876f6d5c5a9981f9ffdb63cccb";
    internal const string SourceFamilySha256 = "a6b8b9d9c59d509fdd153bf6d1895130b99f59cc8c0c58410fdac797937dde1a";
    internal const string LowerPitchBodySha256 = "42dfa0516a88bf1ee1cd6074498580bf87b569231c5eb2a984203a482c3d47c6";
    internal const string LowerPitchUnwindHex = "011e09001e680d001774210017341f0017011c000c500000";
    internal const string EnginePitchBodySha256 = "158c4d68bd1f31a9d4a9d318f77d8b4eb8de96a8ee95ec2806d7e52e3963cf68";
    internal const string LowerTimeBodySha256 = "2525714554563223e54859745e669c08cc2f42c0713080b82ae0e70113d260ef";
    internal const string WrapperTimeBodySha256 = "ee32ecb2138ec2a51f6ccdc04054299860c51f45ec2e33820f3db135104e48ec";
    internal const string PitchGetterBodySha256 = "da434e251bc28b762c32248d35b7acd6f9387eb1cd78b3a0e3545739f06be478";
    internal const string LowerTimeUnwindHex = "01120600126806000ad2065005341100";
    internal const string WrapperTimeUnwindHex = "0106020006520230";
    internal const int LowerPitchRva = 0x1860;
    internal const int EnginePitchRva = 0x15a0;
    internal const int EnginePart2Rva = 0x16f5;
    internal const int EnginePart3Rva = 0x1806;
    internal const int LowerTimeRva = 0x1a60;
    internal const int WrapperTimeRva = 0x3df0;
    internal const int PitchGetterRva = 0x23c0;
    internal const int LowerEngineCallRva = 0x1891;
    internal const int LowerEngineReturnRva = 0x1896;
    internal const uint PitchCacheRva = 0x9f18u;
    internal const uint TimeCacheRva = 0x9f20u;
    internal const uint LowerPitchUnwindRva = 0x3cc0u;
    internal const uint EngineRootUnwindRva = 0x3620u;
    internal const uint EnginePart2UnwindRva = 0x3638u;
    internal const uint EnginePart3UnwindRva = 0x364cu;
    internal const uint LowerTimeUnwindRva = 0x3cd8u;
    internal const uint WrapperTimeUnwindRva = 0x734cu;
    internal const uint TimeFunctionTableRva = 0x32e0u;
    internal const uint TimeNameTableRva = 0x3480u;
    internal const uint TimeFunctionSlotRva = 0x3470u;
    internal const uint TimeNameSlotRva = 0x3610u;
    internal const uint TimeRegisteredNameRva = 0x32b0u;
    private const ulong WrapperPreferredBase = 0x180000000ul;

    private sealed record InitialMethod(string Accessor, string Entry, uint MethodInfoRva,
        uint InitialClassRva, string BytesHex, int[] Dir64Offsets);

    private static readonly IReadOnlyDictionary<string, uint> WrapperExports = new Dictionary<string, uint>
    {
        ["StockAudioPlay"] = 0x3f20u,
        ["StockAudioPlayDelayed"] = 0x3ed0u,
        ["StockAudioPlayDelayedEnd"] = 0x3f20u,
        ["StockAudioPlayEnd"] = 0x3f60u,
        ["StockAudioPlayHelper"] = 0x3e80u,
        ["StockAudioPlayHelperEnd"] = 0x3ed0u,
        ["StockAudioSetLoop"] = 0x3ff0u,
        ["StockAudioSetLoopEnd"] = 0x4040u,
        ["StockAudioStop"] = 0x3f60u,
        ["StockAudioStopBoolean"] = 0x3fa0u,
        ["StockAudioStopBooleanEnd"] = 0x3ff0u,
        ["StockAudioStopEnd"] = 0x3fa0u,
        ["StockExecutableDir64Anchor"] = 0x4040u,
        ["StockFixtureBindLowerPitch"] = 0x29a0u,
        ["StockFixtureBindLowerTime"] = 0x29c0u,
        ["StockFixtureBlockGetTime"] = 0x1200u,
        ["StockFixtureConfigure"] = 0x1280u,
        ["StockFixtureConfigureConstructor"] = 0x1310u,
        ["StockFixtureConfigureDirect"] = 0x1320u,
        ["StockFixtureConfigureProgressResource"] = 0x2c50u,
        ["StockFixtureConfigureResource"] = 0x2c60u,
        ["StockFixtureConfigureStartWaitUntil"] = 0x3050u,
        ["StockFixtureConstructorRead"] = 0x1330u,
        ["StockFixtureCreateAudioClip"] = 0x2c70u,
        ["StockFixtureCreateForeignSharedReceiver"] = 0x13c0u,
        ["StockFixtureCreatePlayer"] = 0x1470u,
        ["StockFixtureCreateProgress"] = 0x1510u,
        ["StockFixtureCreateProgressResourceReceiver"] = 0x2cc0u,
        ["StockFixtureCreateResourceReference"] = 0x2d80u,
        ["StockFixtureCreateSource"] = 0x15a0u,
        ["StockFixtureCreateStart"] = 0x3070u,
        ["StockFixtureCreateStartWithoutResourceFallback"] = 0x30b0u,
        ["StockFixtureDirectRead"] = 0x1620u,
        ["StockFixtureGetBodyLength"] = 0x1690u,
        ["StockFixtureInvoke"] = 0x1780u,
        ["StockFixtureInvokeStart"] = 0x30c0u,
        ["StockFixtureMachineInvoke"] = 0x4050u,
        ["StockFixtureNativeGetPitch"] = 0x23c0u,
        ["StockFixtureProgressResourceRead"] = 0x2de0u,
        ["StockFixtureReleaseGetTime"] = 0x1890u,
        ["StockFixtureReleasePitch"] = 0x18b0u,
        ["StockFixtureResourceRead"] = 0x2e80u,
        ["StockFixtureSetProgressResource"] = 0x2ee0u,
        ["StockFixtureSetStartBoundary"] = 0x3140u,
        ["StockFixtureStartAllocationCount"] = 0x3150u,
        ["StockFixtureStartNoPlayerAllocationCount"] = 0x3160u,
        ["StockFixtureUnityCreateClass"] = 0x2400u,
        ["StockFixtureUnityCreateClip"] = 0x2510u,
        ["StockFixtureUnityDestroy"] = 0x25a0u,
        ["StockFixtureUnityInitializeWrapper"] = 0x2680u,
        ["StockFixtureUnityMutate"] = 0x2750u,
        ["StockFixtureUnityRead"] = 0x2850u,
        ["StockFixtureUnityRegisterStorage"] = 0x2950u,
        ["StockFixtureWaitGetTime"] = 0x18c0u,
        ["StockFixtureWaitPitch"] = 0x18e0u,
        ["StockGetTime"] = 0x3290u,
        ["StockGetTimeEnd"] = 0x3510u,
        ["StockLowerPitchCache"] = 0x9f18u,
        ["StockLowerPitchWrapperMarker"] = 0x6558u,
        ["StockLowerTimeCache"] = 0x9f20u,
        ["StockLowerTimeWrapperMarker"] = 0x6580u,
        ["StockMethod"] = 0x19f0u,
        ["StockMethodAudioPlay"] = 0x1af0u,
        ["StockMethodAudioPlayDelayed"] = 0x1b00u,
        ["StockMethodAudioPlayHelper"] = 0x1b10u,
        ["StockMethodAudioSetLoop"] = 0x1b20u,
        ["StockMethodAudioStop"] = 0x1b30u,
        ["StockMethodAudioStopBoolean"] = 0x1b40u,
        ["StockMethodGetTime"] = 0x1b50u,
        ["StockMethodPause"] = 0x1b60u,
        ["StockMethodPauseLeaf"] = 0x1b70u,
        ["StockMethodPlayScheduled"] = 0x1b80u,
        ["StockMethodPlayerConstructor"] = 0x1b90u,
        ["StockMethodProgressClip"] = 0x2ef0u,
        ["StockMethodProgressMusicPlayer"] = 0x2ab0u,
        ["StockMethodProgressPlay"] = 0x1ba0u,
        ["StockMethodReplay"] = 0x1bb0u,
        ["StockMethodResourceGet"] = 0x2f00u,
        ["StockMethodResume"] = 0x1bc0u,
        ["StockMethodSetClip"] = 0x1bd0u,
        ["StockMethodSetPitch"] = 0x1be0u,
        ["StockMethodSetPitchAlias"] = 0x1bf0u,
        ["StockMethodSetTime"] = 0x1c00u,
        ["StockMethodStartMoveNext"] = 0x3170u,
        ["StockPause"] = 0x3650u,
        ["StockPauseEnd"] = 0x36f0u,
        ["StockPauseLeaf"] = 0x3db0u,
        ["StockPauseLeafEnd"] = 0x3df0u,
        ["StockPlayScheduled"] = 0x3bf0u,
        ["StockPlayScheduledEnd"] = 0x3c50u,
        ["StockPlayerConstructor"] = 0x3e40u,
        ["StockPlayerConstructorEnd"] = 0x3e80u,
        ["StockProgressClip"] = 0x42c0u,
        ["StockProgressClipEnd"] = 0x42cdu,
        ["StockProgressClipTail"] = 0x2f10u,
        ["StockProgressMusicPlayer"] = 0x42b0u,
        ["StockProgressMusicPlayerEnd"] = 0x42bdu,
        ["StockProgressPlay"] = 0x3ca0u,
        ["StockProgressPlayEnd"] = 0x3db0u,
        ["StockProgressPlayerTail"] = 0x2ac0u,
        ["StockProgressResourceProfileMarker"] = 0x6630u,
        ["StockProgressWriterMode"] = 0x2b30u,
        ["StockProgressWriterRead"] = 0x2b40u,
        ["StockReplay"] = 0x36f0u,
        ["StockReplayEnd"] = 0x3920u,
        ["StockResourceGet"] = 0x2b80u,
        ["StockResume"] = 0x3920u,
        ["StockResumeEnd"] = 0x3bf0u,
        ["StockSetClip"] = 0x3c50u,
        ["StockSetClipEnd"] = 0x3ca0u,
        ["StockSetPitch"] = 0x3510u,
        ["StockSetPitchEnd"] = 0x3650u,
        ["StockSetTime"] = 0x3df0u,
        ["StockSetTimeEnd"] = 0x3e40u,
        ["StockStartClipReturned"] = 0x4347u,
        ["StockStartMoveNext"] = 0x42d0u,
        ["StockStartMoveNextEnd"] = 0x44a0u,
        ["StockStartResourceCall"] = 0x4336u,
        ["StockStartResourceLoaded"] = 0x4326u,
        ["StockStartResourceReturned"] = 0x4338u,
        ["StockUnityBackingProfileMarker"] = 0x6538u,
    };

    private static readonly IReadOnlyDictionary<string, uint> LowerExports = new Dictionary<string, uint>
    {
        ["StockEngineFixtureConfigure"] = 0x1200u,
        ["StockEngineFixtureRead"] = 0x1210u,
        ["StockEnginePitchPart2"] = 0x16f5u,
        ["StockEnginePitchPart3"] = 0x1806u,
        ["StockEngineResubmit"] = 0x1820u,
        ["StockEngineResubmitEnd"] = 0x1860u,
        ["StockEngineResubmitReturned"] = 0x1831u,
        ["StockEngineSetPitch"] = 0x15a0u,
        ["StockEngineSetPitchEnd"] = 0x1820u,
        ["StockLowerEngineReturned"] = 0x1896u,
        ["StockLowerExecutableDir64Anchor"] = 0x1a50u,
        ["StockLowerFixtureConfigure"] = 0x1000u,
        ["StockLowerFixtureRead"] = 0x1010u,
        ["StockLowerPitchFunctions"] = 0x3250u,
        ["StockLowerPitchNames"] = 0x3258u,
        ["StockLowerPitchProfileMarker"] = 0x3200u,
        ["StockLowerPitchRegisteredName"] = 0x3228u,
        ["StockLowerPitchRegistrationCount"] = 0x324cu,
        ["StockLowerSetPitch"] = 0x1860u,
        ["StockLowerSetPitchEnd"] = 0x1a4fu,
        ["StockLowerSetTime"] = 0x1a60u,
        ["StockLowerSetTimeEnd"] = 0x1b91u,
        ["StockLowerTimeExecutableDir64Anchor"] = 0x1b98u,
        ["StockLowerTimeFixtureConfigure"] = 0x1510u,
        ["StockLowerTimeFixtureRead"] = 0x1520u,
        ["StockLowerTimeFunctions"] = 0x32e0u,
        ["StockLowerTimeNames"] = 0x3480u,
        ["StockLowerTimeProfileMarker"] = 0x3288u,
        ["StockLowerTimeRegisteredName"] = 0x32b0u,
        ["StockLowerTimeRegistrationCount"] = 0x3280u,
        ["StockNativeEnginePitchMarker"] = 0x3260u,
    };

    private static readonly IReadOnlyDictionary<string, string> Bodies = new Dictionary<string, string>
    {
        ["StockAudioPlay"] = "c19566388c00525fccd6a781068da05ca24acf2a01ca61b146b4ac22ee5fcab0",
        ["StockAudioPlayDelayed"] = "4df8a57f6e4f530d3bee1fa9fea53adebeb61386a26e19e0eb84bfc624f4d24a",
        ["StockAudioPlayHelper"] = "7cf60b913b3fab4f5f5481a30c6bf86105d46784d3b456d32817db8ad860e238",
        ["StockAudioSetLoop"] = "1d483443f623aca2ba6aa5ef31da357de16667a6c9ee288cc2ce0c98ba4bbec8",
        ["StockAudioStop"] = "478296f21849dc41ad4a461cbeb0f16d35fe737f3c2d299cd009e78c1778e836",
        ["StockAudioStopBoolean"] = "c15aa916309c2b736f415dc484dd86a49efae052eebf9c1cc87a12645142e0cf",
        ["StockEngineResubmit"] = "cbfea944b93f0f7a630322a316f08451b8e6dab3fa4db8e2870888a82045a333",
        ["StockEngineSetPitch"] = "158c4d68bd1f31a9d4a9d318f77d8b4eb8de96a8ee95ec2806d7e52e3963cf68",
        ["StockGetTime"] = "59be976456e443dd9cdba75b70bfb75dcfa1ef1f18c18f9f8a33cdc7b1c5af78",
        ["StockLowerSetPitch"] = "42dfa0516a88bf1ee1cd6074498580bf87b569231c5eb2a984203a482c3d47c6",
        ["StockLowerSetTime"] = "2525714554563223e54859745e669c08cc2f42c0713080b82ae0e70113d260ef",
        ["StockPause"] = "a415c0965b29f8a7961da637d569d6dd7b84d3b35bd4f75536ef05412be3c81f",
        ["StockPauseLeaf"] = "71d20929dcb37c588668901aa5846ccb8c3ca07839f17e188652179e6dec4219",
        ["StockPlayScheduled"] = "7002c16e36023b206c3bb9f67f6ed1c261b002c8ff5e2768cbdf337c42e9ea67",
        ["StockPlayerConstructor"] = "a7f08148ec60c6e847e2afc420ed31f806001d5279727dc99b64bdc7f7226949",
        ["StockProgressClip"] = "e023e0f640b4b36f769b78f5101d0d5c68b78f4dae4e289f127c8dcd2d673303",
        ["StockProgressMusicPlayer"] = "023e6bb3d248573b46852d799204eb6319aa06413101a5832ab2a7f4f646bc78",
        ["StockProgressPlay"] = "dc6568242f2f4d75b468ea24d68d262298f358167257425a28c0b30eeec33064",
        ["StockReplay"] = "e0b0146d072acaec9bd41b4267d7f60d50ed2184787d285c59d0d7f8a60807f8",
        ["StockResume"] = "1d36b2c82982546a1fd368f8a2e9b123fa0664c248bd4008a68f7c3f8b743fe8",
        ["StockSetClip"] = "afeb36d74db8aace7d2018e35668a71c72cf83cf5649619ee453d88c4d2a38d9",
        ["StockSetPitch"] = "7eb907b297b304a258e6f2094cd8fc16bf5811fd4b7111bea310d06b1392375d",
        ["StockSetTime"] = "ee32ecb2138ec2a51f6ccdc04054299860c51f45ec2e33820f3db135104e48ec",
        ["StockStartMoveNext"] = "3389d766099b285eda0655341234d858ed08b6ba1b08c801b072e0172d5f613e",
    };

    private static readonly IReadOnlyDictionary<string, InitialMethod> Methods = new Dictionary<string, InitialMethod>
    {
        ["StockMethodAudioPlay"] = new("StockMethodAudioPlay", "StockAudioPlay", 0x9610u, 0x9ab0u, "203f008001000000203f0080010000000000000000000000fc64008001000000b09a00800100000028900080010000000000000000000000000000000000000000000000000000001210000686000000ffff000000000000", new[] { 0, 8, 24, 32, 40 }),
        ["StockMethodAudioPlayDelayed"] = new("StockMethodAudioPlayDelayed", "StockAudioPlayDelayed", 0x95b0u, 0x9ab0u, "d03e008001000000d03e0080010000000000000000000000fc64008001000000b09a00800100000028900080010000009090008001000000000000000000000000000000000000001110000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodAudioPlayHelper"] = new("StockMethodAudioPlayHelper", "StockAudioPlayHelper", 0x9550u, 0x9ab0u, "803e008001000000803e00800100000000000000000000001065008001000000b09a0080010000002890008001000000c090008001000000000000000000000000000000000000001010000696000000ffff020000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodAudioSetLoop"] = new("StockMethodAudioSetLoop", "StockAudioSetLoop", 0x9730u, 0x9ab0u, "f03f008001000000f03f00800100000000000000000000002865008001000000b09a00800100000028900080010000009890008001000000000000000000000000000000000000001510000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodAudioStop"] = new("StockMethodAudioStop", "StockAudioStop", 0x9670u, 0x9ab0u, "603f008001000000603f00800100000000000000000000001c65008001000000b09a00800100000028900080010000000000000000000000000000000000000000000000000000001310000686000000ffff000000000000", new[] { 0, 8, 24, 32, 40 }),
        ["StockMethodAudioStopBoolean"] = new("StockMethodAudioStopBoolean", "StockAudioStopBoolean", 0x96d0u, 0x9ab0u, "a03f008001000000a03f00800100000000000000000000001c65008001000000b09a00800100000028900080010000009890008001000000000000000000000000000000000000001410000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodGetTime"] = new("StockMethodGetTime", "StockGetTime", 0x90d0u, 0x9008u, "9032008001000000903200800100000000000000000000008064008001000000089000800100000018900080010000000000000000000000000000000000000000000000000000000010000686000000ffff000000000000", new[] { 0, 8, 24, 32, 40 }),
        ["StockMethodPause"] = new("StockMethodPause", "StockPause", 0x9250u, 0x9008u, "503600800100000050360080010000000000000000000000b464008001000000089000800100000028900080010000000000000000000000000000000000000000000000000000000310000686000000ffff000000000000", new[] { 0, 8, 24, 32, 40 }),
        ["StockMethodPauseLeaf"] = new("StockMethodPauseLeaf", "StockPauseLeaf", 0x9490u, 0x9ab0u, "b03d008001000000b03d0080010000000000000000000000b464008001000000b09a00800100000028900080010000000000000000000000000000000000000000000000000000000910000686000000ffff000000000000", new[] { 0, 8, 24, 32, 40 }),
        ["StockMethodPlayScheduled"] = new("StockMethodPlayScheduled", "StockPlayScheduled", 0x9370u, 0x9ab0u, "f03b008001000000f03b0080010000000000000000000000e064008001000000b09a00800100000028900080010000009090008001000000000000000000000000000000000000000610000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodPlayerConstructor"] = new("StockMethodPlayerConstructor", "StockPlayerConstructor", 0x94f0u, 0x9008u, "403e008001000000403e0080010000000000000000000000046500800100000008900080010000002890008001000000a890008001000000000000000000000000000000000000000c10000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodProgressClip"] = new("StockMethodProgressClip", "StockProgressClip", 0x9880u, 0x9830u, "c042008001000000c04200800100000000000000000000005866008001000000309800800100000048980080010000007898008001000000000000000000000000000000000000001610000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodProgressMusicPlayer"] = new("StockMethodProgressMusicPlayer", "StockProgressMusicPlayer", 0x97d0u, 0x9790u, "b042008001000000b042008001000000000000000000000020660080010000009097008001000000a097008001000000c097008001000000000000000000000000000000000000000a10000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodProgressPlay"] = new("StockMethodProgressPlay", "StockProgressPlay", 0x9430u, 0x9010u, "a03c008001000000a03c0080010000000000000000000000fc64008001000000109000800100000028900080010000009890008001000000000000000000000000000000000000000810000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodReplay"] = new("StockMethodReplay", "StockReplay", 0x92b0u, 0x9008u, "f036008001000000f0360080010000000000000000000000c064008001000000089000800100000028900080010000007890008001000000000000000000000000000000000000000410000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodResourceGet"] = new("StockMethodResourceGet", "StockResourceGet", 0x98e0u, 0x9838u, "802b008001000000802b00800100000000000000000000006866008001000000389800800100000068980080010000000000000000000000000000000000000000000000000000001710000686000000ffff000000000000", new[] { 0, 8, 24, 32, 40 }),
        ["StockMethodResume"] = new("StockMethodResume", "StockResume", 0x9310u, 0x9008u, "203900800100000020390080010000000000000000000000d064008001000000089000800100000028900080010000007890008001000000000000000000000000000000000000000510000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodSetClip"] = new("StockMethodSetClip", "StockSetClip", 0x93d0u, 0x9ab0u, "503c008001000000503c0080010000000000000000000000f064008001000000b09a0080010000002890008001000000a090008001000000000000000000000000000000000000000710000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodSetPitch"] = new("StockMethodSetPitch", "StockSetPitch", 0x9190u, 0x9ab0u, "1035008001000000103500800100000000000000000000009864008001000000b09a00800100000028900080010000008090008001000000000000000000000000000000000000000110000696000000ffff020000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodSetPitchAlias"] = new("StockMethodSetPitchAlias", "StockSetPitch", 0x91f0u, 0x9ab0u, "103500800100000010350080010000000000000000000000a864008001000000b09a00800100000028900080010000007890008001000000000000000000000000000000000000000210000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodSetTime"] = new("StockMethodSetTime", "StockSetTime", 0x9130u, 0x9ab0u, "f03d008001000000f03d00800100000000000000000000008864008001000000b09a00800100000028900080010000007890008001000000000000000000000000000000000000000a10000686000000ffff010000000000", new[] { 0, 8, 24, 32, 40, 48 }),
        ["StockMethodStartMoveNext"] = new("StockMethodStartMoveNext", "StockStartMoveNext", 0x9960u, 0x9940u, "d042008001000000d04200800100000000000000000000007866008001000000409900800100000048990080010000000000000000000000000000000000000000000000000000000b10000681000000ffff000000000000", new[] { 0, 8, 24, 32, 40 }),
    };

    internal static string BodySha256(string export) => Bodies.TryGetValue(export, out var value)
        ? value : throw new UnsupportedSignatureException("Unknown closed owned body.");

    internal static void RequireExports(IntPtr wrapper, IntPtr lower)
    {
        foreach (var row in WrapperExports) RequireExport(wrapper, row.Key, row.Value);
        foreach (var row in LowerExports) RequireExport(lower, row.Key, row.Value);
    }

    private static void RequireExport(IntPtr module, string name, uint rva)
    {
        if (module == IntPtr.Zero || NativeLibrary.GetExport(module, name) != IntPtr.Add(module, checked((int)rva)))
            throw new UnsupportedSignatureException("A closed owned export changed its exact RVA.");
    }

    internal static void RequireMethodIdentity(IntPtr wrapper, string accessor, string entry, IntPtr method, bool initial = false)
    {
        if (!Methods.TryGetValue(accessor, out var row) || row.Entry != entry
            || method != IntPtr.Add(wrapper, checked((int)row.MethodInfoRva)))
            throw new UnsupportedSignatureException("Unknown owned MethodInfo identity.");
        RequireExport(wrapper, accessor, WrapperExports[accessor]);
        RequireExport(wrapper, entry, WrapperExports[entry]);
        HookTargetValidation.VerifyReadable(method, 88);
        var expected = Convert.FromHexString(row.BytesHex);
        var delta = unchecked((ulong)wrapper.ToInt64() - WrapperPreferredBase);
        foreach (var offset in row.Dir64Offsets)
            BitConverter.GetBytes(unchecked(BitConverter.ToUInt64(expected, offset) + delta)).CopyTo(expected, offset);
        var actual = NativePlaybackExecutableSeal.RangeSeal.Read(method, 88);
        for (var index = 0; index < actual.Length; ++index)
        {
            if (!initial && index >= 32 && index < 40) continue;
            if (actual[index] != expected[index])
                throw new UnsupportedSignatureException("Owned MethodInfo changed a fixed non-Class field.");
        }
        var declaringClass = Marshal.ReadIntPtr(method, 32);
        if (declaringClass == IntPtr.Zero || initial && declaringClass != IntPtr.Add(wrapper, checked((int)row.InitialClassRva)))
            throw new UnsupportedSignatureException("Owned MethodInfo Class identity differs.");
    }

    internal static void RequireInitialMetadata(IntPtr wrapper)
    {
        foreach (var row in Methods.Values)
            RequireMethodIdentity(wrapper, row.Accessor, row.Entry, IntPtr.Add(wrapper, checked((int)row.MethodInfoRva)), true);
    }

    internal static int CallerReturnRva(NativePlaybackSourceRole parent, NativePlaybackSourceRole leaf) => (parent, leaf) switch
    {
        (NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.PauseLeaf) => 0x1c37,
        (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.AudioStop) => 0x1f74,
        (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.SetPitch) => 0x1fa2,
        (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.PlayScheduled) => 0x1fb0,
        (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.AudioStop) => 0x2036,
        (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetTime) => 0x204e,
        (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetPitch) => 0x2076,
        (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.PlayScheduled) => 0x2084,
        (NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceRole.PauseLeaf) => 0x1ee8,
        (NativePlaybackSourceRole.ProgressStartMoveNext, NativePlaybackSourceRole.SetClip) => 0x4347,
        _ => throw new UnsupportedSignatureException("No closed owned physical parent/leaf CALL."),
    };
}
