# RizToolbox SDK 1.2

Compile plugins for the Rizline compatibility host using this reference-only package. The installed host supplies the implementation at runtime; do not bundle any `RizToolbox.*` host DLL in your mod folder.

```xml
<PackageReference Include="RizToolbox.Sdk" Version="1.2.0"
                  IncludeAssets="compile" PrivateAssets="all" />
```

Target `net6.0` and implement `IRizPlugin`. Package the plugin DLL, its private dependencies and a matching `plugin.json` in one directory under `BepInEx/RizToolbox/mods/`. Keep the entry type's public parameterless constructor free of game operations; use `Enable(IPluginContext)` for host services and callbacks.

SDK 1.x has assembly identity `RizToolbox.Sdk, Version=1.0.0.0`. Contract version 1.2 is checked by the host against the plugin's inclusive `minSdk` and exclusive `maxSdkExclusive`; individual capabilities and operations must also be queried. SDK availability does not mean every game build supplies every capability. Experimental native operations require their own runtime acceptance evidence.

Use `context.GetService<T>(capabilityId)` on the main thread. Retain ordinary immutable snapshots for background work. Resources and callbacks returned by scoped services expire with the activation or child scope; never keep them across reactivation.

Operation failures use `OperationError(Code, Message, CapabilityId, TargetId, Retryable)`. Both `OperationResult` variants and service lookup results expose this object as `Error`. Inspect `Code` or `KnownCode`; use `Message` for display. Create failures with an explicit code and message, or forward the existing error object without dropping its metadata. Results use `Ok`/`Fail` factories; there is no text-only failure overload or historical result constructor. Recompile plugins with the matching current SDK after API changes.

Author template and complete API/support documentation are included in the host source distribution. This package contains only a compiler reference assembly and XML documentation, with no game binary, runtime loader or native library.
