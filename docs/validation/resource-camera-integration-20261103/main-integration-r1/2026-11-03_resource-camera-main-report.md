# 资源连续性与相机横向缩放：主目录离线整合

资源连续性与相机rig横向缩放已精确合入主目录，当前915输入／485托管产品／48原生输入。新主目录实际通过针对性76项、整树28套2620项、独立SDK双插件14项、solution0错误／56既有警告、六组件Release和11对PE/PDB／991份源码文档核对。T18SDK／669与U12功能／275均已闭合，原失败及旧源码时期完整保留；当前增量离线验证完成，整个底层仍未完成。

## 授权与边界

见 [scope.md](scope.md)。没有安装或启动真实游戏，不执行游戏方法或DllMain，不操作游戏UI或账号。相机仍为experimental／可能影响玩法；真实更新与渲染次序、可见效果及恢复未证明。原生Speed/Seek与production没有开启。

## Evidence → Finding → Path

| 证据 | 原始记录 | 结论 |
|---|---|---|
| E-001 | [T完整汇总](retained/combined-T/test-result.json)，原6＋新12来源及[22范围closure](retained/closed-T/test-result.json) | 18个SDK场景、669项actual0，关机中断与旧失败保留 |
| E-002 | [U最终功能](retained/closed-U/test-result.json) | 原11／237＋新SDK38＝12／275全0，独审通过，Dispose仅测试修补回带 |
| E-003 | [U合主](retained/main-resource-application-r1/test-result.json)与[V合主](retained/main-camera-application-r1/test-result.json) | 53＋9精确路径，旧值hash先核，主915保持Dispose，fresh针对性76全0 |
| E-004 | [新主28套](retained/main-joint-validation-r1/managed-matrix-result.json)和[两来源构建链](retained/runners-and-attribution/main-joint-build-chain-r1.json) | 新主2620、SDK14、六Release实际通过；原driver在记录SDK路径处FileNotFound，总体false原件未改，只补未执行的两阶段 |
| E-005 | [11对PE/PDB](retained/symbols-main-joint-r2/test-result.json) | 991个实际SHA256文档与当前编译源一致；专项Runtime包含一个profile，因此不是隔离V的990 |
| E-006 | [最终整合](retained/main-integration-closure-r1/test-result.json)和[历史输入迁移](retained/main-integration-closure-r1/historical-input-transitions.json) | 核8863事实，40条主目录旧字节由894改前ZIP保留，另1条U测试旧字节单独保留；不把旧live路径当成当前源码 |

F-001（validated／high／design，E-001→E-003）：资源引用变动只使未来捕获票失效，当前音源与已有Clock仍可继续控制；相机位置和缩放各自仲裁、恢复，缩放每次保留最新Y/Z。当前源来自已实际验证的913＋9路径。

F-002（validated／high／other，E-004→E-006）：主目录新源码经过实际构建和测试，源码与产物可追溯。首次后续打包因脚本残留缓存路径而中断，原29个成功子命令不重跑，新SDK／Release两命令另scope全0；从未将原aggregate false改成true。

P-001（callflow，E-001→E-002→E-003→E-004→E-005→E-006）：收齐T与U真实终态 → 核8266前置事实 → U53合主／fresh76 → V9合主 → fresh主28／2620 → SDK双插件14／六Release → 11PE-PDB991 → 核915主源及历史迁移。

## 复核与下一步

[retained-artifacts.json](retained-artifacts.json)绑定原件和桌面副本，完整915源码ZIP与894／913历史ZIP已保留。实际命令在各launch／process-result及归档脚本中；保护原件，复跑使用新目录。默认不把本增量离线完成解释为真实游戏、production或全部B01–B19通过。

下一从checkpoint和NEXT-FUNCTIONAL-GAPS继续完整generation／未知lower writer及formal provider、Play外层实际回调与非空来源／native Add队列、Seek和其它明确未完成能力；先功能、后性能，真实游戏继续后置。
