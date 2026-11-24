# AuthorTwo

This mod reads the current scene and observes later scene changes through the host's shared SDK. It changes no game state. The host automatically removes its subscription when disabled.

Build with .NET SDK 8 and the SDK reference package from the matching source release:

```powershell
dotnet restore --source 'C:\path\to\riz_toolbox\artifacts\sdk'
dotnet build -c Release --no-restore
```

The restored SDK is a compiler reference only. Copy `bin/Release/net6.0/AuthorTwo.dll` and `plugin.json` to `BepInEx/RizToolbox/mods/author.two/` with the game closed. Do not copy any SDK, Core, Adapter, Runtime or Loader DLL. In an existing installation enable `author.two` through the host command after restarting; new packages do not override saved activation choices.

```json
{ "id": "enable-author.two-1", "action": "enable", "pluginId": "author.two" }
```

Write the command with a new unique `id` to `BepInEx/RizToolbox/command.json`. Check `status.json` for its activation and capability state. A missing scene provider leaves the plugin waiting; a constructor or callback error belongs to this plugin.

After changing plugin identity or version update both `Plugin.Manifest` and `plugin.json`. Keep public constructors free of host/game calls. Obtain new scoped services during every `Enable` rather than carrying old handles to another activation.

The matching SDK uses structured operation failures: `OperationResult.Error` and service lookup `Error` are `OperationError` values with `Code`, `Message`, `CapabilityId`, `TargetId`, and `Retryable`. Branch on `Code` or `KnownCode`; display `Message` without parsing it. Create failures with an explicit code and message, or forward the existing error object so its metadata survives. `Ok(null)` remains a success when the value contract permits null. Recompile against the current SDK after contract changes; there is no text-only `Fail` overload.
