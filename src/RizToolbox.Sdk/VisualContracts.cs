namespace RizToolbox.Sdk;

public static class VisualCapabilityIds
{
    public const string Sprites = "riz.visual.sprites";
    public const string Fonts = "riz.visual.fonts";
    public const string Materials = "riz.visual.materials";
}
public enum VisualTargetKind { Sprite, Font, Material }
public enum VisualAssetKind { Texture2D, Sprite, Font, Material }
public enum VisualFileArea { Package, PluginData }
public sealed record VisualFile(VisualFileArea Area, string RelativePath);
/// <summary>Opaque, process-local identity. A released asset cannot be reused by another plugin.</summary>
public sealed record VisualAsset(string Id, VisualAssetKind Kind);
public sealed record VisualTarget(TargetHandle<VisualAsset> Target, VisualTargetKind Kind,
    TargetMetadata Metadata, IReadOnlyList<string> Aliases);
public interface IVisualAssetLease : IDisposable
{
    VisualAsset Asset { get; }
    int Width { get; }
    int Height { get; }
    long EstimatedBytes { get; }
    bool IsValid { get; }
}
public sealed record MaterialColorProperty(string Name, ResourceColor Value);
public sealed record MaterialFloatProperty(string Name, float Value);
/// <summary>An empty parameter list requests an unchanged clone. Parameter support is adapter/build specific.</summary>
public sealed record VisualMaterialRequest(TargetHandle<VisualAsset> Source,
    IReadOnlyList<MaterialColorProperty>? Colors = null, IReadOnlyList<MaterialFloatProperty>? Floats = null);
/// <summary>Main-thread service for live game UI. Every returned lease is owned by the plugin scope.</summary>
public interface IVisualResourceService
{
    IReadOnlyList<VisualTarget> GetTargets();
    TargetMatch<VisualAsset> Resolve(TargetQuery query);
    /// <summary>RGB/RGBA8, noninterlaced PNG. Texture2D leases can be used to create sprites.</summary>
    OperationResult<IVisualAssetLease> LoadPng(VisualFile file);
    OperationResult<IVisualAssetLease> CreateSprite(VisualAsset texture, float pixelsPerUnit = 100);
    /// <summary>Only adapter-declared built-in font names are accepted.</summary>
    OperationResult<IVisualAssetLease> LoadBuiltinFont(string name);
    /// <summary>Clones the source material. Parameter writes require an adapter-verified shader schema; unsupported requests leave the source unchanged.</summary>
    OperationResult<IVisualAssetLease> CreateMaterial(VisualMaterialRequest request);
    OperationResult<IMutationLease<VisualAsset>> Override(TargetHandle<VisualAsset> target, VisualAsset asset, int priority = 0);
    /// <summary>Maintains a semantic priority layer across object generations at the existing discovery boundary.
    /// Requires a nonempty active-instance query and an unwrapped live asset lease from this service and exact scope.
    /// The first unique match locks scene/content/role/resource values, including null; it never broadens that identity.</summary>
    OperationResult<IVisualBindingIntent> BindOverride(TargetQuery query, IVisualAssetLease asset, int priority = 0);
}
