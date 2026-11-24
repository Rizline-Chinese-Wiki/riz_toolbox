# 运行模式与发行包

普通预览默认使用 Standard。启动时验证游戏构建和原生安装，随后的宿主 Tick 检查当前登记的通道、线程和状态。Standard 不逐 Tick 重扫完整发布图，也不承诺持续发现任意外部内存修改。需要完整诊断时，在启动前选择 Developer。

`BepInEx/RizToolbox/runtime.json` 是启动配置。没有该文件时使用 Standard 和 Info 日志。可以用以下配置让 Developer 对每次进入宿主的 Tick 重新做完整审计：

```json
{
  "schemaVersion": 1,
  "mode": "Developer",
  "logLevel": "Debug",
  "fullAuditEachTick": true,
  "enableProofPlugins": false
}
```

审计和验证插件是独立开关。自带的验证插件需要显式开启 `enableProofPlugins`、安装对应插件目录并启用插件。旧的 `settings.json` 开关不能单独启动验证。运行中的会话固定使用启动时读到的模式。

配置无效会进入仅诊断状态，保留已安装插件和启用意愿。`runtime-startup.json` 记录进程和两个启动状态。`installationState=Ready` 表示原生观察器已挂接。`hostState` 从 `AwaitingUnity` 进入 `InitializingHost`，随后记录 `RunningFirstTick` 和 `FirstTickReturned`。`AdmissionFailed`、`HostInitializationFailed` 和 `FirstTickFailed` 会记录对应错误。

`firstHostTickSequence` 是进入宿主的首个 Tick 序号。`optionalWritesAllowedByStartup` 在观察器挂接、首个宿主 Tick 返回且当前模式允许写入时才为 `true`。具体服务和操作由运行时各自验证。宿主启动后，`status.json` 的 `runtime` 字段报告实际模式和检查范围。

宿主从版本配置指定的程序集和类型识别首个 Tick。Steam 25977441 配置使用 `Assembly-CSharp.dll` 中 `GameManager` 声明的 `void Update()` 实例方法。首次正常返回后，后续 Tick 沿用已绑定的方法指针和线程。

## 选择发行包

| 类型 | 用途 |
| --- | --- |
| Runtime | 普通运行预览，包含宿主、SDK 实现和固定构建适配资源。 |
| Author | 附带编译引用包、作者模板和示例源码。 |
| Validation | 附带验证插件、原生夹具和采证脚本。 |

源码构建命令在完整仓库根目录执行：`scripts/Build.ps1 -PackageKind Runtime`、`Author` 或 `Validation`。另设 `-DistributionOutputDirectory` 可将发行文件输出到独立目录。默认清单 `basicPlugins=[]`；显式 `-BasicPluginIds @('basic.song-scores','basic.theme','basic.quick-panel')` 选择[基础模组](Basic-plugins.md)。预编译交付 ZIP 按其 `DELIVERY.md` 安装；Runtime-basic 已选择三个基础模组，具体内容以清单为准。安装脚本按清单安装运行文件和所选模组，作者和验证材料留在包里。已有 `runtime.json`、`settings.json`、启用意愿及自有插件继续保留，被修改的所选载荷在写前拒绝覆盖。

## 模块需求

宿主先读取插件清单和启用意愿。已启用意愿、还在等能力的插件会提出硬依赖需求。工具 UI、菜单资源、外观、相机、Sprite、Font、Material 和独立 DSP 时钟会随需求启停。声明被移除后，旧服务视图失效。涉及挂起原生资源的清理由已有的退休队列继续处理。

组合 UI/Input、共享音频、Gameplay SDK 服务及 LOCAL 目录与选择也随需求启停。获取挂载列表只读取状态，首个自有窗口才建立 Overlay。只播放自有 WAV 时不会发现游戏声源，替换与混音共用一次周期发现，彼此独立撤回。最后一个音频消费者离开后，已发出的停止请求仍须读回确认，随后才可释放保留的音源和片段。

### Gameplay 控制状态

声明 `riz.gameplay.controls` 或建立活动订阅后，宿主在生命周期和播放维护结束后采样。稳定状态按 250 ms 间隔更新，已知边界和修改会提前触发下一次采样。`GetState` 与订阅使用同一份快照，包含采样的宿主 Tick 序号和单调时间。首次发布前返回 `NotReady`，快照来源已改变时返回 `TargetExpired`。

每次 `Acquire` 或 `Execute` 仍检查当前游戏状态、会话和操作权限。订阅适合更新界面和观察操作支持，实际控制结果通过命令回执确认。

输入的逐对象 Debug 比较只在有效 Developer 模式执行；Standard 不生成这类字符串或读取比较用游戏源。日志和状态持久化使用有界后台消费者，主线程仅复制状态与性能窗口。实际写入成功、队列压力和诊断失败分别计数，详见[诊断说明](Diagnostics.md)。

`Submit` 把请求加入同一个控制入口，由宿主在第二次边界处理之前执行。命令的 `Snapshot` 可读到 `Queued`、`Executing` 和终态，查询只读取已发布的记录。已排队的命令占用当前会话，调用 `Cancel` 或结束租约可在开始前撤回。发生未确认的写入时，终态为 `Unknown`，关联动作进入不可用状态。普通 `Execute` 使用相同的会话占位和写入检查。

### 维护与清理

相机模块启用后，首次维护负责发现目标。已有覆盖在每个宿主 Tick 维持，发现间隔为 250 ms，空结果和失败也遵守这个间隔。进入新局时可以立即重新发现。

模块故障后，框架先撤回受影响的能力，再清理依赖者和提供者。清理结果不明确时保留相关资源并报告需要重启。音频、视觉和播放的退休工作由各自的持有者推进，独立维护和回调在其他模块出错后继续执行。

Playback 的启动期物理安装保持原有时点。休眠的 Gameplay 服务停止发起源捕获和准备，并保留已经存在的原生完成与清理责任。正在使用的 LOCAL 结果界面按实际运行和展示保留。插件常用的按需能力应写入 `requiredCapabilities`，场景限制仍由对应清单和真实状态决定。
