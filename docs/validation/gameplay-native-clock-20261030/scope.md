# N2 原生媒体时钟与有值参与者：离线范围

- auth：2026-10-30 用户恢复后继续 N2 功能；根任务授权本代理写 `Adapter/Gameplay/NativePlayback*`、Gameplay 测试及本报告。根任务另授权在现 `GameplayClockNativeProfile` 增加只读 `ModuleBase` 一行，并移交通用值执行器测试。
- in_scope：复用既有固定 PE 的 `evidence-r3` 文本；实现媒体秒 DSP 锚点、准确的 instance `Single()` 返回适配边界、暂停态速率参与者、同修订原生策略发布 seam；实际离线编译/受控测试。
- network_profile：offline；NuGet 配置清空全部远程源，只使用本机已缓存的框架引用。
- out_of_scope：账号或凭据操作，游戏启动/停止、GUI、安装、真实音频或成绩流程，性能与长期压力；不改现 V7 native helper/发布协议。
- support_boundary：没有游戏原生 access、连续 pitch writer/source observer、调度 occurrence authority 与正式 return hook 的完整组合时，真实 `SetSpeed` / `Seek` 继续 `OperationUnsupported`。
- history：保留 r1、r2、r3 中间目录；最终证据单独放在 D 盘 r4 目录。
