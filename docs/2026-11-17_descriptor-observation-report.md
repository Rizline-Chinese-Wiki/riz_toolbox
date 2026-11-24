# 状态与能力查询的事务副作用修复

新预览将身份和能力查询改成只读观察，避免状态心跳触发音频/Clock事务或准备Seek写入对象。六组件Release0警告/0错误，新播放338、formal-host3及三个受控双局SDK场景3869断言通过；合计**4210断言、另4Host**。旧650/832和同字节Runtime/native检查不计入本批。真实Speed/Seek、SDK时钟读出、完整B01–B19与生产准入仍未完成。`flavor=null`。

[预览ZIP](../artifacts/delivery-20261117/RizToolbox-0.3.0-descriptor-observation-preview-20261117.zip)，37,375,540字节，SHA-256 `72d726332a9d6b84e708c46c882467f3f724d54c3698a5d6d68c584d3b0bbd7a`，25文件已安装匹配。见[scope](../work/game-seek-20261117/descriptor-observation-r3/scope.md)、[timeline](../work/game-seek-20261117/descriptor-observation-r3/timeline.md)。

## Evidence

| ID | 原件 / SHA-256 | 事实与边界 |
| --- | --- | --- |
| E01 | [game-result.json](../work/game-seek-20261117/source-verification-r1/game-r1/game-result.json) · `c9873d225a486d0b1ed6ce8dadf1aa7e6ada2a07b75dc0fede7c2416e84d310b` | 旧包source14.8488秒、SDKPause与独立Paused成功，498.455726秒后ReadyExperimental；查询继续阻塞，未发value命令。 |
| E02 | [ready-query-r1-stack.log](../work/game-seek-20261117/source-verification-r1/game-r1/ready-query-r1-stack.log) · `792aecb010b70aff0cee786e63d2ebb645df291f20225044a2a009482a404c1b` | 状态观察误入descriptor→完整provider/source/Clock事务的实际线程栈。 |
| E03 | [descriptor-normal-formal-two-run-r5.log](../work/game-seek-20261117/descriptor-observation-r2/descriptor-normal-formal-two-run-r5.log) · `1d1578dd43359f3008b7808bdc822aca06528ab81eedaca06ffb126f425bf101` | 第一部分observer已通过，但第36检查SDK GetState仍读DSP，追到能力查询调用完整Probe；不是放宽测试。 |
| E04 | [source-freeze.json](../work/game-seek-20261117/descriptor-observation-r3/source-freeze.json) · `1d15feceb2d71bb7934a65bdf6fff1b500c999c6e9f83ed617552d11100c6730` | 1513输入；五Adapter文件变化，Runtime/SDK/native/Examples未改。 |
| E05 | [managed-build-result.json](../work/game-seek-20261117/descriptor-observation-r3/managed-build-result.json) · `be4be55c16dc7399edca3e919adc67cdd4ba6e74e19a4b73e2a7c6259078c9fe` | 六组件Release0/0、formal host3；三步自然exit0。 |
| E06 | [result.json](../work/game-seek-20261117/descriptor-observation-r3/checks-runner/checks-r1/result.json) · `130d921d27d04749af471872e37cf4e489df60f5573f01e2e24f718085916640` | 新产品四播放模式338通过，实际23次Prepare/Commit及3pair；controlled范围。 |
| E07 | [descriptor-checks-r2.json](../work/game-seek-20261117/descriptor-observation-r3/descriptor-checks-r2.json) · `a58e423252f1dd68c307aa1a9abc20cb33eb20b925b5e8a98b6bd7cc04f9fb8d` | 3个受控双局SDK场景3869断言；data query零DSP/pitch/GetTime、copy/thread/epoch/pending边界，保留实际rate/Replay/Resume/跨局/物理代码故障拒绝。 |
| E08 | [service-generation-fixture-correction.json](../work/game-seek-20261117/descriptor-observation-r3/service-generation-fixture-correction.json) · `383374437290c91cd1a84fec1520ae3c920982528d01f77b1cf8d30849a967d0` | r3第一轮669处将native epoch与SDK generation混同；仅改夹具为SDK state=同SDK lease且更新到新run，native身份另验，产品未改。 |
| E09 | [host-checks-r1.json](../work/game-seek-20261117/descriptor-observation-r3/host-checks-r1.json) · `64b44cecf87b2aad3d44130c8d8d8a005116cb6773f8b0f90830d54cdb317c49` | 新链接实际Loader的四个受控宿主场景通过。 |
| E10 | [descriptor-observation-preview-receipt.json](../artifacts/delivery-20261117/descriptor-observation-preview-receipt.json) · `6d153c063e4322702e4a4cf0fbddbe8b08eb4af040fd9966a3fd7b66e15b8b6c` | 新ZIP40成员/25运行文件完整读回。 |
| E11 | [install-observation.json](../work/game-seek-20261117/descriptor-observation-r3/install-observation.json) · `a540b07a6cd685f4cbdddbc7938e1ff2680c9537fd252813d25866c5377b5960` | 21:59:26.2847005安装25运行文件SHA匹配；安装器不启动游戏。 |

3869是三场景内的断言数，包含实际音频参与者反复读回时的断言，不是3869个独立用例。三个场景均是受控metadata/owned fixture、双局、一次实际joint transfer、同一组12次Prepare/Commit及同一final Clock；不签真实Stock foundation。故障场景按Unknown保留根责任，正常场景释放为0。测试DLL、原生输入与最终产品逐一绑定（E07）。旧夹具不完整或CALL profile不匹配的失败保留在descriptor-observation-r2，未放宽运行时准入规则；最终使用运行时已经固定支持的完整Start fixture SHA9fe8cde…作为测试工具，不采用旧产品结果验新源码。

## Finding

| ID | 状态 / 置信度 | 结论 |
| --- | --- | --- |
| F01 | 已修复并定向通过 / 高 | descriptor仅为身份数据，原查询却创建/验证具体provider并触发完整DSP/Clock读取。新observer按真实host/session/run、armed/withdrawn/group-active检查返回数据；真正Capture仍完整授权（E01/E02/E04–E07）。 |
| F02 | 已修复并定向通过 / 高 | SDK GetState的能力查询经Availability调用完整Probe。Seek Probe还CaptureSeek、PrepareTimeWrite和获取judgement backend。新rate/Seek availability observation只描述请求入口；请求目标的全部依赖和writer检查留在真正Prepare/Execute（E03–E07）。 |
| F03 | 已实现 / 高 | Ready维护保留fault/drain，对同一准确run/session且无handoff/pending时不重复创建/验证provider。改变run、失效或pending走原完整路径；此调度谓词不授予值操作权限（E04/E07）。 |
| F04 | 已实现 / 高 | formal host注册在同一无consumer同步段只用一次完整formal Clock验证，再做本地注册复核。新具体provider在实际Capture时按原协议创建；控制检查没有被metadata观察替代（E04–E07）。正式stock分支仍须新实机。 |
| F05 | 未完成 / 高 | 旧游戏的ReadyExperimental是旧包阶段证据，不能验新包。真实value/readout、可闻同步、整局与生产准入尚未闭合（E01/E10/E11）。 |

## Path

**P01（E01/E02/E04/E07 → F01）：** 只读身份接口→真实注册host/session/run的局部检查→不可执行descriptor。Backend状态捕获优先观察数据；数据不发行source、writer、readout或Clock。实现[ProviderHost](../src/RizToolbox.Adapter/Gameplay/NativePlaybackStockClockRouter.ProviderHost.cs)与[NativeBackend](../src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.cs)。

**P02（E03/E04/E07 → F02）：** CurrentProbe显式observationOnly→rate/Seek request-entry observation→零DSP/pitch/GetTime或参与者创建。RequireSupported默认仍完整Probe；Capture和PrepareSeek的实际source/Clock/writer/judge/目标验证保留。实现[rate接口](../src/RizToolbox.Adapter/Gameplay/NativePlaybackRateProvider.cs)、[Seek入口](../src/RizToolbox.Adapter/Gameplay/GameplayNativeBackend.Seek.cs)、[实际宿主](../src/RizToolbox.Adapter/Gameplay/NativePlaybackStockRateHost.cs)。

**P03（E04/E05/E07 → F03/F04）：** 每次Ready维护先处理完成和故障→只对同一准确当前association和armed session保持等待→真正变化重新全验证；注册formal host的重复源/时钟验证在没有native consumer的同步边界内合并。没有跨音频操作缓存成功票。

复核命令、子进程、精确输入哈希与自然退出码见E05–E09。当前新源码使用新字节检查，r1/r2旧构建和失败夹具均不作为本批验收。
