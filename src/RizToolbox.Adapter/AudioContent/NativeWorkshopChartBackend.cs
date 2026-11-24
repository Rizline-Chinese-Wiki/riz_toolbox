using RizToolbox.Runtime;
using A=RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal sealed class NativeWorkshopChartBackend
{
    private readonly NativeRuntime _api;private readonly IntPtr _levels,_workshop,_resources,_normal,_object,_scene;
    internal NativeWorkshopChartBackend(NativeRuntime api)
    {
        _api=api;_levels=api.Class("Assembly-CSharp.dll","","WorkshopLevels");_workshop=api.Class("Assembly-CSharp.dll","","WorkshopLevel");
        _normal=api.Class("Assembly-CSharp.dll","","NormalModeInLevelController");_resources=api.Class("UnityEngine.CoreModule.dll","UnityEngine","Resources");
        _object=api.Class("UnityEngine.CoreModule.dll","UnityEngine","Object");_scene=api.Class("UnityEngine.CoreModule.dll","UnityEngine.SceneManagement","Scene");
    }
    internal NativeRuntime.RootHandle Resolve(ulong id)
    {
        var dictionary=_api.CallStatic(_levels,"get_LevelsById");if(dictionary==IntPtr.Zero)throw new KeyNotFoundException("Workshop levels are not ready.");using var root=_api.Root(dictionary);
        if(!_api.Unbox<bool>(_api.CallTyped(root.Target,"ContainsKey",new[]{"System.UInt64"},A.Value(id))))throw new KeyNotFoundException("The game has no existing workshop ID "+id+".");
        var level=_api.CallTyped(root.Target,"get_Item",new[]{"System.UInt64"},A.Value(id));
        if(!_api.ExactHookReceiverMatches(level,_workshop))throw new UnsupportedSignatureException("WorkshopLevels dictionary did not return WorkshopLevel.");return _api.Root(level);
    }
    internal bool IsCurrent(IParsedLocalChart chart)
    {
        if(chart is not ParsedNativeChart parsed)return false;var count=0;
        var all=_api.CallStatic(_resources,"FindObjectsOfTypeAll",A.Ref(_api.Type(_normal)));
        foreach(var controller in _api.ArrayItems(all).Take(1024))
        {
            if(controller==IntPtr.Zero||!_api.Unbox<bool>(_api.CallStatic(_object,"op_Implicit",A.Ref(controller)))||!_api.Unbox<bool>(_api.Call(controller,"get_isActiveAndEnabled")))continue;
            var go=_api.Call(controller,"get_gameObject");var scene=_api.Unbox<int>(_api.Call(go,"get_scene"));
            if(_api.Text(_api.CallStatic(_scene,"GetNameInternal",A.Value(scene)))!="Level")continue;
            var info=_api.Read<IntPtr>(controller,"LevelInformation");if(info==IntPtr.Zero||!_api.Read<bool>(info,"levelBegan"))continue;
            var parameters=_api.Read<IntPtr>(controller,"LevelParams");if(parameters!=IntPtr.Zero&&_api.Read<IntPtr>(parameters,"chart")==parsed.Pointer)count++;
        }
        return count==1;
    }
}
