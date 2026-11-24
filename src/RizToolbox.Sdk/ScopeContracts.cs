namespace RizToolbox.Sdk;

/// <summary>Automatic boundaries for a child scope. All expire with the plugin activation.</summary>
public enum ScopeLifetime
{
    Manual,
    Scene,
    /// <summary>Expires at the next run start, finish or scene change. Create after a start to bind that run.</summary>
    Run
}

/// <summary>Optional SDK 1.2 context extension; old IPluginContext implementations remain compatible.</summary>
public interface IScopedPluginContext : IPluginContext
{
    /// <summary>Cancelled when this activation or child scope expires. May be observed on a worker thread.</summary>
    CancellationToken Lifetime { get; }
    /// <summary>Creates a main-thread-owned child, automatically released when its parent expires.</summary>
    IPluginScope CreateScope(string name, ScopeLifetime lifetime = ScopeLifetime.Manual);
}

/// <summary>A separate service, event and resource ownership boundary within one plugin activation.</summary>
public interface IPluginScope : IScopedPluginContext, IDisposable
{
    string ScopeId { get; }
    bool IsActive { get; }
}
