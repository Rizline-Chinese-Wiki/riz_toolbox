# 当前候选 SDK 分发与参考包

最新939输入候选已完成独立SDK分发链验证：两个插件各自从模板生成、引用实际SDK nupkg编译，再由正式宿主加载，14/14检查通过。另产出可离线编译插件的参考包，并从实际ZIP解压后跑通文档四步。主图仍928，producer原main-r3长回归未结束；这不是完整SDK、B01–B19或游戏运行时验收。

| 证据 | 支持的行为 | 来源 |
|---|---|---|
| [当前SDK结果](validation/sdk-current-hold-20261114/run/result.json) | formal-host-build及sdk-distribution自然退出0，14项全过 | [两个独立生成项目](validation/sdk-current-hold-20261114/distribution/generated/) |
| [实际原root退出](validation/sdk-current-hold-20261114/controls/run-r3-root-exit.json) | controller41108/root58050实际0/chunk32dc11，对应当前939+两个额外Distribution.Tests源码 | [冻结输入](validation/sdk-current-hold-20261114/source/) |
| [SDK归档清单](validation/sdk-current-hold-20261114/validation-summary.json) | SDK包、直接宿主产物、插件源码/产物、缓存依赖和原失败日志均可在归档内核对 | [archive-only检查器](validation/sdk-current-hold-20261114/controls/publish_evidence.py) |
| [参考包ZIP](../artifacts/sdk-reference-candidate-20261114-hold939.zip) | SDK1.2引用包、原模板、四个本地.NET6依赖包和校验源码；没有RizToolbox游戏运行时 | [交付清单](../artifacts/sdk-reference-candidate-20261114-hold939/manifest.json) |
| [解压实跑结果](validation/sdk-reference-kit-20261114/run/result.json) | 独立hive安装模板、生成MyMod、全新缓存离线还原、Release编译，四命令全部自然0 | [实跑root退出](validation/sdk-reference-kit-20261114/controls/root-exit.json) |

14项覆盖SDK引用包结构/身份、实际restore/build、插件不携带宿主实现、不同加载上下文、激活/事件/停用/能力恢复，以及结构化已知/未来错误、worker/过期服务视图、私有合同拒绝和提供者故障隔离。离线变体保留原始探针和全部14检查，只调整依赖传输、隔离输出目录和构建编排。四个第三方缓存包原字节复制、未改签名；不访问外部feed，不修改全局模板、账号或永久环境。

失败历史保留：r1遗漏不在foundation scanner中的Distribution.Tests两个源文件，构建因项目缺失失败；r2宿主W0E0且SDK打包成功，但初始本地feed漏带WindowsDesktop.App.Ref，插件还原失败，没有声称任何conformance PASS。修正暂存和依赖feed后，r3重新构建并运行完整14项，939及额外冻结输入不变。

SDK归档1034文件/31,866,849字节，发布root40259实际0/chunk29e37c；直接归档checker即时工具实际0/chunk818147，无虚构session。summary SHA256：`012350764368913ea506e06d57b636cd9a02374091c7468af0e90e88d67724ff`。当前SDK原root receipt：`81f403d7bf7599dcc43c09364c92bbb5365330306e7ee4e6acd07b3f576a7429`。

参考ZIP18,385,889字节，SHA256：`d83ecee11dab54710c0bd632a32f1080dca9e3b929f9ff89820d637e7d700294`。生成工具实际0/chunk62a5b9并逐项比对ZIP与目录字节。解压实跑controller19948、即时工具实际0/chunk9525cd，使用独立NUGET_PACKAGES子进程环境和空缓存，并确认实际还原nupkg与交付manifest包hash一致；没有用旧全局同版本缓存代验。实跑证据20文件，summary SHA256：`8defb677416cdefa57f3a0ca750759d77d98092d852906e16c93e2ea7770e950`，归档工具实际0/chunkcb9e6c。

参考包用于SDK编译/模板检查，verification-examples是分发校验探针，不是游戏功能模组。本批未合主、未重跑完整managed矩阵，不认证真实Speed/Seek、HoldIdle、生产首次准入或B01–B19。未安装/启动游戏、操作游戏UI/账号、改永久环境、提交Git或覆盖旧ZIP。旧881输入运行时预览包原样保留。
