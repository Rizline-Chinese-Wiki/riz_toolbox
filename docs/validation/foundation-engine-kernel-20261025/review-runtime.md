# Kernel 运行时与父验证有限复核

2026-10-25。**root 已记录 full session23528 实际 terminal exit0；本页已按正式归档复核新 Kernel 七个进程及输入的时间边界，未发现阻断本批自有回归交付的问题。** [正式汇总](milestone-summary.json)为25套1617/1617、599项声明源/23项包；Kernel Entry1606、父PE1486、native3402和managed889分别计数。下面保留预跑审查及其哈希，正式结论以本节新归档为准。

**安装补充也已实际完成。** core23528成功后，root进一步实际发现Install.ps1白名单未包含新Kernel，修前自有fakegame Safety确实红测“Invalid package manifest”。[独立安装补充](../foundation-kernel-install-20261025/summary.json)只演进Install.ps1/Test-InstallSafety.ps1/Test-InstallRecovery.ps1三项，core599 ZIP/23包原件不动。root实际观察Safety66373 exit0/16检查、Recovery67106 exit0/13检查；本页只读核终态及三项overlay/现行599源码ZIP，未执行安装或测试。

当前599来源必须解释为core的596项加三项已专项验证安装脚本，[source-overlay-manifest](../foundation-kernel-install-20261025/source-overlay-manifest.json)明确每项before/after；[source-final-manifest](../foundation-kernel-install-20261025/source-final-manifest.json)没有增加或删除路径，当前599 ZIP SHA256 `45a2000d5c1ca81d8404bad649304dcd2ffde7767d84bda66868bfd20b2f4d28`。本页相关生成器逐项核core599归档仍准确、三当前脚本只等于指定overlay、其余596不变；不能再声称当前599均等于core旧hash，也不能宣称又重跑了1617。自有fakegame Safety用实际23项包验证Kernel安装/旧22升级/修改后拒绝/卸载等；Recovery的13项使用合成自有仓库和实际journal恢复，未向Steam游戏安装或启动DLL。补充批[真实游戏保全](../foundation-kernel-install-20261025/real-game-preservation-check.json)保存18已装文件/四原件hash匹配。

本页作者实现了两旧runner的可选绝对路径分支、新Kernel parent与独立PE验证器，因此对这些脚本的审查属于作者自查。其他代理实现的native Kernel、EntryFixture与生产bridge/ledger只做有限交叉读取，不是独立完整审计。审查只读磁盘PE、JSON、旧冻结ZIP及日志；没有构建、启动fixture/游戏、操作UI、安装或改动599项冻结源。范围见[scope.md](scope.md)。

## 正式full原件与时间边界

[build-run.json](build-run.json)记录session23528实际terminal0；[build.log.run.json](build.log.run.json)与构建日志hash一致。正式[Kernel结果](engine-kernel-evidence.json)和[native-build原件](native-build/engine-node-kernel-cases/fixture-result.json)逐对象相等；当前新DLL为[native-build/engine-node-kernel/RizToolbox.EngineNodeKernel.dll](native-build/engine-node-kernel/RizToolbox.EngineNodeKernel.dll)，39424字节，SHA256 `923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662`。它不是预检的41e01c同名文件。Entry child PID57540实际exit0；Entry1606、父级1486、native136original/124pairs、managed44original/28pairs均再次通过，合计180original/152pairs，不包含Entry的零原调用。

本页对正式持有磁盘PE另写独立标准库只读解析，实际GetEntry向量减module.base后扫描四个磁盘literal序列，仍分别在RO非exec/nonwrite `0x9020/0x9120/0x9220/0x9320`；128项与DIR64逐项相等，无额外DIR64。正式actualBase=`0x7ff813110000`、disk preferred=`0x180000000`，delta=`140697006047232`；mappedHeader字段独立记录。五mode结果和原件逐对象相等，每mode四entry反算一致的真实Kernel基址，路径hash均指向新DLL。正式关键原件hash如下：

| 原件 | SHA256 |
|---|---|
| engine-kernel-evidence.json | `66c2583377943575ee56322b6e392ee775415086aec9695abe8b9eb0d7f75832` |
| entries/fixture-result.json | `cd1923315339e7ee9f6473a72006eb795fbcaee885b95d24c441991e78d80421` |
| kernel-entry-proof.json | `877472589e3b115a7ce2eae9d9818c3890df3a64dcab56868c8f21ef5f20ec58` |
| managed/fixture-result.json | `8520531825dc6e1a97b62f36eb9da761c5c8cc32f132b3d26f0782ac1be0818f` |

**31项input仅承诺Kernel runner持锁期间不变。** 正式parent记录全部before=after，但在runner释放锁后，统一构建接着执行[Test-FirstInstallManaged.ps1第29行](../../../scripts/Test-FirstInstallManaged.ps1)，正常将新编译的旧JudgeContext复制到Runtime.Tests旁：该副本从`01e5171d…a66f48`变为`398d1c1b…809ec9`。首次summary错误地强求当前文件也等于更早during hash，实际触发[AssertionError原件](history/first-summary-failure.log)；这不是Kernel/native/managed测试失败。修正后的[kernel-post-run-files.json](kernel-post-run-files.json)保留发生时间与唯一当前差异，不篡改runner历史hash；Kernel本体和其余30项当前仍匹配。

[kernel-supplied-files.json](kernel-supplied-files.json)把19份当次供给binary/managed输出归档，oldhelper准确during版本按SHA从旧73进程的raw/build恢复，见[native-build/engine-node-kernel-supplied-files](native-build/engine-node-kernel-supplied-files)。这只说明恢复来源不是当次路径；字节hash确为当次01e版本，不能用后来398d覆盖当次版本。Kernel当前包为新923486版本，旧59-export helper当前包为398d版本，两DLL状态/epoch保持独立，不因上述副本归档而混成一份模块。

完整构建虽通过，保留94条去重原始编译诊断，未声称零warning。历史576源的canonical cpp/Build.ps1/两个runner是core冻结前授权的4项有意变化，详见[intentional-source-changes.json](intentional-source-changes.json)；core599构建前后与其ZIP闭合，当前源后来三安装脚本精确overlay另列，历史576/12/14归档本身未修改。上述归档与完整结果不能自动授予生产采用或游戏验证。

## 历史预跑的原件、路径与哈希

预跑证据根为 `D:/RizToolbox-build-cache/engine-kernel-cases-first-20261025`；使用的实际新DLL为 `D:/RizToolbox-build-cache/engine-kernel-runner-first-20261025/RizToolbox.EngineNodeKernel.dll`，SHA256 `41e01c80fd14f0db2eb8932303db3984f5aee4ea93a8a9e65f422a47a2e687a3`。文件没有复制或改名成JudgeContext。

| 原件（相对预跑根） | SHA256 | 本页实际读取 |
|---|---|---|
| fixture-result.json | `b45595ed6e85de4fac9e6a3910c5a130d4144acbd1a5b57ec4435bc48a0834cd` | parent成功、31项锁定输入、Entry1/native1/managed5、180原调用 |
| held-inputs.json | `92f8fa7f3a137b1620b41e965e84397edca0e5cb2679276b8cef68931e94fc66` | 所有path/hash集合与parent终态相等，前后hash全部一致 |
| entries/fixture-result.json | `8d909b79918143fe2a7139dc279696997f2315dcf8c410e30642c12d1679a7f5` | 1606实际fixture检查、128实际GetEntry、4表、256拒绝、12无效坐标 |
| kernel-entry-proof.json | `7a46bdf69fd2e64249f84179963de56ab9ba08ead9220bd9f1e711403586ddf8` | 1486父级PE/表断言；本页再独立读disk bytes与relocation核对 |
| native/fixture-result.json | `d031a91e46f77fb4de9dc2c94e61790b2f6acb818244b3dd0dbbf751406ef46e` | 3402检查、136实际原调用、124配对 |
| managed/fixture-result.json | `d715bf6ecc673e15370760161b5f5991757816bbafc32bb384ae9f7563648988` | 五mode889检查、44实际原调用、28配对 |

Entry child PID46388、exit0、未超时。native manifest与五个managed mode自己的file列表均明确指向上述Kernel绝对路径，before/after hash与独立PE结果一致。用每个mode四个实际entry分别减去持有Kernel中该kind/slot25的RVA，四项都得到本mode一致的Kernel映像基址；这不只是检查一个自报“使用Kernel”的Boolean。

31项锁定集合覆盖Kernel、entryfixture、旧native/managedfixture、固定Dobby、已供给的managed输出文件、声明源、三个runner与独立verifier等。parent用同一FileShare.Read句柄从hash开始保持到全部child结束，并在finally逐项再hash；该集合与held-inputs逐项相等。系统dotnet、Windows及完整SDK/编译器不属于已归档输入闭包；不能据此声称整机环境已固定。

旧JudgeContext的状态与Kernel分开。当前[统一构建脚本](../../../scripts/Build.ps1)先构建旧helper并照常执行旧native/managed回归，再以另一绝对DLL路径新增Kernel回归；新Kernel拥有独立静态状态，五个managed child又各自独立。预跑没有据旧59-export helper的成功替代Kernel成功，也没有声称两模块共享或迁移状态；预跑时待完成的full旧helper与新Kernel两组，现均在23528实际通过并分别归档，见首节正式结论。

## 静态表与实际descriptor读取

本页直接从持有DLL解析DOS/PE、section、磁盘OptionalHeader.ImageBase和base relocation，不导入作者验证器复用其表结论。实际GetEntry地址先减 `module.base`，得到128个独立RVA；再用**磁盘**preferred base `0x180000000` 加这些RVA组成四个32指针序列，在只读、非写、非执行section中扫描。

| kind | 本次唯一磁盘table RVA | section | 指针/真实DIR64 |
|---|---:|---|---:|
| Start0 | `0x9020` | .rdata | 32 / 32 |
| Execute1 | `0x9120` | .rdata | 32 / 32 |
| Release2 | `0x9220` | .rdata | 32 / 32 |
| Detach3 | `0x9320` | .rdata | 32 / 32 |

四表与实际映射中memcpy出来的slot指针逐项相等；128个slot有独立DIR64，当前样本总DIR64也恰128、无额外项。验证器没有硬编码section名或RVA，也没有预先强制总relocation必为128。实际映射基址为 `0x7ff8105b0000` 对应JSON十进制 `140703403016192`，实际delta `140696960565248`；映射header的 `mappedHeaderImageBase` 等于本次实际基址，不能拿它代替磁盘preferred base。表中preferred+RVA与映射actualBase+RVA的差值按真实relocation关系核对。

512份Fresh GetInfo由初始128、拒绝后256和最终128组成，全部status2/recordABI1/size112、实际kind/slot/entry准确，target/original/attached/Unknown/epoch/活动和错误计数均0。256次Configure让每个已知typed entry分别作为target和original，投到另一个kind的同slot，均在claim前返回1；12个无效坐标核GetEntry0、GetInfo/Configure/Attach各1。无效GetInfo中的A5 poison是未授予输出，不能当真实Fresh记录。EntryFixture没有调用typed entry、原函数或observer，witnessCalls0；这部分只能证明descriptor/table合同，不能取代后续真实调用。

PE独立结果是AMD64、exact8 nonforwarded exports；entry、TLS、delay-import、load-config、CLR目录均0，直接普通导入仅 `Kernel32!RaiseFailFastException` 和 `ntdll!__C_specific_handler`。没有CRT/Dobby/heap/LoadLibrary直接导入。`/NODEFAULTLIB`最终PE与COFF里可能保留的DEFAULTLIB pragma是两个不同层次；本文不把pragma存在说成加载了CRT，也不把两个OS导入扩张成所有传递调用安全。进一步静态范围见[kernel-dependency-review.md](kernel-dependency-review.md)。

## 原生与五种托管mode

旧native fixture实际使用新DLL通过3402检查、136原调用、124enter/exit及124唯一sequence；保留12个未观察调用、17worker、8嵌套、原异常16、观察异常34、free-node6、snapshotFault3和outputFault2等既有矩阵。Execute仍是完整uint32，原函数恰一次/旧call不借新observer等断言没有放宽。Dobby只在fixture中实际Prepare/Commit自有目标，Kernel DLL本身不导入Dobby；这组调用不证明首次游戏发布安全。

| mode | checks | 实际original | captures/Complete/Dispose | ledgerUnknown | Pending/EngineCancelled |
|---|---:|---:|---:|---|---|
| normal | 227 | 9 | 9/9/9 | false | true/false |
| original-seh | 158 | 8 | 4/4/4 | true | true/false |
| original-cpp | 154 | 8 | 4/4/4 | true | true/false |
| detach | 203 | 11 | 7/7/7 | false | true/false |
| observer-fault | 147 | 8 | 4/4/4 | true | true/false |

五份mode结果与managed aggregate逐对象相等；每mode四个kind真实entry属于新Kernel、slot25/epoch分别明确，原生Entered=Exited、inFlight0，dispatcher与nodeledger Outstanding均0。双sink每次captures/peerCapture、Complete/peerComplete、Dispose/peerDispose全部配对，四kind使用同一生产dispatcher/ledger实现并实际回调；这与EntryFixture不执行typed entry的证据分别成立。

normal/detach保留实际同步Start前的node关联、后续同record handle升级和真实free-node后不回读的记录；receiver4096/4097与实际native owner不同，trackerGeneration17/18和EngineGeneration1各自保存。metadata/provider/pairs仍受控，没有执行真实三tracker backend、生产对象backend或IL2CPP/GC，不能把字段名称中production视为生产游戏链已接线。异常的四channel原生Unknown都1；observer-fault只有Detach快照故障让nativeUnknown1，前三kind的原生Unknown0但全部CLR dispatcherUnknown，故障包含层次不能混淆。detach模式保留两次逻辑绑定、四次空观察原调用；根和永久原生槽责任没有因Dispose被释放。

## 旧断言、当前冻结与余项

本页在预跑96386审查时将两runner与旧576源码ZIP逐行diff：仅HelperDirectory由必填改可选、末尾新增HelperPath及绝对路径优先/旧Directory回退分支。之后每行执行与全部既有断言保持不变；参数放末尾保留旧位置调用。新parent只串联既有native136和五managed mode，不构建共享项目；新增entry进程及独立PE表证据另列，没有合成fake PASS。作者对自己的脚本作此检查；当时待完成的完整构建现已由23528实际运行并正式归档，安装三脚本overlay与独立Safety/Recovery验证另计，见本页首节。

full23528运行期间599输入未修改，本页收尾也只改文档。前文预跑96386属于历史先行验证，正式full及23包身份见首节；包只在自有fakegame安装测试中使用，未安装到真实游戏。Kernel还没有替换旧early-fixed依赖链的helper，也未取得系统WinHTTP、真实UnityPlayer首次调用之前的V4凭据、生产coordinator/GC、scheduler/async/future/内联清理或所有消费者退休。完整B01–B19继续；EngineCancelled=false、Pending保留、RequestPlay关闭，游戏实测稍后。
