using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using RizToolbox.Sdk;

namespace RizToolbox.Core;

internal static class ScopedServiceViews
{
    public static T Create<T>(PluginScope scope, T inner, string capabilityId, Action verify, Action? dispose = null) where T : class
    {
        object view = inner switch
        {
            IUiService ui when typeof(T) == typeof(IUiService) => new Ui(scope, ui, verify),
            IResourceService resources when typeof(T) == typeof(IResourceService) => new Resources(scope, resources, verify),
            IGameResourceService resources when typeof(T) == typeof(IGameResourceService) => new GameResources(scope, resources, verify),
            IExtensionService extensions when typeof(T) == typeof(IExtensionService) => new Extensions(scope, extensions, verify),
            _ => CreateProxy(scope, inner, capabilityId, verify, dispose)
        };
        return (T)view;
    }

    private static T CreateProxy<T>(PluginScope scope, T inner, string capabilityId, Action verify, Action? dispose) where T : class
    {
        var view = DispatchProxy.Create<T, ScopedServiceProxy>();
        var proxy = (ScopedServiceProxy)(object)view;
        proxy.Inner = inner;
        proxy.Verify = verify;
        proxy.Own = value => scope.Own(value);
        proxy.DisposeOwned = dispose;
        proxy.CapabilityId = capabilityId;
        return view;
    }

    private static OperationError Error(Exception error, PluginScope scope, string capability, string? target = null)
        => new(error is ObjectDisposedException ? ErrorCode.ScopeExpired : !scope.MainThread.IsMainThread ? ErrorCode.WrongThread
            : error is ArgumentException ? ErrorCode.InvalidArgument : error is OperationCanceledException ? ErrorCode.Cancelled : ErrorCode.InternalError,
            error.Message, capability, target);
    private static OperationResult<T> Failed<T>(Exception error, PluginScope scope, string capability, string? target = null)
        => OperationResult<T>.Fail(Error(error, scope, capability, target));

    private sealed class Ui : IUiService
    {
        private readonly PluginScope _scope; private readonly IUiService _inner; private readonly Action _verify;
        public Ui(PluginScope scope, IUiService inner, Action verify) { _scope = scope; _inner = inner; _verify = verify; }
        public OperationResult<IPanelHandle> CreatePanel(PanelDefinition definition)
        {
            try
            {
                _verify();
                if (definition is null || string.IsNullOrEmpty(definition.Id) || !Regex.IsMatch(definition.Id, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$"))
                    return OperationResult<IPanelHandle>.Fail(ErrorCode.InvalidArgument, "Panel local ID must be 1-128 ASCII letters, digits, dots, hyphens or underscores.", CapabilityIds.Ui, definition?.Id);
                var result = _inner.CreatePanel(definition with { Id = _scope.PluginId + "/" + definition.Id });
                if (!result.Success || result.Value is null) return result;
                return OperationResult<IPanelHandle>.Ok(_scope.Own<IPanelHandle>(new Panel(_scope, result.Value, _verify, _scope.VerifyThread)));
            }
            catch (Exception ex) { return Failed<IPanelHandle>(ex, _scope, CapabilityIds.Ui, definition?.Id); }
        }
    }

    private sealed class Panel : IPanelHandle
    {
        private IPanelHandle? _inner; private readonly Action _verify; private readonly Action _verifyThread; private readonly PluginScope _scope;
        public Panel(PluginScope scope, IPanelHandle inner, Action verify, Action verifyThread) { _scope = scope; _inner = inner; _verify = verify; _verifyThread = verifyThread; Id = inner.Id; }
        public string Id { get; }
        public OperationResult SetText(string text)
        {
            try { _verify(); return _inner?.SetText(text) ?? OperationResult.Fail(ErrorCode.TargetExpired, "Panel is disposed.", CapabilityIds.Ui, Id); }
            catch (Exception ex) { return OperationResult.Fail(Error(ex, _scope, CapabilityIds.Ui, Id)); }
        }
        public void Dispose() { if (_inner is null) return; _verifyThread(); var inner = _inner; _inner = null; inner.Dispose(); }
    }

    private sealed class Override : IResourceOverride
    {
        private IResourceOverride? _inner; private readonly Action _verifyThread;
        public Override(IResourceOverride inner, Action verifyThread) { _inner = inner; _verifyThread = verifyThread; ResourceKey = inner.ResourceKey; }
        public string ResourceKey { get; }
        public void Dispose() { if (_inner is null) return; _verifyThread(); var inner = _inner; _inner = null; inner.Dispose(); }
    }

    private sealed class Resources : IResourceService
    {
        private readonly PluginScope _scope; private readonly IResourceService _inner; private readonly Action _verify;
        public Resources(PluginScope scope, IResourceService inner, Action verify) { _scope = scope; _inner = inner; _verify = verify; }
        public OperationResult<IResourceOverride> OverrideTexture(TextureOverrideRequest request)
        {
            try
            {
                _verify();
                var result = _inner.OverrideTexture(request);
                return result.Success && result.Value is not null
                    ? OperationResult<IResourceOverride>.Ok(_scope.Own<IResourceOverride>(new Override(result.Value, _scope.VerifyThread))) : result;
            }
            catch (Exception ex) { return Failed<IResourceOverride>(ex, _scope, CapabilityIds.Resources, request?.ResourceKey); }
        }
    }

    private sealed class GameResources : IGameResourceService
    {
        private readonly PluginScope _scope; private readonly IGameResourceService _inner; private readonly Action _verify;
        public GameResources(PluginScope scope, IGameResourceService inner, Action verify) { _scope = scope; _inner = inner; _verify = verify; }
        public OperationResult<IReadOnlyList<GameResourceTarget>> GetTargets()
        {
            try { _verify(); return _inner.GetTargets(); }
            catch (Exception ex) { return Failed<IReadOnlyList<GameResourceTarget>>(ex, _scope, GameCapabilityIds.Resources); }
        }
        public OperationResult<IResourceOverride> OverrideColor(GameColorOverrideRequest request)
        {
            try
            {
                _verify();
                var result = _inner.OverrideColor(request);
                return result.Success && result.Value is not null
                    ? OperationResult<IResourceOverride>.Ok(_scope.Own<IResourceOverride>(new Override(result.Value, _scope.VerifyThread))) : result;
            }
            catch (Exception ex) { return Failed<IResourceOverride>(ex, _scope, GameCapabilityIds.Resources, request?.ResourceKey); }
        }
    }

    private sealed class Extensions : IExtensionService
    {
        private readonly PluginScope _scope; private readonly IExtensionService _inner; private readonly Action _verify;
        public Extensions(PluginScope scope, IExtensionService inner, Action verify) { _scope = scope; _inner = inner; _verify = verify; }
        public IReadOnlyList<ExtensionPointDescriptor> GetAll() { _verify(); return _inner.GetAll(); }
        public OperationResult<ExtensionPointDescriptor> Describe(string id)
        {
            try { _verify(); return _inner.Describe(id); }
            catch (Exception ex) { return Failed<ExtensionPointDescriptor>(ex, _scope, CapabilityIds.Extensions, id); }
        }
        public OperationResult<IDisposable> Subscribe<T>(ExtensionPoint<T> point, Action<T> handler, int priority = 0)
        {
            try
            {
                _verify();
                if (handler is null) return OperationResult<IDisposable>.Fail(ErrorCode.InvalidArgument, "Extension subscriber is null.", CapabilityIds.Extensions, point?.Id);
                void Invoke(T message)
                {
                    if (!_scope.Active) return;
                    try { _verify(); }
                    catch (ObjectDisposedException) { return; }
                    try { _scope.InvokeCallback("extension:" + point.Id, () => handler(message)); }
                    catch (Exception ex) { _scope.ReportFault(ex); throw; }
                }
                var result = _inner.Subscribe(point, Invoke, priority);
                return result.Success && result.Value is not null
                    ? OperationResult<IDisposable>.Ok(_scope.Own<IDisposable>(new IdempotentSubscription(result.Value, _scope.VerifyThread))) : result;
            }
            catch (Exception ex) { return Failed<IDisposable>(ex, _scope, CapabilityIds.Extensions, point?.Id); }
        }
    }

    private sealed class IdempotentSubscription : IDisposable
    {
        private IDisposable? _inner; private readonly Action _verifyThread;
        public IdempotentSubscription(IDisposable inner, Action verifyThread) { _inner = inner; _verifyThread = verifyThread; }
        public void Dispose() { if (_inner is null) return; _verifyThread(); var inner = _inner; _inner = null; inner.Dispose(); }
    }
}

/// <summary>Generated interface proxy; public visibility is required by DispatchProxy's generated assembly.</summary>
public class ScopedServiceProxy : DispatchProxy
{
    internal object Inner = null!;
    internal Action Verify = null!;
    internal Func<IDisposable, IDisposable> Own = null!;
    internal Action? DisposeOwned;
    internal string CapabilityId = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (DisposeOwned is not null && targetMethod?.Name == nameof(IDisposable.Dispose) && targetMethod.ReturnType == typeof(void)
            && targetMethod.GetParameters().Length == 0 && typeof(IDisposable).IsAssignableFrom(Inner.GetType()))
        { DisposeOwned(); return null; }
        try { Verify(); }
        catch (Exception ex) when ((ex is ObjectDisposedException or InvalidOperationException) && IsStructuredResult(targetMethod?.ReturnType))
        {
            var error = new OperationError(ex is ObjectDisposedException ? ErrorCode.ScopeExpired : ErrorCode.WrongThread, ex.Message, CapabilityId, TargetId(args));
            if (targetMethod!.ReturnType == typeof(Result)) return Result.Fail(error);
            if (targetMethod.ReturnType == typeof(OperationResult)) return OperationResult.Fail(error);
            if (targetMethod.ReturnType.IsGenericType && targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(ServiceResolution<>))
                return Activator.CreateInstance(targetMethod.ReturnType, new object?[] { false, null, null, error });
            return targetMethod.ReturnType.GetMethod(nameof(Result.Fail), BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(OperationError) }, null)!.Invoke(null, new object[] { error });
        }
        try
        {
            var result = targetMethod!.Invoke(Inner, args);
            if (result is IDisposable owned) Own(owned);
            return result;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static bool IsStructuredResult(Type? type) => type == typeof(Result) || type == typeof(OperationResult)
        || type is { IsGenericType: true } && (type.GetGenericTypeDefinition() == typeof(Result<>) || type.GetGenericTypeDefinition() == typeof(OperationResult<>)
            || type.GetGenericTypeDefinition() == typeof(ServiceResolution<>));
    private static string? TargetId(object?[]? arguments)
    {
        foreach (var argument in arguments ?? Array.Empty<object?>())
        {
            if (argument is TargetQuery query) return query.Key;
            if (argument is ServiceQuery service) return service.Id;
            var type = argument?.GetType();
            // Read only SDK-owned immutable handle DTOs; arbitrary provider
            // property getters must not run while a thread/scope is rejected.
            if (type?.IsGenericType != true || type.Assembly != typeof(TargetHandle<>).Assembly) continue;
            if (type.GetGenericTypeDefinition() == typeof(TargetHandle<>)) return (string?)type.GetProperty("Key")!.GetValue(argument);
            if (type.GetGenericTypeDefinition() == typeof(MutationKey<>))
            {
                var target = type.GetProperty("Target")!.GetValue(argument);
                return target is null ? null : (string?)target.GetType().GetProperty("Key")!.GetValue(target);
            }
        }
        return null;
    }
}
