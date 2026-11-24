using System.Security.Cryptography;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Visual;

internal enum VisualMaterialValueType { Color, Float }
internal enum VisualMaterialReferenceKind { Material, Shader }
internal sealed record VisualMaterialProperty(string Name,VisualMaterialValueType Type,float? Minimum=null,float? Maximum=null);
internal sealed record VisualMaterialProfile(string Id,string ReferenceResource,VisualMaterialReferenceKind ReferenceKind,
    IReadOnlyDictionary<string,string> RequiredFiles,IReadOnlyList<VisualMaterialProperty> Properties);

/// <summary>Extracted from the exact installed Unity serialized shader properties, never from shader names.</summary>
internal static class VisualMaterialProfiles
{
    internal const string GameAssemblyHash="853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private const string ResourceManagerHash="4b43cc1090bd3655287390cfb13d244038f53ced4479cedf92bbcc1f8fbdbeac";
    private const string ResourcesHash="b8c59da01063f9e1bcd7f90fcbc79949b98e67294b99782d397e3610afb6f59e";
    private static IReadOnlyDictionary<string,string> Files(bool textShader)
    {
        var files=new Dictionary<string,string>(StringComparer.Ordinal){
            ["GameAssembly.dll"]=GameAssemblyHash,["Rizline_Data/globalgamemanagers"]=ResourceManagerHash,
            ["Rizline_Data/resources.assets"]=ResourcesHash};
        if(textShader)files["Rizline_Data/sharedassets0.assets"]="d2b477be6a8ff02ef8e846b2a9a9616a72b3174db28b4fbe673647820ef14032";
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(files);
    }
    internal static readonly VisualMaterialProfile TextOutline=new("rizline.pc.tmp-mobile-outline.v1",
        "fonts & materials/liberationsans sdf - outline",VisualMaterialReferenceKind.Material,Files(true),Array.AsReadOnly(new[]{
            new VisualMaterialProperty("_FaceColor",VisualMaterialValueType.Color),
            new VisualMaterialProperty("_OutlineColor",VisualMaterialValueType.Color),
            new VisualMaterialProperty("_OutlineWidth",VisualMaterialValueType.Float,0,1),
            new VisualMaterialProperty("_FaceDilate",VisualMaterialValueType.Float,-1,1)}));
    internal static readonly VisualMaterialProfile Translucent=new("rizline.pc.translucent-ui-float.v1",
        "translucentimage",VisualMaterialReferenceKind.Shader,Files(false),Array.AsReadOnly(new[]{
            new VisualMaterialProperty("_Vibrancy",VisualMaterialValueType.Float),
            new VisualMaterialProperty("_Brightness",VisualMaterialValueType.Float),
            new VisualMaterialProperty("_Flatten",VisualMaterialValueType.Float)}));
    private static readonly VisualMaterialProfile[] Profiles={TextOutline,Translucent};
    internal static IReadOnlyList<VisualMaterialProfile> Candidates(VisualMaterialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var colors=request.Colors??Array.Empty<MaterialColorProperty>();var floats=request.Floats??Array.Empty<MaterialFloatProperty>();
        if(colors.Count==0&&floats.Count==0)return Array.Empty<VisualMaterialProfile>();
        if((long)colors.Count+floats.Count>16)throw new InvalidDataException("InvalidMaterialRequest: at most 16 parameters are accepted.");
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var color in colors)
        {
            if(color is null||string.IsNullOrWhiteSpace(color.Name)||!names.Add(color.Name)||color.Value is null)
                throw new InvalidDataException("InvalidMaterialRequest: unique nonempty property names and values are required.");
            var c=color.Value;
            if(new[]{c.R,c.G,c.B,c.A}.Any(x=>!float.IsFinite(x)||x<0||x>1))
                throw new InvalidDataException("InvalidMaterialRequest: Color uses the conservative finite RGBA [0,1] adapter policy.");
        }
        foreach(var value in floats)
            if(value is null||string.IsNullOrWhiteSpace(value.Name)||!names.Add(value.Name)||!float.IsFinite(value.Value))
                throw new InvalidDataException("InvalidMaterialRequest: unique nonempty property names and finite Float values are required.");
        var candidates=Profiles.Where(profile=>colors.All(c=>profile.Properties.Any(p=>p.Name==c.Name&&p.Type==VisualMaterialValueType.Color))&&
            floats.All(f=>profile.Properties.Any(p=>p.Name==f.Name&&p.Type==VisualMaterialValueType.Float))).ToArray();
        if(candidates.Length==0)throw new NotSupportedException("UnsupportedShaderSchema: no evidenced profile declares every requested property and type.");
        foreach(var profile in candidates)Validate(profile,request);
        return candidates;
    }
    private static void Validate(VisualMaterialProfile profile,VisualMaterialRequest request)
    {
        foreach(var color in request.Colors??Array.Empty<MaterialColorProperty>())
            if(!profile.Properties.Any(p=>p.Name==color.Name&&p.Type==VisualMaterialValueType.Color))throw new NotSupportedException("UnsupportedProperty: Color type is not declared by this profile.");
        foreach(var value in request.Floats??Array.Empty<MaterialFloatProperty>())
        {
            var spec=profile.Properties.SingleOrDefault(p=>p.Name==value.Name&&p.Type==VisualMaterialValueType.Float)
                ??throw new NotSupportedException("UnsupportedProperty: Float type is not declared by this profile.");
            if(!float.IsFinite(value.Value)||spec.Minimum is float min&&value.Value<min||spec.Maximum is float max&&value.Value>max)
                throw new InvalidDataException("InvalidMaterialRequest: "+value.Name+" exceeds its serialized shader Range.");
        }
    }
    /// <summary>Production paths are relative to the actual executable, not a plugin-provided root. Handles stay open through resource resolution.</summary>
    internal static IDisposable VerifyFiles(VisualMaterialProfile profile,string gameRoot)
    {
        var streams=new List<FileStream>();
        try
        {
            foreach(var file in profile.RequiredFiles)
            {
                var path=Path.Combine(gameRoot,file.Key.Replace('/',Path.DirectorySeparatorChar));
                var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);streams.Add(stream);
                using var algorithm=SHA256.Create();
                if(stream.Length<1||stream.Length>256L*1024*1024||!string.Equals(Convert.ToHexString(algorithm.ComputeHash(stream)),file.Value,StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("UnsupportedShaderSchema: supported build or serialized asset hash differs: "+file.Key);
            }
            return new EvidenceFiles(streams);
        }
        catch(Exception error)
        {
            foreach(var stream in streams)stream.Dispose();
            if(error is NotSupportedException)throw;
            throw new NotSupportedException("UnsupportedShaderSchema: required build/asset evidence is unavailable.",error);
        }
    }
    private sealed class EvidenceFiles:IDisposable
    {
        private List<FileStream>? _streams;internal EvidenceFiles(List<FileStream> streams)=>_streams=streams;
        public void Dispose(){var streams=_streams;_streams=null;if(streams is not null)foreach(var stream in streams)stream.Dispose();}
    }
}
