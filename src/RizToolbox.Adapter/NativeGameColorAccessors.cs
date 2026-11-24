using RizToolbox.Runtime;

namespace RizToolbox.Adapter;

/// <summary>A color property is identified by exact declaring metadata, independently of asset/material slots.</summary>
internal sealed record NativeGameColorSlot(string Assembly,string Namespace,string ReceiverType,string DeclaringType,
    string AliasKind,string Role,string ResourceId)
{
    internal static NativeGameColorSlot Image{get;}=new("UnityEngine.UI.dll","UnityEngine.UI","Image","Graphic",
        "image","image-color","unity.image.color");
    internal static NativeGameColorSlot TmpText{get;}=new("Unity.TextMeshPro.dll","TMPro","TMP_Text","TMP_Text",
        "tmp-text","tmp-text-color","unity.tmp-text.color");
}

internal sealed record NativeGameColorAccessors(IntPtr ReceiverType,IntPtr Read,IntPtr Write)
{
    internal static NativeGameColorAccessors Bind(NativeRuntime api,NativeGameColorSlot slot)
    {
        var receiver=api.Class(slot.Assembly,slot.Namespace,slot.ReceiverType);
        var declaring=api.Class(slot.Assembly,slot.Namespace,slot.DeclaringType);
        return Bind(receiver,
            (name,parameters)=>api.Method(declaring,name,parameters.Length,false,parameters),
            (method,name)=>api.IsMethod(method,slot.Namespace,slot.DeclaringType,name),api.DescribeReturnType);
    }

    // The seam checks both the requested parameter lists and actual declaring/return metadata without native invocation.
    internal static NativeGameColorAccessors Bind(IntPtr receiver,Func<string,string[],IntPtr> resolve,
        Func<IntPtr,string,bool> declaredByExpectedType,Func<IntPtr,string> returnType)
    {
        if(receiver==IntPtr.Zero)throw new MissingMemberException("Color receiver class is absent.");
        var read=resolve("get_color",Array.Empty<string>());
        var write=resolve("set_color",new[]{"UnityEngine.Color"});
        if(read==IntPtr.Zero||write==IntPtr.Zero||!declaredByExpectedType(read,"get_color")||!declaredByExpectedType(write,"set_color")||
            returnType(read)!="UnityEngine.Color"||returnType(write)!="System.Void")
            throw new UnsupportedSignatureException("Color accessors differ from the exact declared property; inherited fallback is not permitted.");
        return new(receiver,read,write);
    }
}
