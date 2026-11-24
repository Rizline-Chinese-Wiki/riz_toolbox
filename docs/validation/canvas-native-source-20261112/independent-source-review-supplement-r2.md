# Canvas source 增量复核 r2

当前6个源码增量、最终5个CanvasNativeSource测试文件及Program独立选择器已只读复核，未发现剩余必要阻断。完整记录见 [independent-source-review-supplement-r2.json](independent-source-review-supplement-r2.json)，SHA256 `da662fcd0267122f23b0f87821d7c15477bfd0a0a46b2f8803333cea120a92df`。

原 [independent-source-review.json](independent-source-review.json) 的SHA256仍为 `6264dc30464b950645775d92221b58f22963458830aa52a2932d2d17834ba233`，内容未覆盖。原复核漏掉了真实的publication撤销问题与测试回调目标错误；首轮完整判定真实266/268、自然退出1必须保留。原结论不能验收修改后的源码。

| Evidence | Finding | Path / 修复 |
|---|---|---|
| E-S1：首轮266/268结果及原源码哈希 | F-S1：已取得并登记root后，publisher中的postguard抛异常误记unconfirmed token | 先预分配、登记root并正常确认accept，再由外层N/arrayguard检查来源；根责任仍由实际owner清理 |
| E-S2：当前AdoptPublication/OwnLifetime | F-S2：reference getter返后可能已经撤销，不能再取得新root | 未消费GCtoken仅本地accept，保存admission异常；外层guard及之后来源访问持续拒绝 |
| E-S3：当前数组/Single源码与新增撤销测试 | F-S3：pin、slot root及sample-size回调后需要拒绝后续观察 | pin登记进NativePublicationCall后先guard；每PublicationAccepted后在publishercatch外guard；Canvas传VerifyLocal，旧caller默认null |
| E-S4：DoublePass最终测试源码 | F-S4：row0.progress回调此前修改了尚未观察的row1.progress，合法新快照不应被拒绝 | 仅row1.progress刚返回时更换同一字段，原严格拒绝和清理断言保留 |

P-S1，path_type=callflow：读取token → 本地admission检查 → 如有效则预分配并登记实际root；如已撤销则不消费token → publisher确认accept → 外层guard拒绝 → 已取得pin/root/参数责任正常单次清理。publication acceptance和来源有效性分别记录，未伪造未知释放或在撤销后继续读取。

Program只允许默认全套，或单独 `--canvas-native-source-checks` 的原16分组；其他参数退出2。主控iteration-r2真实16/16、自然退出0、输入未变，复核所列全部源码与测试哈希逐项匹配该批inputs-before。iteration-r1的16/16属于中间图；当前完整268/Runtime51/static6/Audio1001仍需主控独立新矩阵，不能由本补充件替代。

本复核未编辑src/tests、未构建或运行测试、未映射或启动游戏。完整来源双采样、engine/Unity回调、target写入、Hold退出与完整Seek仍按原限制处理。
