using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Visual;

/// <summary>Only approved package/data roots are accepted; opened handles are checked against junction races.</summary>
internal sealed class VisualFileAccess
{
    internal const int MaximumFileBytes=16*1024*1024;
    private readonly Func<string,string?> _packages,_data;
    internal VisualFileAccess(Func<string,string?> packages,Func<string,string?> data){_packages=packages;_data=data;}
    internal byte[] Read(string owner,VisualFile file)
    {
        if(file is null||!Enum.IsDefined(typeof(VisualFileArea),file.Area)||string.IsNullOrWhiteSpace(file.RelativePath)||file.RelativePath.Length>512||
            Path.IsPathRooted(file.RelativePath)||file.RelativePath.Any(c=>char.IsControl(c)||c==':')||
            file.RelativePath.Split(new[]{'/','\\'}).Any(x=>x is "" or "." or ".."))throw new InvalidDataException("InvalidAssetPath: an ordinary relative file path is required.");
        if(!string.Equals(Path.GetExtension(file.RelativePath),".png",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("UnsupportedFormat: only .png resource files are accepted.");
        var configured=(file.Area==VisualFileArea.Package?_packages:_data)(owner);
        if(string.IsNullOrWhiteSpace(configured))throw new InvalidDataException("AssetRootUnavailable: the host has not approved this plugin directory.");
        var root=Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        var path=Path.GetFullPath(Path.Combine(root,file.RelativePath));
        if(!Inside(root,path))throw new InvalidDataException("InvalidAssetPath: file is outside the approved plugin root.");
        for(var current=path;current.Length>=root.Length;current=Path.GetDirectoryName(current)??"")
        {
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("InvalidAssetPath: resource paths may not traverse symbolic links or junctions.");
            if(string.Equals(current,root,StringComparison.OrdinalIgnoreCase))break;
        }
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(OperatingSystem.IsWindows())
        {
            var final=new StringBuilder(32768);var length=GetFinalPathNameByHandle(stream.SafeFileHandle,final,final.Capacity,0);
            if(length==0||length>=final.Capacity)throw new IOException("AssetPathUnverified: could not verify the opened resource handle.");
            var actual=final.ToString();if(actual.StartsWith("\\\\?\\UNC\\",StringComparison.Ordinal))actual="\\\\"+actual[8..];else if(actual.StartsWith("\\\\?\\",StringComparison.Ordinal))actual=actual[4..];
            if(!Inside(root,Path.GetFullPath(actual)))throw new InvalidDataException("InvalidAssetPath: opened file resolves outside the approved plugin root.");
        }
        if(stream.Length<33)throw new InvalidDataException("PNG must contain its complete header.");
        if(stream.Length>MaximumFileBytes)throw new VisualOperationException(ErrorCode.QuotaExceeded,"PNG exceeds the 16 MiB encoded resource limit.");
        var bytes=new byte[(int)stream.Length];var position=0;
        while(position<bytes.Length){var n=stream.Read(bytes,position,bytes.Length-position);if(n==0)throw new IOException("Resource file changed during reading.");position+=n;}
        if(stream.ReadByte()!=-1)throw new IOException("Resource file grew during reading.");return bytes;
    }
    private static bool Inside(string root,string path)=>path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,int length,uint flags);
}

/// <summary>Can be shared by independently recoverable visual modules.</summary>
internal sealed class VisualResourceBudget
{
    internal static VisualResourceBudget ProcessShared{get;}=new();
    internal const long MaximumPluginBytes=32L*1024*1024,MaximumTotalBytes=96L*1024*1024;
    internal const int MaximumPluginAssets=32,MaximumTotalAssets=128;
    private readonly Dictionary<string,(long Bytes,int Count)> _owners=new(StringComparer.Ordinal);
    private long _bytes;private int _count;
    internal int AssetCount=>_count;
    internal long EstimatedBytes=>_bytes;
    internal IDisposable Reserve(string owner,long bytes)
    {
        var usage=_owners.TryGetValue(owner,out var current)?current:default;
        if(bytes<0||bytes>MaximumPluginBytes||usage.Bytes+bytes>MaximumPluginBytes||usage.Count>=MaximumPluginAssets||_bytes+bytes>MaximumTotalBytes||_count>=MaximumTotalAssets)
            throw new VisualOperationException(ErrorCode.QuotaExceeded,"Visual allocation would exceed per-plugin or process limits.");
        _owners[owner]=(usage.Bytes+bytes,usage.Count+1);_bytes+=bytes;_count++;
        return new Release(()=>{var old=_owners[owner];_owners[owner]=(old.Bytes-bytes,old.Count-1);_bytes-=bytes;_count--;});
    }
    private sealed class Release:IDisposable{private Action? _action;internal Release(Action action)=>_action=action;public void Dispose(){var a=_action;_action=null;a?.Invoke();}}
}
