using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

internal static class AudioDemandLifecycleChecks
{
    private static readonly string[] SourceServices = { AudioContentCapabilityIds.Replacement, AudioContentCapabilityIds.Mix };
    internal static (string Name, Action Test)[] Cases => new (string, Action)[]
    {
        ("Audio module demand owns one backend and one scheduled discovery for three capabilities", SharedDiscovery),
        ("Audio withdrawal restores only the withdrawn mutation and preserves other consumers", IndependentWithdrawal),
        ("Last audio consumer revokes the view but retains an unconfirmed voice until native completion", PendingVoice)
    };

    private static void SharedDiscovery()
    {
        using var f = new DemandFixture();
        f.Step(0, Array.Empty<string>());
        Equal(0, f.Backends); Equal(0, f.Backend.Discoveries); Equal(0, f.Backend.Created.Count);

        f.Step(1, new[] { AudioContentCapabilityIds.Playback });
        f.Step(2, new[] { AudioContentCapabilityIds.Playback });
        Equal(1, f.Backends); Equal(0, f.Backend.Discoveries);

        var all = SourceServices.Append(AudioContentCapabilityIds.Playback).ToArray();
        f.Step(3, all); f.Step(3, all); f.Step(4, all); f.Step(7.9, all);
        Equal(1, f.Backend.Discoveries); Equal(1, f.Backends);
        // Maintenance cadence and discovery cadence are independent. A late
        // maintenance sample discovers once, with no per-capability catch-up.
        f.Step(9, all); f.Step(9, all);
        Equal(2, f.Backend.Discoveries);
        f.Step(10, Array.Empty<string>());
        f.Step(20, Array.Empty<string>());
        Equal(2, f.Backend.Discoveries);
    }

    private static void IndependentWithdrawal()
    {
        using var f = new Fixture(); using var ctx = f.Context();
        var src = f.Add(); f.Audio.Refresh();
        var audio = Need(f.Play(ctx).LoadWav(f.File)); var replacement = f.Replace(ctx); var mix = f.Mix(ctx);
        var target = f.Target(ctx);
        var clipLease = Need(replacement.Replace(target, audio.Asset));
        var volume = Need(mix.Override(target, AudioMixParameter.Volume, .4f));

        f.Audio.Disable(AudioContentCapabilityIds.Replacement);
        Equal(src.Original, src.Current); Check(!clipLease.IsActive); Check(volume.IsActive);
        Check(volume.SetValue(.5f).Success); Equal(.5f, src.Volume);
        Equal(ErrorCode.ScopeExpired, replacement.Replace(target, audio.Asset).Error!.Code);

        f.Audio.Enable(AudioContentCapabilityIds.Replacement);
        var recovered = f.Replace(ctx);
        Equal(target, Need(recovered.GetTargets()).Single().Target);
        var replacementAgain = Need(recovered.Replace(target, audio.Asset));
        f.Audio.Disable(AudioContentCapabilityIds.Mix);
        Equal(1f, src.Volume); Check(!volume.IsActive); Check(replacementAgain.IsActive);
        Equal(f.Backend.Created.Single(), src.Current);
    }

    private static void PendingVoice()
    {
        using var f = new DemandFixture();
        var playbackDemand = new[] { AudioContentCapabilityIds.Playback };
        f.Step(0, playbackDemand);
        var view = f.Context.GetService<IAudioPlaybackService>(AudioContentCapabilityIds.Playback).Service!;
        var clip = Need(view.LoadWav(f.File)); var playback = Need(view.Play(clip.Asset)); clip.Dispose();
        var nativeClip = f.Backend.Created.Last(); var source = f.Backend.Sources.Single();
        source.FailStop = true;
        f.Step(1, Array.Empty<string>()); f.Step(5, Array.Empty<string>());
        Equal(ErrorCode.ScopeExpired, view.LoadWav(f.File).Error!.Code);
        Check(!playback.IsActive); Equal(1, source.Stops); Equal(0, source.Disposed); Equal(0, nativeClip.Destroyed);

        f.Step(6, playbackDemand);
        Check(!f.Capabilities.GetCapability(AudioContentCapabilityIds.Playback).Available); Equal(1, f.Backends);
        for (int now = 36; now <= 246; now += 30) f.Step(now, playbackDemand);
        Equal(AdapterModuleState.Suspended, f.ModuleState(AudioContentCapabilityIds.Playback));
        source.Playing = false;
        f.Step(247, playbackDemand); f.Step(248, playbackDemand);
        Equal(1, source.Stops); Equal(1, source.Disposed); Equal(1, nativeClip.Destroyed);
        Equal(2, f.Backends); Check(f.Capabilities.GetCapability(AudioContentCapabilityIds.Playback).Available);
        Equal(ErrorCode.ScopeExpired, view.LoadWav(f.File).Error!.Code);
    }

    private sealed class DemandFixture : IDisposable
    {
        private readonly Fixture _files = new();
        private readonly AudioContentModules _audio;
        private readonly ModuleManager _modules;
        private readonly PluginHost _host;
        internal readonly CapabilityRegistry Capabilities = new(new("test", "test", "test", "test"));
        internal Backend Backend => _files.Backend;
        internal ContentFile File => _files.File;
        internal int Backends;
        internal IPluginContext Context = null!;

        internal DemandFixture()
        {
            var log = new Logger();
            _audio = AudioContentModules.Create(null!, _files.Main, log, _ => _files.Directory, _ => _files.Directory,
                audioBackend: () => { Backends++; return Backend; });
            _modules = new(Capabilities, new Symbols(), log, "test", "test");
            foreach (var module in _audio.Modules.Where(AudioContentModules.OwnsAudioCapability))
                _modules.Add(module, RizlineAdapter.IsDemandManaged(module));
            _host = new(Capabilities, _files.Main, new EventHub(log), log);
            var observer = new Observer(context => Context = context);
            Check(_host.Register(observer).Success); Check(_host.Enable(observer.Manifest.Id).Success);
        }

        internal void Step(double now, string[] capabilities)
        {
            _modules.SetCapabilityDemand(capabilities);
            _modules.Tick(now);
            _audio.Maintain(now, _modules);
        }

        internal AdapterModuleState ModuleState(string cap)
            => _modules.GetModules().Single(module => module.Manifest.Capabilities.Contains(cap)).State;

        public void Dispose() { _host.Dispose(); _modules.Dispose(); _files.Dispose(); }
    }

    private sealed class Symbols : IModuleSymbolResolver
    { public AdapterSymbolProbe Probe(AdapterSymbolRequirement symbol) => new(symbol.Id, true, symbol.Type, null); }

    private sealed class Observer : IRizPlugin
    {
        private readonly Action<IPluginContext> _enable;
        internal Observer(Action<IPluginContext> enable) => _enable = enable;
        public PluginManifest Manifest => new("test.audio-demand", new(1, 0), new(1, 0));
        public void Enable(IPluginContext context) => _enable(context);
        public void Disable() { }
    }
}
