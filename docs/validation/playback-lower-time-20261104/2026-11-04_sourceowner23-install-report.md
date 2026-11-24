# S3A 真实 group23 安装与三 SourceOwner 采用验证报告

日期为2026-11-04。本批完成正常 broker 的23-hook安装、三个真实 SourceOwner的一次采用、采用后lower-time private original原调用，以及独立错误版本和byte-only反例。fresh构建实际natural0，W0/E0。12个测试进程全部natural0，共854项检查，按9个harness场景582、2个版本API场景24、新byte-only场景前置233加额外15分账。验收边界为S3A，managedCallbacksAccepted、adapterAccepted、sdkAccepted、overallS3Accepted均为false。[范围](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/scope.md)

## 固定输入与实现

217输入由213基线、ABI两个新增文件和两个独立harness文件组成。Runtime/Sdk/harness均fresh构建。精确产品delta为18路径，16M加2A，harness另2A。Sdk29个源路径与基线hash全部一致。compile path、冻结原件与PDB文档的归属逐项闭合。[源码归属](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/final-source-ownership217.json) [精确delta](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/final-managed-delta-r1.json)

完整owned23 issuer独立选择8ea6b0…/98353e…两图，19个普通source与GetTime采用native StockMethod元数据。三个native member共享lower module并保持no-MethodInfo。Group注册所有owner后建立三目标family，reserve三channel、一次Prepare三entry、逐一Issue/Adopt三票，按lower-pitch、engine-pitch、lower-time固定顺序Commit。SetTime/LowerSetTime在首次Arm前实际配置。Bridge reserve允许role22/kind8，Hub帧validator仍拒role22与旧role携2048。独立静态review的18个产品hash与fresh编译源全部一致。[review绑定](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/final-product-review-binding-r1.json)

## 实际进程结果

| 场景 | 自然退出 | 检查 | Prepare/Commit | 主要观察 |
| --- | --- | --- | --- | --- |
| install | 0 | 233 | 23/23 | 三票adopt均1，三成员Committed，13原调用、13stores、26边界 |
| wrong-profile | 0 | 21 | 0/0 | 新两图经旧22 issuer拒绝 |
| pre-metadata/cache/registration/wrapper | 全0 | 88 | 全0/0 | 改动后的固定证据拒绝 |
| old-source-helper | 0 | 67 | 0/0 | additive export缺失，实际失败group保留 |
| old-single-helper | 0 | 67 | 0/0 | additive export缺失，实际失败group保留 |
| tamper-time | 0 | 106 | 23/23 | 首先检测同页protection变化，恢复后sticky失效 |
| 两个独立version-v2 API | 全0 | 24 | 全0/0 | 实际2/1版本组合触发精确V1拒绝，owners0到0 |
| third-tail-byte-only | 0 | 前置233加15 | 23/23 | byte修改后先恢复RX32，明确byte拒绝，恢复后inactive |

主install使用真实owner/receipt验证错owner、重复Issue、重复proof Adopt、重复channel Adopt和重复Reserve拒绝，Current继续成立。采用后的private original真实处理+0、-0、普通值、subnormal、infinity与NaN payload，原生raw32 store逐值相等。该路径managedCallbacks和ClockReads均0。[主进程](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/attempt-r1/install/process-result.json) [主stdout](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/attempt-r1/install/stdout.log)

错误版本fixture在固定export表RVA111008与110928处核完整`b801000000c3`叶函数，仅改为`b802000000c3`，每份文件只有一个立即数字节变化。各API进程先实际读取版本，再正常调用fresh Runtime Hub构造函数，在hash验证之后到达精确V1分支。ProcessOwners均0到0，Dobby未加载。它们单独计API覆盖。[补充输入与补丁](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/api-negative-r1/inputs.json)

最初tamper-time同时改变byte和页保护，其实际首拒来自protection。新增byte-only进程以不可变harness Main正常Bind为必要前置，将第三body+304从0x90改0x91，先恢复PAGE_EXECUTE_READ32，再取得“relocation byte changed”异常。恢复原byte后实际group仍inactive。[byte-only原件](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/api-negative-r1/third-tail-byte-only/process-result.json)

## Evidence → Finding → Path

| E-id | source_ref | content_hash | repro_command |
| --- | --- | --- | --- |
| E01 | attempt-r1/build/process-result.json | 64df5966fcb325dd668e8e3f8a7dad6e930fa7b7a2b63a5ad01dc345596bdff9 | Get-Content -Raw attempt-r1/build/process-result.json |
| E02 | attempt-r1/install/stdout.log | 740c2015934d6c5092b5116e9d568ec2c579f6b5adca020c4015e076439be12c | Get-Content -Raw attempt-r1/install/stdout.log |
| E03 | pdb-source-ownership-r1.json | ad992f38047720f18dc3c916e9b5af4b7d111252249025606fbac03b4fee45cf | Get-Content -Raw pdb-source-ownership-r1.json |
| E04 | api-negative-r1/inputs.json | c3e958cea3089a1604b5f6726de2ad753d304b3450d2a14cc4e5d52e3cc2aa92 | Get-Content -Raw api-negative-r1/inputs.json |
| E05 | api-negative-r1/third-tail-byte-only/stdout.log | 3bae8fa0cfa611d1d31fee4c5f3bb6bfba9604cab8d22c44d89159053c715a9a | Get-Content -Raw api-negative-r1/third-tail-byte-only/stdout.log |

以上repro_command在本scope根目录读取不可变证据。每个process-result保留当时实际argv、PID、creation FILETIME、起止时间、自然退出和stdout/stderr hash。

| F-id | severity | evidence_ids | confidence | location | status |
| --- | --- | --- | --- | --- | --- |
| F01 | n/a_re | E01,E02,E03 | high | ExactHookBroker.Playback.Group/SourceOwner | validated |
| F02 | n/a_re | E02,E04 | high | Hub.LowerTime/ValidateFrame | validated |
| F03 | n/a_re | E05 | high | third native body+304/group VerifyCurrent | validated |

P01的path_type为callflow。正常Bind登记owner→三channel Reserve→family实际Prepare→各真实Issue/Adopt→固定次序Commit与Current，对应E01/E02/F01。采用后private OriginalAddress→native Before→真实raw32 store→native After，对应E02/F01。固定版本export实际2→fresh Hub正常构造→V1拒绝与owners0，对应E04/F02。新实际group→third尾byte改变与恢复RX→VerifyCurrent字节拒绝→恢复byte后sticky inactive，对应E05/F03。

## 源与符号归属

3新PE/PDB的CodeView identity全部匹配，共220文档，Sdk31、Runtime185、harness4，所有源与generated文档SHA256均吻合。Sdk29路径源hash差异0。原native机械R4与ABI原件作为固定依赖引用，本批三票采用直接消费其真实original/relocation/UW验证。[PDB原件](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/pdb-source-ownership-r1.json) [native输入](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/final-native-inputs-r1.json)

## 准备修订与时间线

第一次prebuild输入校验因外部静态CALL facts发布文件在pin后修订而实际退出1，dotnet尚未启动，attempt目录尚未创建。旧manifest、旧runner与观察到的拒绝记录已保留。旧raw facts字节被同级producer覆盖后不可用。最终build采用a35a81…/74919B的冻结facts，此后全部输入再次逐hash核验。[修订记录](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-sourceowner23-r1/preparation-revisions-r1.json)

时间线依各process-result记录。准备拒绝后完成一次fresh build，随后主install与八个独立harness负例全部自然0。符号验证natural0后执行两个独立version负例和第三byte-only负例，最后冻结217输入、精确delta、报告和artifact index。原harness与产品源码在fresh build后保持本次归属版本。

根只读复核见[记录](D:/riz-build-cache-next/playback-lower-time-r1-20261104/root-sourceowner23-review-r1/test-result.json)。B2新回调与B3 typed恢复分别有独立候选和后续验收，以上854项不代签这些新源码。
