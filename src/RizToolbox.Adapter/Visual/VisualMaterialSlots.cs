namespace RizToolbox.Adapter.Visual;

/// <summary>Semantic material ownership slots. A TMP receiver never falls back to Graphic.material.</summary>
internal sealed record VisualMaterialSlot(string Id,string Role,string ResourceId,int DiscoveryPriority,string ReadMethod,string WriteMethod);
internal static class VisualMaterialSlots
{
    internal static readonly VisualMaterialSlot Graphic=new("graphic","material","unity.ui.material",0,"get_material","set_material");
    internal static readonly VisualMaterialSlot Translucent=new("graphic","material","unity.ui.translucent-image.material",100,"get_material","set_material");
    internal static readonly VisualMaterialSlot TmpFontShared=new("tmp-font-shared","material","unity.tmp.font-shared-material",100,"get_fontSharedMaterial","set_fontSharedMaterial");
    internal static VisualMaterialSlot? Select(bool isTmpReceiver,bool tmpAccessorsVerified,bool isTranslucentReceiver)
        =>isTmpReceiver?(tmpAccessorsVerified?TmpFontShared:null):isTranslucentReceiver?Translucent:Graphic;
    /// <summary>Ranks copied candidates before the service's bounded target cap. Stable scan order breaks ties.</summary>
    internal static IEnumerable<T> Prioritize<T>(IEnumerable<(T Candidate,VisualMaterialSlot Slot,int ScanOrder)> candidates)
        =>candidates.OrderByDescending(x=>x.Slot.DiscoveryPriority).ThenBy(x=>x.ScanOrder).Select(x=>x.Candidate);
}
