# Stock 来源、宿主局身份与主目录合入

2026-10-31，`flavor=null`。本轮已经把S2实际stock/leaf调用、S3私有native writer、writer生命周期、七根/API读回和新增宿主局身份五批实现合入工作树，共25文件。合入前7950个项目文件已归档；745源逐字节核对，只有原合同声明的EngineWait snapshot runner转换保留工作树版本。没有提交、发布、安装或启动游戏，也没有操作游戏UI和账号。

合入后已用新源码独立完成：**27套2465/2465、六DLL Release、solution零错误/零警告、SDK独立双插件14/14，全部actual exit0**。Release有1个既有generated nullable warning。联合结果为`D:/riz-build-cache-next/playback-stock-main-integration-r1-20261031/validation-r1/test-result.json`，SHA `b072df26ead9330e494388cbce8b192b07b20f287c8c97e0047d761ff104d0f9`；3162个唯一输入/证据文件回核，索引SHA `e9f0c48f36c293450034880bd98aa9fb8b6b1e7fc6ba0f8f81a507a33ef5eae7`。这只闭合本轮源码整合，N2与B01–B19整体仍未完成。

## 实现与边界

新增`GameEventState.PlaybackRunAssociation`来自共享宿主实例的私有对象登记，包含该宿主的session、RunId、scene、controller/information与递增观察epoch。相同字段的record、手工构造和MemberwiseClone不能进入登记。当前关联稳定时重复capture复用同一票；观察丢失、场景/控制器变化、runs不可用、结束和取消先撤旧票，再通知生命周期监听者。恢复当前地址不会恢复旧票，必须重新观察controller后另签关联；Controller尚在提交时拒绝重入capture。

`NativePlaybackStockRootedSource`现在强制消费这张宿主票，在根和API读取前后复验；宿主撤回同步停止逻辑来源。一个监听器失败不阻止其他来源撤回。失效若发生在真实native Returned内，七个根仍保留到本来源的实际journal和writer责任完成，随后在owner线程释放。正常Pause/Replay/Resume、实际schedule参数、native保留writer和独立API读回保持原路径。

这只证明**共享宿主已经观察到的run/scene关联**。测试使用合成IL2CPP metadata与真实自有native stock fixture、Dobby、Runtime API和GameEventState实现，没有真实游戏运行。未观察到的source字段ABA、完整source generation及底层engine writer覆盖仍缺；唯一final Clock、正式stock provider和跨局handoff尚未实现，真实Speed/Seek与production继续关闭。

## 证据

| Evidence | 原件 | 结论 |
|---|---|---|
| E-RUN | `D:/riz-build-cache-next/playback-stock-run-authority-r1-20261031/checks-r2/test-result.json` | 15个独立进程、326项，全部actual exit0；含观察gap、availability恢复、scene/controller ABA、over/cancel、读取中失效和Returned内清理 |
| E-SYMBOLS | 同目录`managed-build-r1/`与`symbols-r2/test-result.json` | build零警告/零错误；5 PE/PDB、465源文档；实际exit0 |
| E-PATCH | 同目录`handoff-r1/`与`patch-review-r1/test-result.json` | 745源ZIP、5文件增量；实际git apply检查并比对全部源 |
| E-MERGE | `D:/riz-build-cache-next/playback-stock-main-integration-r1-20261031/test-result.json` | 五批25文件已写入主目录；未覆盖无关源码；改前归档内旧core-r4的727输入hash相同 |
| E-NEWCORE | `D:/riz-build-cache-next/foundation-stock-run-integrated-core-r1-20261031/test-result.json` | 新源码738输入，coordinator28、receiver10、Audio1001，实际exit0；整树full/release/solution/SDK另行验收 |
| E-FULL | 同级`foundation-stock-run-integrated-{full,release,solution,sdk}-r1-20261031/` | 27套2465、六DLL、solution 0/0、SDK双插件14，均actual exit0 |
| E-GENERATION | E-MERGE目录`old-proof-current-source-rejection.*` | 旧core-r4默认current校验实际拒绝合入后的GameEventState；显式历史归档模式不得验收新树 |

E-RUN冻结交付`completion-r1.json` SHA256：`5ee6c97cbb5ef008e4b9a372e58b3ca453d35acb5a62fc09348def5fb64d4f7a`。E-MERGE SHA256：`7b9bd5ce4a998e8368be9db62683efeaf22221e5443dc86d2cd51967d2665457`。

F-RUN（`n/a_re/high/validated`）：E-RUN/E-SYMBOLS证明观察恢复和相同地址不再让旧播放来源取得宿主局身份；未观察的原生字段写者不在其范围。F-MERGE（`n/a_re/high/validated`）：E-PATCH/E-MERGE证明已有五批代码已经进入当前树，E-NEWCORE仅验其具体回归范围，不能替代整树验收。P-RUN（`path_type=callflow`）：共享host Controller观察→私有对象登记→rooted source/API前后核验→实际native occurrence/writer读回；任何已观察失效→同步logical withdrawal→保留本来源在途根→实际drain后释放。

## 失败保留与下一步

checks-r1的326项实际通过，但源码清单较窄；最终checks-r2覆盖所有测试源，包括被链接的FakeNative文件。symbols-r1因基线ZIP不包含符号验证脚本而失败，原始stderr/exit1保留；symbols-r2先冻结实际验证定义，再核PE/PDB/source。首次新core命名碰到已有目录，工具拒绝覆盖，随后采用独立的`foundation-stock-run-integrated-*`目录。联合collector初版把migration的before事实对当前已修改路径比较而失败，脚本/实际exit1/stdout/stderr已保留；修正版只将这些before事实对确切pre-write归档验证，所有after事实与738 current输入、745源仍核当前树。最终collector actual exit0。没有把任何失败改写成成功。

下一步实现唯一final Clock与GetTime policy的dormant→arm交接，继续concrete provider/module和同物理安装跨局conditional handoff。旧S1的三个映像进程继续运行；收尾时只对其原core-r4和确切pre-merge归档封存，不能复用其旧结果验收这次源码。任意Seek完整重建、生产首次准入及B01–B19总验收仍未完成。
