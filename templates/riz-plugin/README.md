# RizModProject

这是一个最简 RizToolbox 插件模板：启用后会读取当前场景并监听后续场景变化，不修改游戏状态；停用时宿主会自动清理订阅。

## 1. 编译插件

需要安装 **.NET SDK 8**，并准备好与游戏宿主同版本的 `RizToolbox.Sdk` 编译引用包。

如果直接在作者包目录里创建了本项目，按作者包 README 传入 `--configfile` 还原即可；若使用本地源码构建出的 SDK，把下面路径换成实际的 `artifacts\sdk` 目录：

```powershell
dotnet restore --source 'C:\path\to\riz_toolbox\artifacts\sdk'
dotnet build -c Release --no-restore
```

## 2. 安装并启用

1. **先关闭游戏**，把 `bin/Release/net6.0/RizModProject.dll` 和 `plugin.json` 放到游戏目录的 `BepInEx/RizToolbox/mods/wiki.hello/`。
   - **不要**把 `RizToolbox.Sdk.dll` 或任何宿主 DLL（Core/Adapter/Runtime/Loader）拷进插件目录。
2. 启动游戏。如果是全新安装且没有旧配置，按默认规则加载；如果已有安装记录，发一条启用命令（把新的唯一 `id` 写入 `<游戏目录>/BepInEx/RizToolbox/command.json`，或用玩家包的 `Send-HostCommand.ps1`）：

```json
{ "id": "enable-wiki.hello-1", "action": "enable", "pluginId": "wiki.hello" }
```

3. 打开 `<游戏目录>/BepInEx/RizToolbox/status.json` 确认插件是否已启用：
   - 如果场景服务还没就绪，插件会暂时处于 `waiting` 状态，等能力就绪后自动启用；
   - 如果构造函数或回调抛了异常，会在 `status.json` 和 `toolbox.log` 里记录到本插件名下。

## 3. 修改与开发注意事项

- **改插件 ID 或版本号**：必须同时改 `Plugin.cs` 里的 `Manifest` 和 `plugin.json`，保持两边一致。
- **构造函数别碰游戏**：无参构造函数里不要调用宿主或游戏接口，所有初始化和取服务都写在 `Enable(IPluginContext)` 里。
- **不要跨次缓存句柄**：每次 `Enable` 都重新获取服务和订阅，停用后旧句柄会自动失效。
- **按错误码判断失败**：SDK 失败结果统一返回 `OperationError`（包含 `Code`、`Message`、`CapabilityId`、`TargetId`、`Retryable`）。写分支逻辑请判断 `Code` / `KnownCode`，`Message` 只用来展示，不要靠解析文字判断原因。自己构造失败结果请用带错误码的 `Fail(code, message)`，或直接透传已有错误对象；值契约允许空值时 `Ok(null)` 依然算成功。
