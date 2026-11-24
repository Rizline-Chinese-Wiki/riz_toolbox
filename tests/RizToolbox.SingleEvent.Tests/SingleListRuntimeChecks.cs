using System.Runtime.InteropServices;
using RizToolbox.Runtime;

internal static class SingleListRuntimeChecks
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("actual NativeRuntime executing-list capture roots event list backing and every capacity object before class reads", Roots),
        ("actual NativeRuntime empty list captures full capacity without mistaking absent mid for empty", Empty),
        ("actual NativeRuntime list captures preserve duplicate receiver objects at separate live indices", Duplicate),
        ("actual NativeRuntime list rejects changed count version backing and active null with roots retained", Changes),
        ("actual NativeRuntime list root publication failures retain each earlier actual native handle", PartialRoots),
        ("actual NativeRuntime list metadata header failure cannot become a partial accepted vector", BadArray),
        ("actual NativeRuntime list rejects worker access before native roots fields or metadata", Worker)
    };
    private static void Check(bool value){if(!value)throw new InvalidOperationException("Single list runtime assertion failed.");}
    private static void Throws(Action action){try{action();}catch{return;}throw new InvalidOperationException("Expected rejection.");}
    private static void Roots()
    {
        using var f=new SingleListFixture(); Marshal.WriteInt32(f.Scheduled,0x18,1);
        var list=f.Capture(); Check(list.Event==f.Manager.ToInt64()&&list.List==f.Scheduled.ToInt64()&&list.Array==f.PlayingArray.ToInt64()
            &&list.Count==1&&list.Version==7&&list.Items.Count==4&&f.Retained.ContainsKey(f.Tween)&&f.Retained.ContainsKey(f.OtherTween)
            &&f.UnrootedObjectReads.Count==0&&f.Base.NewRoots>f.Retained.Count&&f.Base.FreedRoots==f.Base.NewRoots-f.Retained.Count);
        f.NoInvocations();
    }
    private static void Empty()
    {
        using var f=new SingleListFixture();Marshal.WriteInt32(f.Scheduled,0x18,0);var list=f.Capture();
        Check(list.Count==0&&list.Items.Count==4&&list.Items[0]==f.Tween.ToInt64()&&f.Retained.Count==5&&f.UnrootedObjectReads.Count==0);f.NoInvocations();
    }
    private static void Duplicate()
    {
        using var f=new SingleListFixture();Marshal.WriteIntPtr(f.PlayingArray,48,f.Tween);Marshal.WriteInt32(f.Scheduled,0x18,3);
        var list=f.Capture();Check(list.Count==3&&list.Items.Take(3).All(value=>value==f.Tween.ToInt64())&&f.Retained.Count==5&&f.UnrootedObjectReads.Count==0);f.NoInvocations();
    }
    private static void Changes()
    {
        for(var mode=0;mode<4;mode++){
            using var f=new SingleListFixture();Marshal.WriteInt32(f.Scheduled,0x18,1);var changed=false;
            if(mode<3)f.BeforePublishedRoot=pointer=>{
                if(changed||pointer!=f.Tween)return;changed=true;
                if(mode==0)Marshal.WriteInt32(f.Scheduled,0x18,0);
                if(mode==1)Marshal.WriteInt32(f.Scheduled,0x1c,8);
                if(mode==2)Marshal.WriteIntPtr(f.Scheduled,0x10,f.PlayingArray+8);
            };
            else Marshal.WriteInt32(f.Scheduled,0x18,2);
            Throws(()=>f.Capture());Check(f.Retained.ContainsKey(f.Manager)&&f.Retained.ContainsKey(f.Scheduled)&&f.Base.ActiveRoots>=3&&f.UnrootedObjectReads.Count==0);f.NoInvocations();
        }
    }
    private static void PartialRoots()
    {
        using var f=new SingleListFixture();Marshal.WriteInt32(f.Scheduled,0x18,1);
        f.BeforePublishedRoot=pointer=>{if(pointer==f.OtherTween)throw new IOException("native element root rejected");};
        Throws(()=>f.Capture());Check(f.Retained.ContainsKey(f.Tween)&&f.Retained.ContainsKey(f.PlayingArray)&&!f.Retained.ContainsKey(f.OtherTween)
            &&f.Base.ActiveRoots>=4&&f.UnrootedObjectReads.Count==0);f.NoInvocations();
    }
    private static void BadArray()
    {
        using var f=new SingleListFixture();Marshal.WriteInt32(f.Scheduled,0x18,1);f.ArrayByteAdjustment=8;
        Throws(()=>f.Capture());Check(f.Retained.Count==3&&f.UnrootedObjectReads.Count==0);f.NoInvocations();
    }
    private static void Worker()
    {
        using var f=new SingleListFixture();var fields=f.FieldReads;var metadata=f.MetadataReads;var roots=f.Base.NewRoots;
        Task.Run(()=>Throws(()=>f.Capture())).GetAwaiter().GetResult();Check(f.FieldReads==fields&&f.MetadataReads==metadata&&f.Base.NewRoots==roots);f.NoInvocations();
    }
}
