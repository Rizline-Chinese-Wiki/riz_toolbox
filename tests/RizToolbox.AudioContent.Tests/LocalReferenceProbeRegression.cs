using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using static Assert;

internal static class LocalReferenceProbeRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Existing resource owner probe is opt-in once-only reentry-safe and does not consume its caller root", Once),
        ("Existing owner probe ignores wrong-thread calls without consuming its main-thread opportunity", Thread),
        ("Native metadata capture and diagnostic sink failures never escape or retry borrowed owner access", Failures),
        ("Owner evidence preflights all rows before logging and refuses oversized or excessive payloads", Bounds)
    };
    private static LocalResourceReferenceDiagnosticReport Report(params LocalResourceReferenceEvidence[] rows)
        => new("existing-owner:InsideLevelParams", "test-build", DateTimeOffset.UnixEpoch, Array.AsReadOnly(rows));
    private static LocalResourceReferenceEvidence Row(string value="observed") => new("existing.music.dispatch", "unknown",
        new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>{{"Reason",value}}));
    private static void Once()
    {
        using var main=new MainThreadDispatcher(); var log=new ProbeLog(); var frees=0; var captures=0;
        using var root=new NativeRuntime.RootHandle(1,_=>frees++, _=>new IntPtr(42));
        var off=new LocalResourceReferenceOwnerProbe(main,log,false,_=>{captures++;return Report(Row());}); off.TryCapture(root); Equal(0,captures);
        LocalResourceReferenceOwnerProbe? probe=null;
        probe=new(main,log,true,owner=>{captures++;Equal(new IntPtr(42),owner.Target);probe!.TryCapture(owner);return Report(Row());});
        probe.TryCapture(root); probe.TryCapture(root); Equal(1,captures); Equal(2,log.Messages.Count); Equal(0,frees); Equal(new IntPtr(42),root.Target);
        root.Dispose(); Equal(1,frees);
    }
    private static void Thread()
    {
        using var main=new MainThreadDispatcher(); var log=new ProbeLog(); var captures=0; Exception? escaped=null;
        using var root=new NativeRuntime.RootHandle(1,_=>{},_=>new IntPtr(43));
        var probe=new LocalResourceReferenceOwnerProbe(main,log,true,_=>{captures++;return Report(Row());});
        var worker=new System.Threading.Thread(()=>{try{probe.TryCapture(root);}catch(Exception e){escaped=e;}}); worker.Start(); worker.Join();
        Check(escaped is null); Equal(0,captures); Equal(0,log.Messages.Count); probe.TryCapture(root); Equal(1,captures);
    }
    private static void Failures()
    {
        using var main=new MainThreadDispatcher(); var log=new ProbeLog(); var captures=0; var frees=0;
        using var root=new NativeRuntime.RootHandle(1,_=>frees++, _=>new IntPtr(44));
        var probe=new LocalResourceReferenceOwnerProbe(main,log,true,_=>{captures++;throw new InvalidDataException("metadata unavailable");});
        probe.TryCapture(root); probe.TryCapture(root); Equal(1,captures); Equal(0,frees); Equal(1,log.Messages.Count(x=>x.Level==LogLevel.Warning));
        var brokenLog=new ProbeLog{Throw=true}; captures=0;
        var broken=new LocalResourceReferenceOwnerProbe(main,brokenLog,true,_=>{captures++;return Report(Row());});
        broken.TryCapture(root); broken.TryCapture(root); Equal(1,captures); Equal(0,frees); Equal(new IntPtr(44),root.Target);
    }
    private static void Bounds()
    {
        using var main=new MainThreadDispatcher(); using var root=new NativeRuntime.RootHandle(1,_=>{},_=>new IntPtr(45));
        foreach(var report in new[]{Report(Row(),Row(new string('x',1900))),Report(Enumerable.Repeat(Row(),65).ToArray())})
        {
            var log=new ProbeLog(); var probe=new LocalResourceReferenceOwnerProbe(main,log,true,_=>report); probe.TryCapture(root);
            Equal(0,log.Messages.Count(x=>x.Level==LogLevel.Info)); Equal(1,log.Messages.Count(x=>x.Level==LogLevel.Warning));
        }
    }
    private sealed class ProbeLog:ILogger
    {
        internal readonly List<(LogLevel Level,string Message)> Messages=new(); internal bool Throw;
        public void Log(LogLevel level,string message,Exception? error=null){if(Throw)throw new IOException("diagnostic sink");Messages.Add((level,message));}
    }
}
