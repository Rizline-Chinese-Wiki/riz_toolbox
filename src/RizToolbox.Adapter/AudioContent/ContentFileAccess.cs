using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed class ContentFileAccess
{
    internal const int MaximumFileBytes = 16 * 1024 * 1024;
    private readonly Func<string,string?> _packages, _data;
    internal ContentFileAccess(Func<string,string?> packages, Func<string,string?> data) { _packages=packages; _data=data; }
    internal byte[] Read(string owner, ContentFile file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(file is null || !Enum.IsDefined(typeof(ContentFileArea),file.Area) || string.IsNullOrWhiteSpace(file.RelativePath) || file.RelativePath.Length>512 ||
           Path.IsPathRooted(file.RelativePath) || file.RelativePath.Any(c=>char.IsControl(c)||c==':') || file.RelativePath.Split(new[]{'/','\\'}).Any(x=>x is "" or "." or ".."))
            throw new InvalidDataException("An ordinary relative content path is required.");
        var maximumBytes=file.MaximumBytes;
        if(maximumBytes is <1 or >MaximumFileBytes)
            throw new InvalidDataException("Content byte limit must be between 1 byte and 16 MiB.");
        var approved=(file.Area==ContentFileArea.Package?_packages:_data)(owner);
        if(string.IsNullOrWhiteSpace(approved)) throw new InvalidDataException("The host has not approved this plugin directory.");
        var root=Path.GetFullPath(approved).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        var path=Path.GetFullPath(Path.Combine(root,file.RelativePath));
        if(!Inside(root,path)) throw new InvalidDataException("Content is outside the approved plugin root.");
        for(var p=path;p.Length>=root.Length;p=Path.GetDirectoryName(p)??"")
        {
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Content may not traverse links or junctions.");
            if(string.Equals(p,root,StringComparison.OrdinalIgnoreCase)) break;
        }
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(OperatingSystem.IsWindows())
        {
            var final=new StringBuilder(32768); var count=GetFinalPathNameByHandle(input.SafeFileHandle,final,final.Capacity,0);
            if(count==0 || count>=final.Capacity) throw new IOException("The opened content path could not be verified.");
            var actual=final.ToString(); if(actual.StartsWith("\\\\?\\UNC\\",StringComparison.Ordinal))actual="\\\\"+actual[8..];else if(actual.StartsWith("\\\\?\\",StringComparison.Ordinal))actual=actual[4..];
            if(!Inside(root,Path.GetFullPath(actual)))throw new InvalidDataException("The opened file is outside the approved plugin root.");
        }
        var length=input.Length;
        if(length<1 || length>maximumBytes)throw new InvalidDataException($"Content file must contain 1..{maximumBytes} bytes.");
        var bytes=new byte[(int)length]; var at=0;
        while(at<bytes.Length){cancellationToken.ThrowIfCancellationRequested();var n=input.Read(bytes,at,Math.Min(65536,bytes.Length-at));if(n==0)throw new IOException("Content changed during reading.");at+=n;}
        if(input.ReadByte()!=-1)throw new IOException("Content grew during reading.");cancellationToken.ThrowIfCancellationRequested();return bytes;
    }
    private static bool Inside(string root,string path)=>path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,int length,uint flags);
}

internal sealed class AudioBudget
{
    internal const long MaximumOwnerBytes=48L*1024*1024, MaximumTotalBytes=128L*1024*1024;
    internal const int MaximumOwnerAssets=16, MaximumTotalAssets=64;
    private readonly Dictionary<string,(long Bytes,int Count)> _owners=new(StringComparer.Ordinal);
    private long _bytes; private int _count;
    internal IDisposable Reserve(string owner,long bytes)
    {
        var old=_owners.GetValueOrDefault(owner);
        if(bytes<0 || bytes>MaximumOwnerBytes || old.Bytes+bytes>MaximumOwnerBytes || old.Count>=MaximumOwnerAssets || _bytes+bytes>MaximumTotalBytes || _count>=MaximumTotalAssets)
            throw new AudioQuotaException("Audio allocation exceeds the owner or process budget.");
        _owners[owner]=(old.Bytes+bytes,old.Count+1);_bytes+=bytes;_count++;
        return new ActionLease(()=>{var now=_owners[owner];_owners[owner]=(now.Bytes-bytes,now.Count-1);_bytes-=bytes;_count--;});
    }
}
internal sealed class AudioQuotaException:Exception { internal AudioQuotaException(string message):base(message){} }
internal sealed class ActionLease:IDisposable { private Action? _action;internal ActionLease(Action action)=>_action=action;public void Dispose(){var action=_action;_action=null;action?.Invoke();} }
