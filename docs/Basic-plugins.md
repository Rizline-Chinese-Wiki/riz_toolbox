# 基础模组预览

本轮提供三个独立的普通 SDK 模组，使用 Standard。代码已接入主工程，当前候选的离线结果见交付报告。三个游戏内场景、停用后的可见恢复、重启和实机成本仍待验证，包状态保持 `preview/unverified`。

| 模组 | 可操作内容 | 何时更新 |
| --- | --- | --- |
| `basic.song-scores` | 歌曲与最好成绩面板、搜索、翻页、手动刷新 | 首次显示、换场景、点击刷新；失败最多每 5 秒重试 |
| `basic.theme` | 菜单按钮颜色及指定颜色目标 | 配置在启用时读取，目标声明每 5 秒检查 |
| `basic.quick-panel` | F8 显示/隐藏面板，选择并播放、停止自有 WAV | 显式点击播放/停止；换场景和停用请求停止 |

每个模组声明其所需能力。能力尚未就绪时，宿主会记住启用意愿并等待。Windows Steam 的 Basic 玩家包包含表中的三个模组。解压 ZIP，按根目录的《安装说明.md》操作。纯底层 Runtime 版供自行安装其他模组的玩家使用。

以下构建命令在完整源码仓库根目录执行；交付 ZIP 提供预编译载荷和安装脚本。默认 Runtime 不选择模组；从源码构建时可显式选择：

```powershell
./scripts/Build.ps1 -SkipTests -PackageKind Runtime `
  -BasicPluginIds @('basic.song-scores','basic.theme','basic.quick-panel') `
  -DistributionOutputDirectory 'C:/riz-preview'
```

`-SkipTests` 只控制这次构建是否运行检查，不赋予验收状态。Author/Validation 也接受同一选择参数，其作者或验证材料留在分发目录。清单中的 `basicPlugins` 必须与实际载荷一致。

Windows Steam 玩家包使用随包的 `Install-EngineTransportCandidate.ps1`。从源码单独构建的预览分发目录使用 `Install.ps1 -GameDirectory <游戏目录> -DistributionDirectory <含清单的目录>`。安装器记录所选模组的归属，遇到用户修改或自装的同名文件会在写入前停止。升级会保留原有模组和配置。

首次安装没有启用意愿文件时，所选基础模组采用宿主默认启用。已有 `activation.json` 会保留；新增模组需要显式启用。可在运行后发送一条命令，再等 `status.json` 和日志的实际处理结果：

```powershell
./scripts/Send-HostCommand.ps1 -GameDirectory <游戏目录> -Action enable -PluginId basic.theme
./scripts/Send-HostCommand.ps1 -GameDirectory <游戏目录> -Action disable -PluginId basic.theme
```

命令运行后可在 `BepInEx/RizToolbox/status.json` 的插件列表确认实际状态。

## 配置与 WAV

配置文件相对游戏目录位于 `BepInEx/RizToolbox/config/plugin-<模组ID>/config-<名称>.json`。名称分别为 `song-scores`、`theme`、`quick-panel`。首次使用只读取内存默认值；点击保存才写配置。无效配置会保留并报告原因。

配置样例在 `examples/RizToolbox.BasicPlugins/configuration/`。歌曲和快捷面板有 Reload settings 按钮；主题须先停用、编辑 `config-theme.json`，再启用。主题默认选择当前菜单按钮颜色，最多 16 个目标；也可配置 `TargetKeys`。撤回颜色层后，由宿主恢复基线或剩余层，实际画面恢复仍需验证。

默认 WAV 的完整路径相对游戏目录为：

```text
BepInEx/RizToolbox/plugin-storage/storage/plugin-basic.quick-panel/data/audio/owned.wav
```

支持自有 PCM16、单/双声道、8–192 kHz、44 字节至 1 MiB 的 WAV。预读用于显示存储错误；实际音频加载在打开文件后按 `ContentFile.MaximumBytes` 执行 1 MiB 限额，文件被替换后仍按新打开的文件检查。包不附音频，不自动播放。面板保存的路径、音量和循环设置用于下一次 Play WAV。隐藏面板保留当前播放；换场景或停用请求停止。

停止失败会保留音源和片段的责任，阻止启动替代播放，面板报告错误。异常启动、成功却没有返回停止租约、未返回结果都保留 WAV 和作用域，需交宿主清理。日志中的请求成功只证明提供方返回了结果，不代替听到声音或观察恢复。

## 验收记录

`New-PreviewAcceptanceRecord.ps1` 只读记录当前 Runtime 清单、载荷、安装归属、所选模组/WAV 与会话声明。`Test-PreviewAcceptanceRecord.ps1` 再核对同一候选和来源，写独立结果目录。用户报告的游戏场景与文件一致性分开；记录始终为 `preview/unverified`。倍率、Seek 和完整 B01–B19 继续保留各自未完成状态。
