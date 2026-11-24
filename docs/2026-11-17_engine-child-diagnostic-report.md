# Engine 音调子调用诊断（2026-11-17）

私有 V2 诊断已构建、完成四种原生机制检查并安装。当前预览用于定位实际恢复流程中 Engine 音调发生为何没有取得精确 child 标记；完整游戏 Speed/Seek 和 B01–B19 仍未完成。范围为用户授权的本机 Rizline SDK 开发，flavor=null。

## 实际问题与本次改动

上一 V1 包的新游戏实际会话在恢复后出现 `status4 / origin=source / sourceMask=0x2`，当前 `EngineSetPitch / kind8 / sequence11 / parent10 / depth3 / argBits=0x3f800000 / childFlags0`。这确定了 LowerPitchBlocked 和当前音调发生，尚未确定父10角色、首个失败条件或历史置位者。[本包独立游戏结果](../work/game-seek-20261117/fresh-block-diagnostic/fresh-diagnostic-r1-game-result.json)保留安装哈希、会话和日志；没有借离线结果判定游戏通过。

固定 lower495B / engine640B 与当前磁盘 UnityPlayer 匹配。lower原样传float、同函数唯一Engine CALL、native self无额外偏移；[静态复核](../work/game-seek-20261117/engine-child-analysis/static-verification.json)不支持放宽参数或caller判定。

本次6 native + 2 CLR只记录原判定**实际已求值**和匹配的位，保持原短路次序、getter次数、锁、flags、票和同一次Read。FailureV2为136B/pack8、DiagnosticVersion2；原208B身份快照仍只在成功时消费。扩展仅在当前Engine/Icall scope投影真实父scope、Begin/Child masks和lower函数内caller偏移；引用参数归零，不输出根地址。没有新增Source query、重发票或放宽准入。[位合同及六native哈希](../work/game-seek-20261117/engine-child-diagnostic/native-mask-contract.md)列出Begin21位/Child29位及具体布局。

## 当前字节的验证

1272个编译输入、8处变化已[冻结](../work/game-seek-20261117/engine-child-diagnostic/source-freeze.json)。新[native/托管构建](../work/game-seek-20261117/engine-child-diagnostic/native-build-result.json)及[11项托管步骤](../work/game-seek-20261115/engine-child-diagnostic-r1-result.json)全部退出0。新[独立runner](../work/game-seek-20261117/engine-child-diagnostic/checks-runner/runner-source-freeze.json)包含5源与13个实体metadata bootstrap；当前执行结果绑定新DLL与helpers。

| 新原生机制用例 | 断言 | 验证边界 |
| --- | ---: | --- |
| Replay | 113 | 同真实factory票贯穿原调用树，精确Engine child与DSP schedule保持 |
| Resume | 133 | 同票、原调用/实际物理退出、child和schedule保持 |
| direct-time | 46 | worker10不闭合owner票，真实直达时间写入拒绝4/mask4 |
| direct-engine | 46 | 真实cached native self直达Engine拒绝4/mask2；tested1f/matchedf，父/Begin/caller投影0 |

[四个独立进程338项](../work/game-seek-20261117/engine-child-diagnostic/checks-runner/checks-r1/result.json)全部通过，每个实际23/23成员+3pair、4root→0、真实factory claim1。拒绝用例没有执行票；原函数与物理退出、journal排空/根退休均完成。它们未执行stock foundation、Unity、正式stock adoption或Clock，不能代替游戏正向验收。

## 交付与下一步

[当前ZIP](../artifacts/delivery-20261117/RizToolbox-0.3.0-engine-child-diagnostic-preview-20261117.zip)为37,305,899B，SHA `9a9173ded64c0b7c7769da7c463adea65222a7501e088c054da3e806704d0e04`；[打包回执](../artifacts/delivery-20261117/engine-child-diagnostic-preview-receipt.json)核35成员/25运行文件读回。[实际安装](../work/game-seek-20261117/engine-child-diagnostic/install-observation.json)于10:43:53本地完成25/25匹配。SDK公共合同未变，旧V1包与原件保留。

Evidence→Finding→Path：V1实际会话指出当前Engine未识别child；固定原件排除float变换假设；V2同次记录保留原权限行为，338项验证限定机制透明；下一步只读取**本新包**的真实父scope和首个失败位，再修实证路径。当前安装不代表游戏通过，后续实际状态以[续接点](../.docs/CURRENT-CHECKPOINT.md)为准。
