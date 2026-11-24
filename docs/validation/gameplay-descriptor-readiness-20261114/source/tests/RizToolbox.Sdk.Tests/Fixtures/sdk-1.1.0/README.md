# SDK 1.1 二进制兼容样本

这些文件在 SDK 1.2 构建之前，直接复制自本机已安装、已运行的 0.2 游戏插件目录。原始位置、捕获时间及 SHA256 在 `provenance.json`。不要通过当前源码重编译或覆盖样本来“修复”兼容测试。

`RizToolbox.Sdk.dll` 是旧 API 比较基线；测试专用 ALC 只用于反射检查。`RizToolbox.Examples.dll` 是已编译的旧插件；行为测试会把它和旧 `plugin.json` 复制到临时插件包，并通过正式 `PluginPackageLoader` 加载到新的 SDK / Core。测试包不带旧 SDK，因此真实验证了共享 SDK 绑定。

`public-api.txt` 由旧 SDK 反射生成并签入；正常测试同时校验它与旧二进制一致，以及当前 SDK 仍包含这些签名。基线不是从当前源码生成的。

确需重建文本表示时，在仓库根目录执行：

```powershell
dotnet run --project tests/RizToolbox.Sdk.Tests/RizToolbox.Sdk.Tests.csproj -c Release -- --write-baseline tests/RizToolbox.Sdk.Tests/Fixtures/sdk-1.1.0/public-api.txt
```

这条命令只从哈希核验后的旧 DLL 读取 API，不编译旧插件。
