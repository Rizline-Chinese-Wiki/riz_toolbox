using RizToolbox.Core.Diagnostics;
using RizToolbox.Core.Mutation;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

internal sealed partial class GamePostProcessingModule : IAdapterModule
{
    private const string Capability = GamePostProcessingCapabilityIds.PostProcessing;
    internal const int Minimum = 0, Maximum = 2;
    private readonly IMainThreadDispatcher _main;
    private readonly SemanticTargetsModule _shared;
    private readonly ILogger _log;
    private readonly Func<IGameBlurBackend> _create;
    private readonly string _fingerprint;
    private IGameBlurBackend? _backend;
    private object? _registration;
    private Binding? _binding;
    private OperationError? _unavailable;

    private sealed record Binding(IGameBlurTarget Source, TargetHandle<int> Handle, MutationChannel<int> Channel);

    internal GamePostProcessingModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        SemanticTargetsModule shared, string fingerprint)
        : this(main, log, shared, fingerprint, () => new NativeGameBlurBackend(api)) { }

    internal GamePostProcessingModule(IMainThreadDispatcher main, ILogger log, SemanticTargetsModule shared,
        string fingerprint, Func<IGameBlurBackend> create)
        => (_main, _log, _shared, _fingerprint, _create) = (main, log, shared, fingerprint, create);

    public AdapterModuleManifest Manifest { get; } = new("rizline.game-post-processing", new(1, 0),
        new[] { Capability }, new[] { TargetCapabilityIds.Targets, TargetCapabilityIds.Mutations },
        new[] { "GameStart", "MainMenu" }, NativeGameBlurBackend.Requirements,
        "Experimental 0–2 integer downsample for the active Main Camera TranslucentImageSource. " +
        "Pinned Steam 25977441 metadata and serialized scene evidence. Final rendered output awaits game validation.");
    public double IntervalSeconds => 0.25;

    public ModuleProbe Probe()
    {
        _main.VerifyAccess();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _fingerprint != NativeGameBlurBackend.Fingerprint)
            return ModuleProbe.Unsupported("The game post-processing target requires the pinned Windows x64 game build.");
        if (_backend is null)
        {
            _backend = _create();
            _registration = new();
        }
        return Tick();
    }

    public void Register(ModuleRegistration registration)
    {
        var identity = _registration ?? throw new InvalidOperationException("The game post-processing provider is inactive.");
        registration.RegisterScoped<IGamePostProcessingService>(Capability, context => new View(this, identity, context));
    }

    public ModuleProbe Tick()
    {
        _main.VerifyAccess();
        if (_backend is null) return ModuleProbe.Waiting("The game post-processing provider is inactive.");
        if (_ticking) return ModuleProbe.Available;
        _ticking = true;
        IGameBlurTarget? candidate = null;
        try
        {
            var registration = _registration;
            candidate = _backend.Discover();
            if (!ReferenceEquals(registration, _registration)) return ModuleProbe.Waiting("The post-processing provider ended during discovery.");
            if (_binding is { } previous)
            {
                if (candidate?.Identity != previous.Source.Identity || !previous.Source.IsCurrent()) Retire();
                else if (!previous.Channel.Valid)
                {
                    StopInvalidatedIntents(previous.Handle, Error(ErrorCode.Conflict, "The selected blur channel lost ownership."));
                    Retire();
                }
            }
            if (!ReferenceEquals(registration, _registration)) return ModuleProbe.Waiting("The post-processing provider ended during target retirement.");
            if (_binding is { } current)
            {
                var refresh = current.Channel.Refresh();
                if (!refresh.Success && !current.Channel.Valid)
                    StopInvalidatedIntents(current.Handle, refresh.Error! with { CapabilityId = Capability });
            }
            else if (candidate is not null)
            {
                var source = candidate;
                candidate = null;
                Bind(source, registration);
            }
            _unavailable = _binding is null ? Error(ErrorCode.TargetMissing, "The current scene has no eligible Main Camera blur component.", true) : null;
        }
        catch (AdapterOperationException error)
        {
            Retire(); _unavailable = error.Error with { CapabilityId = Capability };
        }
        catch (Exception error)
        {
            Retire(); _unavailable = Error(ErrorCode.ObservationUnavailable, error.Message, true);
            _log.TryLog(LogLevel.Debug, "Main Camera blur observation could not be completed.", error);
        }
        finally
        {
            try { candidate?.Dispose(); ReconcileIntents(); }
            finally { _ticking = false; }
        }
        return ModuleProbe.Available;
    }

    private void Bind(IGameBlurTarget source, object? registration)
    {
        var role = GamePostProcessingRoles.MainCameraBlurDownsample;
        int Read()
        {
            var value = source.Read();
            if (value is < Minimum or > Maximum)
                throw new UnsupportedSignatureException("The actual blur baseline is outside the supported downsample range.");
            return value;
        }
        var registered = _shared.Targets.Register(role + "/" + source.Identity.Scene, Read,
            source.IsCurrent, source, new(Scene: source.Identity.Scene, Role: role, ResourceId: role,
                Description: "Active main-camera blur texture downsample exponent (0–2)."), new[] { role });
        if (!registered.Success)
        {
            source.Dispose();
            throw new AdapterOperationException(registered.Error!);
        }
        try
        {
            var channel = _shared.Mutations.Register(registered.Value!, GamePostProcessingRoles.DownsampleProperty,
                source.Write, MutationStrategy.PriorityOverride, ExternalWritePolicy.PreserveExternalAndInvalidate,
                value => value is >= Minimum and <= Maximum ? null : "Downsample must be an integer from 0 to 2.");
            if (!channel.Success) throw new AdapterOperationException(channel.Error!);
            if (!ReferenceEquals(registration, _registration))
                throw new ObjectDisposedException(nameof(GamePostProcessingModule));
            _binding = new(source, registered.Value!, channel.Value!);
        }
        catch { _shared.Targets.Invalidate(registered.Value!); throw; }
    }

    private void Retire()
    {
        var binding = _binding;
        if (binding is null) return;
        _binding = null;
        try { binding.Channel.Dispose(); }
        finally { _shared.Targets.Invalidate(binding.Handle); }
    }

    public void Release()
    {
        _main.VerifyAccess();
        _registration = null;
        _backend = null;
        _unavailable = null;
        try { StopIntents(); }
        finally { Retire(); }
    }

    private static OperationError Error(ErrorCode code, string message, bool retryable = false)
        => new(code, message, Capability, GamePostProcessingRoles.MainCameraBlurDownsample, retryable);

    private sealed class View : IGamePostProcessingService
    {
        private readonly GamePostProcessingModule _owner;
        private readonly object _registration;
        private readonly IPluginContext _context;
        internal View(GamePostProcessingModule owner, object registration, IPluginContext context)
            => (_owner, _registration, _context) = (owner, registration, context);

        public Result<GameBlurDownsampleTarget> GetDownsampleTarget()
        {
            if (_owner.AccessError(_registration, _context) is { } access) return Result<GameBlurDownsampleTarget>.Fail(access);
            try
            {
                if (_owner._binding is not { } binding || !binding.Channel.Valid || !binding.Source.IsCurrent())
                    return Result<GameBlurDownsampleTarget>.Fail(_owner._unavailable ?? Error(ErrorCode.TargetMissing, "The current blur target has not been observed.", true));
                return Result<GameBlurDownsampleTarget>.Ok(new(binding.Handle, binding.Source.Identity.Scene, Minimum, Maximum));
            }
            catch (Exception error)
            {
                return Result<GameBlurDownsampleTarget>.Fail(Error(ErrorCode.ObservationUnavailable, error.Message, true));
            }
        }

        public Result<IMutationLease<int>> OverrideDownsample(TargetHandle<int> target, int value, int priority = 0)
        {
            var actual = GetDownsampleTarget();
            if (!actual.Success) return Result<IMutationLease<int>>.Fail(actual.Error!);
            if (target is null)
                return Result<IMutationLease<int>>.Fail(Error(ErrorCode.InvalidArgument, "A current blur target handle is required."));
            if (target != actual.Value!.Target)
                return Result<IMutationLease<int>>.Fail(Error(ErrorCode.TargetExpired, "The selected blur target generation is no longer current."));
            var result = _owner._shared.Mutations.ForPlugin(_context).Override(new MutationKey<int>(target, GamePostProcessingRoles.DownsampleProperty), value, priority);
            return result.Success ? Result<IMutationLease<int>>.Ok(result.Value!)
                : Result<IMutationLease<int>>.Fail(result.Error! with { CapabilityId = Capability });
        }

        public Result<IGameBlurBindingIntent> BindDownsample(TargetQuery query, int value, int priority = 0)
            => _owner.BindDownsample(_registration, _context, query, value, priority);
    }
}
