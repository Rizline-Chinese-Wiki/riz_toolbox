# 写入凭据结束、活动名额复用与旧地址失效

2026-10-31，`flavor=null`。这是上一份[writer执行层](README.md)的功能增量，仍在独立D staging，未合入生产树。它解决每owner256个writer名额只能使用一次的问题；没有做性能基准或开放真实游戏控制。

当前原件在 `D:/riz-build-cache-next/playback-stock-writer-lifetime-stage-r1-20261031/`，`completion-r1.json` SHA `e4b5a1e034a77686ecb5417d3ffa573116211af2b9ac22e9d18043de5b4345d8`。最终11进程1739检查全部实际退出0；托管build-r3为0 warning/0 error，5PE/PDB、457源文档匹配；当前独立native helper实际build0、18导出、1PE/PDB和2COFF源证明通过。source wire160/136与writer信息224B不变，新增明确的Release导出，不回退旧helper。

## 实现与所有权

native只允许原owner线程结束未执行或已真正physical-exit的reservation；State=Running、当前TLS writer或已attempt但没有finally的条目拒绝释放。释放从活动表移除，旧地址保留为process-retained tombstone且不分配给新reservation；活动名额可再用。旧pointer/nonce查不到活动条目，旧epoch/nonce不会因名额复用变成新凭据。

managed hub按native nonce登记它实际发行的reservation对象；Run、回调消费、Verify和Dispose都验证确切对象引用及线程。复制对象不能清理真条目，worker Dispose在任何字段改变前拒绝。producer拥有所有准备/执行过的writer责任，只有后继实际完成后才结束前驱；新stock root和producer结束也会清理对应writer。回调内Dispose只提出结束请求，正在执行的原setter仍恰好执行，直到native finally后才回收名额和journal观察。

旧S2和writer后继凭据仍保持不可变，回收不会重写fence、清除未知副作用或解除物理故障。普通逻辑结束继续保留健康九成员物理安装；原setter写后异常仍按原规则撤组。tombstone内存暂按进程寿命保留，没有宣称堆内存零增长或最终释放；性能和地址回收策略后置。

## 实际正向与反例

| Evidence | 原件 | 观察与边界 |
|---|---|---|
| E-LIFE | `checks-r3/test-result.json`及每mode stdout/stderr/exit | 11进程1739；包含下列两个长序列，另in-flight Dispose、复制/worker清理、ABA/alias/clip/SEH和S2 normal/copies |
| E-ONE-ROOT | `checks-r3/writer-lifetime.stdout.log` | 同一实际Pause root下321次原setter调用，超过原256寿命限制；各前驱失效，旧native pointer/nonce在复用后仍不可读；341项 |
| E-STOCK-ROOTS | `checks-r3/writer-roots.stdout.log` | 132次Replay＋132次Pause的真实stock转换、397次setter调用，始终同9次Prepare/Commit；1075项。它不是不同RunId/scene/source的S6跨局证明 |
| E-SYMBOLS | `symbols-r2/test-result.json`、`native-proof-r1/test-result.json` | 实际当前PE/PDB/C13/ASM和每个源码checksum，未借旧17导出helper验当前18导出源码 |
| E-SOURCE | `handoff-r1/changed-files.json`、`changes.patch`、`implemented-source-snapshot.zip`、`verification-index.json` | 基于上一冻结S3的7文件增量，739源归档，933文件哈希回核；没有改生产树 |

`checks-r1`九进程639与`checks-r2`十进程664是较早测试源码版本，保留而不累计为最终覆盖。`symbols-r1`曾在最终执行结果落盘前过早启动，失败已保留；最终`symbols-r2`在完整检查结束后独立子进程实际退出0。

F-LIFE（`n/a_re/high/validated`，E-LIFE/E-ONE-ROOT/E-STOCK-ROOTS）：同一安装的持续writer和stock occurrence能够回收活动名额，旧对象/旧native pointer不复活。F-DRAIN（`n/a_re/high/validated`，E-LIFE/E-SYMBOLS）：结束请求不能越过actual native finally，复制或worker清理不改变真正owner责任。P-LIFE（`path_type=callflow`）：actual native write → Returned/finally → 已验证后继或logical结束 → owner线程Release → 活动表移除、tombstone保留 → 新nonce使用活动名额，见上述实际执行与源码证据。

七根/run/source/DSP与NativeRuntime读回、唯一final Clock、正式provider/module、不同run的条件移交及生产首次准入仍待实现；默认真实Speed/Seek保持关闭。实际readback仍是自有native fixture内存，不能代替完整游戏状态或真实流程验收。
