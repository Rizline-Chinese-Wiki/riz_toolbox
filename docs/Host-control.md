# 宿主控制与插件包更新提示

运行中的 Loader 接受 `BepInEx/RizToolbox/command.json` 中的一条请求。在解压后的 `delivery` 目录使用脚本；先查询状态，后续每条命令等待实际处理结果再发送：

```powershell
./scripts/Send-HostCommand.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline' -Action status
./scripts/Send-HostCommand.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline' -Action set-language -Language zh-Hans
```

插件 ID 从实际 `status.json` 的 `plugins` 中选取。安装 Runtime-basic 后，可分别发送以下命令停用、启用主题：

```powershell
./scripts/Send-HostCommand.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline' -Action disable -PluginId basic.theme
./scripts/Send-HostCommand.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline' -Action enable -PluginId basic.theme
```

脚本原子写入带新 ID 的请求，返回 `Dispatched=false`。这只表示请求已保存；实际处理结果看同目录的 `status.json` 和 `toolbox.log`。这是单条命令邮箱，下一条写入会替换尚未处理的请求；等待本条结果后再发下一条。宿主约每500毫秒检查邮箱，执行仍依赖游戏主线程能响应。

## 停用本次 SDK 会话

```powershell
./scripts/Send-HostCommand.ps1 -GameDirectory 'D:/SteamLibrary/steamapps/common/Rizline' -Action shutdown
```

`shutdown` 在记录的游戏主线程上取消后台命令处理、停用插件、释放适配器和包上下文，并取消剩余调度请求；本帧不再运行 Adapter tick 或发布 `FrameUpdate`。这条命令不调用游戏退出。停用后 SDK 不再接受命令，重新启用需要重启游戏。

最终状态包含 `stopped=true`、`stopReason="command"`、实际 `plugins` 状态和 `shutdownFailures`。清理某一步抛错时，宿主会记录并继续尝试其他步骤。`shutdownFailures=[]` 只表示顶层停机步骤没有抛错；提供方内部记录的资源清理失败和待原生退出责任仍以日志、资源计数及独立验证为准。永久原生桥保持原有生命周期，不在后台线程强制卸钩。

原生验证计划里的 shutdown 仍保留其退出测试游戏的行为，与上述普通宿主命令有不同调用入口。

## 插件包磁盘变化

后台每5秒检查已经加载的插件包，完成后把不可变结果送到主线程，供 `status.json` 输出：

```json
{
  "packageUpdates": [
    {
      "pluginId": "basic.theme",
      "restartRequired": true,
      "changedFiles": ["RizToolbox.BasicPlugins.dll"],
      "message": "Package files changed on disk; restart is required. ..."
    }
  ]
}
```

首次检查前 `restartRequired` 为 `null`。后续检查报告相对已加载包快照的新增、修改和删除；文件恢复到原快照后提示消失。无法验证包目录时会报告需要恢复文件并重启。新增的整个包目录要重启后才会被发现。

检查不重新加载 DLL，不改包文件或当前插件实例。包扫描只读取不可变目录和指纹，不持有会阻塞主线程停机的扫描锁。停机后完成的旧扫描结果不会再发布。游戏主线程阻塞时，提示发布和状态写入也会延后。

这些改进参考了 [RD-InFalsus-Mod 的薄加载入口和停机调用](https://github.com/REDDRAGON-HL/RD-InFalsus-Mod/blob/617153d0183128c5e1d7d60acfd872bbb64cd6e8/CustomSong/Source/Mod/Entry.BepInEx.cs)，以及[文件变更在生命周期中处理的组织方式](https://github.com/REDDRAGON-HL/RD-InFalsus-Mod/blob/617153d0183128c5e1d7d60acfd872bbb64cd6e8/CustomSong/Source/Mod/CustomSongCore.cs)。实现使用本项目已有的主线程调度、包指纹和 scope 清理；未复制其代码或异游戏偏移。
