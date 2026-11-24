# N2 范围

- auth：用户要求继续完整 B01–B19 功能；root 明确分派 N2，并于 2026-10-29 确认 N1 快照已实际捕获后授权下列编辑及独立验证。
- in_scope：`src/RizToolbox.Sdk/GameplayContracts.cs`、`src/RizToolbox.Adapter/Gameplay/**`、`tests/RizToolbox.Gameplay.Tests/**`、`docs/Gameplay-primitives.md`、本验证目录；固定 GameAssembly/既有元数据只读，隔离构建及受控后端测试。
- out_of_scope：其它 SDK/Core/Audio/Visual/FirstInstall/native/pack 修改；游戏启动、停止、安装、GUI；提交/推送；性能优化、全 B01–B19 验收。
- network_profile：offline，不需要网络工具或外部服务。
- sample：`D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll`，SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；既有 `C:/Users/71957/Desktop/绿豆诡计v2/output/il2cpp_output/dump.cs` 只读。
- tooling：已读 ida-reverse 及其先例/工具索引；索引无可用 IDA/MCP，沿用已安装 `C:/Python314/python.exe`、pefile、Capstone 静态回退；无新安装或 GUI。
- status：实现与离线证据包；真实单位校准、原生 Speed/Seek 完整事务和结果/提交影响尚未验收。
