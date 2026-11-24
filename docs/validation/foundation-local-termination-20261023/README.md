# LOCAL 终止隔离与有限退休

2026-10-23。**1148/1148、25 套完整构建通过，仅暂存；当前游戏安装仍为 1021 四门包。** Runtime130、Hooks82、Audio325；较1081批增加67组离线检查，测试数量不表示底层覆盖率。两项原生 helper fixture 分别31261/61358通过，它们不等于游戏新入口验收。

本批遵循[范围](scope.md)，实现经过[多代理交叉审查](independent-review.md)。完整日志保留13条共享FakeNative字段未赋值的CS0649警告，没有编译错误；最初Hooks缺少using的失败日志也保留，不能把后续通过改写为从未失败。

## 实现与边界

| 实现 | 现在能证明的范围 |
|---|---|
| 第五个controller.LevelOver@3C0C00前置门 | 与Retry共用精确controller角色；ordinary原调用与受保护调用分别测试。新生产入口尚未在游戏解析/挂接 |
| Progress.Update@400FE0与三个Timeline ABI | original恰好一次，finally退出，thread-local精确上下文；sealed LevelOver claim先标记尾部，再处理worker、handler故障和重入。UInt64结构返回/参数经真实unmanaged calli验证，未把helper fixture当作该游戏调用点的Dobby验收 |
| 实际NativeRuntime桥接 | 独立controller/info/progress根、双向图核验、纯immutable身份分类、五门与Progress实时在途、实际Unity帧与单调检查；Closed证据后先撤映射、再确认在途已退才free |
| 单次结果队列 | LevelOver只排队，晚帧且真实在途为零才Create/Capture；实际ControllerOver结果由既有capture实现处理，结果/UI/commands失败有界保留。缺完整依赖与原生集成时不能开放admission |
| 未激活封闭事务 | 自己发现prefab、先开Awake窗口/捕获globals再clone与注参；未暴露激活入口。持续Awake0、相同globals、无Info/结果及五个Unity对象dead才可在host发行退休证明；Close后仍等待bundle后帧完成 |
| 有界factory责任 | 构造即固定exact registry/session，预留32进程槽后接管root；历史同地址session不能释放新root，未知free不重试、不退配额 |

`NativeNeverActivatedRetirement` 的证明只适用于它自己未发布原生调用源的封闭图，不能由外部提供几个bool拼出证明，也不能推广到已激活局。首次确认dead的Tick直接return，下一Tick才发行证明；下一Tick允许与确认处于同一Unity帧。请求Destroy、确认dead以及bundle结束分别遵守代码中实际的晚帧条件。

生产模块仍未安装完整termination handler、result sink或Retry/Back事务，`RequestPlay`保持不可用。当前pending资源与结果capture步骤不等于实际设置初始化、Awake/激活验收、开始事件关联或已激活消费者退休。旧helper临时root记录覆盖限制见审查文档；异常保留unknown不表示已释放。

## 可复核证据

- [build-summary.json](build-summary.json)：25套1148/1148，完整[build.log](build.log) SHA256 `7899eb15514374060bcc7cbbb813383d9df8525c2e328aa07eecf5783540622b`。
- [staging-check.json](staging-check.json)：新18文件；[source-integrity-check.json](source-integrity-check.json)：346份构建输入。
- [source-archive-check.json](source-archive-check.json)：逐项冻结于source-inputs.zip，ZIP SHA256 `a229a76f2b45a4b73fac4b8750b4bf82744dcb7e07bbed5ec5dec7af8ecb69e0`，manifest SHA256 `4b876c3b83e2432b7e5b64ab6a008c6fb29dc4cbc5632b53842d4823a594fc56`。
- [previous-installation-check.json](previous-installation-check.json)：当前1021安装18项与其326份归档输入分别核对；当前工作树是新实现，不能当成旧1021输入。
- [original-game-check.json](original-game-check.json)：三个原游戏文件哈希不变。

```powershell
Set-Location C:\Users\71957\Desktop\riz_toolbox
& .\scripts\build.ps1 -NativeOutputDirectory D:\RizToolbox-build-cache\local-termination-20261023
```

上述命令会重新构建暂存输出，不安装或操作游戏。历史原始日志/归档独立保留；再次开发改变工作树后，应核对历史归档，不要求旧source-integrity脚本证明新工作树未改。只有需要核对本批仍是当前暂存时，才运行本目录summarize-build.py --staged。

完整B01–B19继续，仅B03整项与B18a子项已勾选。本批没有新游戏会话验收或新增任务勾选。

下一步见[激活前责任预留](next-activation-reservations.md)；LOCAL之外的实现缺口见[全范围剩余审计](foundation-remaining-audit.md)。后者区分实际缺实现与已有实现待实测，不把所有底层工作缩为本地谱面事务。
