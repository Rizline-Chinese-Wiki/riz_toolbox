# 自有映像最早 TLS 与入口的补丁窗口观察

2026-10-27。实际冷启动 r3 已通过：PID **20296**、退出码 0、子进程 **287 项检查 / 0 失败**，父进程输入哈希前后不变。自有目标 DLL 的两个 TLS callback 和 DLL Entry 在 `wmain` 前由加载器实际调用，分别留下前三条机器采样；随后 Host 显式调用自有 `UnityMain2`，留下第四条。每条采样的 36 个完整源窗口、共 380 字节，都与同 PID 的 V5 发布记录中的实际补丁及读回字节一致。

**这只证明该自有映像四个观察位置所见的源字节已经是提交后的补丁。它不授权生产游戏的首次执行窗口，不证明任意执行器没有抢先运行，不开放生产 admission。** 本轮没有执行原游戏函数、安装游戏、CLR 家族附着或取消完成。观察 DLL 保留的十三段游戏函数体只供发布器核验、提交源字节；由加载器与 Host 调用的代码全部为自有短桩和采样器。

本报告基于已有父子原件、三个 raw 文件、候选磁盘 PE 和归档进行只读复核；编写报告时没有重建或运行目标。沿用[逐记录退休范围](../foundation-engine-retirement-20261026/scope.md)中的本地授权和游戏实测暂缓限制，本批范围是独立 owned 启动时序取证，不扩大到生产许可。

## 实际原件与四条记录

最终原件为 [cold r3 父结果](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/test-result.json)及 [Host 子结果](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/child-result.json)。父记录 `passed=true / inputHashesUnchanged=true / timedOut=false / killed=false`。运行线程 **47488** 同时等于实际 V5 的 `producerThread` 和 `mainThread`，PID 字段为 20296。

| 顺序 / commit | callback ID | 实际位置 | reason | TID | 栈中实际返回 PC |
|---:|---:|---|---:|---:|---|
| 1 / 1 | 0 | TLS callback 0 | 1，ProcessAttach | 47488 | `0x7ffd00285c7b` |
| 2 / 2 | 1 | TLS callback 1 | 1，ProcessAttach | 47488 | `0x7ffd00285c7b` |
| 3 / 3 | 2 | DLL Entry | 1，ProcessAttach | 47488 | `0x7ffd002bef6a` |
| 4 / 4 | 3 | Host 调用自有 UnityMain2 | `0xffffffff`，显式导出标识 | 47488 | `0x7ff6036c2865` |

实际目标基址为 `0x7ffba0e30000`。返回 PC 来自进入短桩时的 `[rsp]`，线程号来自 `GS:[0x48]`；没有从 Host 回填这些值。前三条返回位置属于加载器调用路径的实际可执行映像地址，第四条来自 Host 的实际调用返回位置；本报告没有独立符号化这些 PC，也不把地址猜测写成精确函数名。

三个实际文件分别是：

| 文件 | 字节数 | 独立复核 |
|---|---:|---|
| [publication.raw](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/publication.raw) | 91280 | ABI5 / Ready / owned Profile1、PID 与实际 TID、36 提交窗口 |
| [observations-initial.raw](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/observations-initial.raw) | 9792 | 三条已提交记录，剩余五槽全零 |
| [observations-final.raw](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/observations-final.raw) | 9792 | 四条已提交记录，剩余四槽全零；前三条没有变化 |

报告再次逐字节核对 initial 的 3×36 窗口和 final 的 4×36 窗口，共 252 次比较，涵盖四条唯一记录的 144 个窗口样本；这不是 252 次独立执行。每个样本只比较其声明的完整物理窗口长度，与 V5 `patchBytes` 和 `observedBytes` 同时相等；32 字节槽尾部、记录保留字段、未使用记录均为零。四条记录的 `payloadBytes` 都为 380，`sequence==commitSequence`，header 的 `claimed==committed` 且 overflow 为零。Host 的 GetInfo→Verify→GetInfo 及导出调用后的回读要求完整 V5 记录不变。

## 机器采样器和文件身份

[独立生成器](../../../scripts/generate-engine-transport-owned-observed-image.py)只读固定 SHA 的原 UnityPlayer 文件并生成新的 owned DLL；没有修改旧 owned 生成器、旧严格 profile 验证器、原生发布器或生产开关。新[观察 profile 验证器](../../../scripts/generate-engine-transport-owned-observed-profile.py)为该明确变体独立验证磁盘事实。

每个自有入口为 11 字节：设置固定 callback ID 后直接相对跳转到采样器。采样器不使用栈帧、不 CALL、不调用导入函数、不依赖 CRT/CLR/动态 Load，也不查询或存储 Ready。它用原子 `xadd` 取得正序号，逐窗口直接复制当前映像源字节，最后写 commit。只记录 DLL ProcessAttach；`UnityMain2` 使用单独导出标识。读取字节发生在这些自有入口内部最早的一小段指令中，不能泛称“在 TLS callback 之前采样”。

本次 sampler 位于 RVA `[0x1a490,0x1a9f7)`，1383 字节，磁盘 SHA256 为 `08a0395c6d7fc1c90a4bef52f52894cf2be448ace8515882a37e86b7e3182859`。静态 `.pdata` ordinal 为 24，展开 RVA 为 `0x1e341c8`，真实 leaf 元数据为 `01 00 00 00`。四个短桩也归属于明确的未选中静态 leaf RF；Entry 和 UnityMain2 可以归属于同一原 RF 范围，不虚构两个独立表项。

Host 运行时实际调用 `RtlLookupFunctionEntry`，要求返回的 imageBase、表项地址、ordinal、begin/end/unwind 全等于独立生成头中的事实；还要求代码区域保持 RX、展开区域保持 R，比较四个短桩和 sampler 的完整机器字节。报告的独立磁盘复核确认全 93760 个 RF 严格排序且不重叠、短桩字节和 leaf 完整一致、sampler 的精确表项与完整 SHA 一致。报告没有在目标结束后重新执行 Windows 的动态 RF 查询，运行时查询依据 Host 的实际检查结果。

十三个完整源函数体、二十七个源 RF/完整展开数据、只读 owned marker 均保留；只有已列明的非选中 owned stub/采样器 RF 被修改。仍为原八节布局、两个导出和四个普通 WinHTTP 导入，不新增第五模块以外的自定义依赖。采样器的转换全为直接 JMP，观察目标的 CFG/load-config 明确关闭且独立验证；没有在真实游戏映像上调整 CFG。旧严格 owned validator 的原始短桩字节规则仍应拒绝该 11 字节观察变体，本报告未为此另运行一次旧验证器。

## r1、r2 失败与精确 COW 存储规则

| 版本 | 实际 PID / 退出码 | 实际观察 | 处理与边界 |
|---|---|---|---|
| cold r1 | 60412 / 4 | 首次 CopyObservation 被拒；未产生子 JSON | 原件保留。该次缺少 region 诊断，不能倒填为已测得某一保护值 |
| cold r2 | 64112 / 4 | 完整 RPM 9792 字节成功；三条记录已提交，但严格 PAGE4 条件失败 | 诊断确认同一分配基址的已写页为 4、未写零页为 8；没有冒充通过 |
| cold r3 | 20296 / 0 | 观察区精确 section 身份及 4/8 分区通过，四记录和完整源 payload 验证通过 | 仅专用观察存储规则修正；RX 代码/R 展开要求不变 |

实际观察存储 RVA `0x1c04000`，为 `.data` RVA `0x1c03000 + 0x1000`，总长 9792 字节，完全在该节的 virtual/raw-backed 范围内。r2 的 [child-result.json](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r2-20261027/child-result.json)记录：首 4096 字节为 `MEM_IMAGE / MEM_COMMIT / PAGE_READWRITE(4)`，后续区域为相同 allocationBase 的 `MEM_IMAGE / MEM_COMMIT / PAGE_WRITECOPY(8)`。该次采样 header 为 claimed 3、committed 3、overflow 0，callback 0/1/2 的 seq=commit 1/2/3、真实 TID 38508，因此拒绝并非“TLS 没有执行”。

r3 的 [stderr 原件](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/stderr.log)再次记录相同现象：initial 前一页为 4，剩余为 8；第四条记录写入后，前两页变为 4，剩余为 8。前三条记录的总长度是 `64+3×1216=3712`，第四条扩展到 4928 字节，这与实际被写页面数一致。这里是 SEC_IMAGE 写时复制数据页的实际观察，没有用它改变 source patch 或代码页保护。

新的专用 `ObservationStorage` 规则先验证实际只读 PE header、AMD64/八节/optional header、唯一 `.data` 的原 flags 为读写且非执行、固定 RVA 偏移和精确 9792 字节全在原 raw-backed 范围。随后每个实际区段必须保持同一目标 allocationBase、`MEM_COMMIT`、`MEM_IMAGE`，保护值恰为 4 或 8，其他值均拒绝。通用 `Memory`、RX sampler/short-stub、R unwind 校验仍只接受原来的精确值。`strictMemoryAccepted=true` 表示这一完整、受限的存储规则通过，不表示将 RPM 能读到任意地址作为成功。

失败诊断保留实际 region、保护、类型、地址和 copiedBytes；初始 raw 即使后来检查失败也持久化。r2 的 publication/final 零占位文件明确标为 `publicationRawValid=false / finalObservationSampled=false`，不能用于宣称 Ready 或导出采样成功。

## 父层、归档与剩余覆盖

父脚本独立解析三个 raw，核实际 PID、TID、顺序、reason、sourceBase、36 窗口几何与完整补丁/读回 payload、保留区和未用槽全零，并核最终前三记录未改。Host 额外实际核代码页、完整机器码、RF 运行时返回位置和返回 PC 的可执行映像属性。父层没有独立重新执行 live `VirtualQuery/RtlLookupFunctionEntry`；相关 live 结果来自该次 Host 检查和 stderr。采样数据的 9792 字节原件使父层可以重建字节证据，而不是只信 passed 布尔。

报告独立核对：42 个 cold runner 输入、30 个候选构建输入、40 个候选文件的当前 SHA 均匹配 manifest；候选 ZIP 的 30 项解压内容 SHA 多集与 30 项输入完全相等，归档 SHA 也一致。构建父脚本持有其输入读锁并复核构建前后哈希，运行父脚本对候选文件持读锁并复核运行后哈希。原始游戏文件没有被打包进该 30 项候选归档，其精确身份由独立目标生成 manifest 记录；因此不宣称无需固定原输入就能换机完整复现。

本场景为一个实际冷启动、一个主线程，且没有注入 TLS 失败、观察 overflow、并发 Entry/Export、源窗口被篡改或异常展开。leaf RF 的边界/映射身份有实际检查，但没有本次 exception unwind/recovery 执行证据。该 Host 未启动 CLR，`fullFamilyAdoptionVerified=false`；前批全 12-family CLR 首次采用是另外一组证据，不能合并成一次相同进程的验证。未执行原游戏代码、整局、取消或所有消费者关闭；`ProductionFirstInstallAuthorized=false` 保持。

E-001 为 r1 首读失败父原件，E-002 为 r2 region 与真实初始 raw，E-003 为 r3 父子结果与三个 raw，E-004 为 r3 候选 PE、固定头和输入归档。F-001（validated，high confidence）由 E-003/E-004 支持调用路径“owned TLS0→TLS1→Entry→wmain→自有导出”在采样位置已见 36 处实际提交字节。F-002（validated，high confidence）由 E-002/E-003 支持只对原 RW 映像观察区接受精确 4/8 分区的修正。两条结论均不产生生产首次执行安全许可。

## 精确 SHA256

| 原件 | SHA256 |
|---|---|
| [r1 父失败结果](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r1-20261027/test-result.json) | `fe2441ec0bc199235e5d38e8922e7d6477d033c805c8e2dddaea278c39659514` |
| [r2 父失败结果](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r2-20261027/test-result.json) | `9d876858e5ade8bad459b1e49a83ed4cea4046b41ea5386986fcf4222e63a374` |
| [r2 子诊断](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r2-20261027/child-result.json) | `ab7fadc4cf4a323d4375143cff6ee492f3c4a0f0138b5c49567bc579ec6c7c7c` |
| [r3 父通过结果](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/test-result.json) | `707b026aeecbe226cb981dbfd1851376c083b6e937ce3c08dec33a60932b623c` |
| [r3 子通过结果](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/child-result.json) | `f075a24177f8496fe28da74e0c32bc09a1404df2d27e4549c35fe65aaca5cdc1` |
| [V5 raw](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/publication.raw) | `8b8b30cdfc2a192d513b1b6f688ac643c279d0311d2709897a1dc851469ac3b6` |
| [初始 raw](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/observations-initial.raw) | `9331dc58c75e4c37c8afbbd2b41ea11917e060361d54ccc0dae458eab7dc1e7d` |
| [最终 raw](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/observations-final.raw) | `14bba37a6f8e3f8d07586b7ecb27bbc7d1387c9a848f62c02bc4b3fa8b77a20e` |
| [实际 region stderr](D:/riz-build-cache-next/engine-transport-owned-observed-cold-r3-20261027/stderr.log) | `aa228f5203b36f1d690bcf322595310a20e9810e7376cef630d7296a83dcc119` |
| [观察目标生成 manifest](D:/riz-build-cache-next/engine-transport-owned-observed-target-r1-20261027/owned-observed-target-image.json) | `27695aa09d1b56cb4427d32b611df00b626ed203d595cb5c522ce901b096729e` |
| [候选 build manifest](D:/riz-build-cache-next/engine-transport-owned-observed-candidate-r3-20261027/observed-owned-candidate-build.json) | `70394b6ba6f3fd349daeadbdb83e4fbb6b9316a3dbeabd117fd4cf279a58efa4` |
| [候选输入 ZIP，30 项](D:/riz-build-cache-next/engine-transport-owned-observed-candidate-r3-20261027/owned-candidate-inputs.zip) | `452a95f485ad13fc11724e4f2cf25df264725a6d965f9c29f461c51f033a60ec` |
| [观察固定头](D:/riz-build-cache-next/engine-transport-owned-observed-candidate-r3-20261027/EngineTransportObservation.Generated.h) | `1fb3976a21acf93093ec03e2142a42759f2c621cf2353793335379ba2aab3a41` |
| [发布器编译 profile 头](D:/riz-build-cache-next/engine-transport-owned-observed-candidate-r3-20261027/EngineTransportProfile.Generated.h) | `a9630eae1f5a3d35428b2fc01522fce0d42197410ff5e29872046d732eb7c973` |
| [观察 profile JSON](D:/riz-build-cache-next/engine-transport-owned-observed-candidate-r3-20261027/native-profile.json) | `9eb5529c41b7ae6d0df72746f398d9202ee2c1be8757b50776d5f21108b88f70` |
| [实际目标 DLL](D:/riz-build-cache-next/engine-transport-owned-observed-candidate-r3-20261027/RizToolbox.TransportOwnedTarget.dll) | `0936525a9101566357d32db3901f327c21acfbb680d8262b335f9effdeee5f50` |
| [实际 Bootstrap DLL](D:/riz-build-cache-next/engine-transport-owned-observed-candidate-r3-20261027/RizToolbox.EngineBootstrap.dll) | `8f10c44559ae014f7869d0106baf5646df03bb7725e5255fab9d470b8673681e` |
| [实际 Kernel DLL](D:/riz-build-cache-next/engine-transport-owned-observed-candidate-r3-20261027/RizToolbox.EngineNodeKernel.dll) | `1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a` |
| [实际 owned winhttp DLL](D:/riz-build-cache-next/engine-transport-owned-observed-candidate-r3-20261027/winhttp.dll) | `3a6443af5e62a489704cebccb522997c3f3f805dca34a07e71d554eb515acb70` |
| [实际 Host EXE](D:/riz-build-cache-next/engine-transport-owned-observed-candidate-r3-20261027/RizTransportOwnedHost.exe) | `6386bf6a38e8f5bfd8d299fb45ac9bd61b5585165c75507a7cb84dba4f89e631` |
| [观察生成器源码](../../../scripts/generate-engine-transport-owned-observed-image.py) | `8d2554d5d71ce00adc8f36f52ca31facac629ccf2710228e14eff1c71fa6b25b` |
| [观察 profile 验证器源码](../../../scripts/generate-engine-transport-owned-observed-profile.py) | `143d6351a927713c3d60008024d5c7d714605f3d5f1321a084ba790bf3aba4b8` |
| [Host 源码](../../../native/RizToolbox.EngineTransportObservedColdFixture/Host.cpp) | `1a57cec6b80d227bb5302583d2f81de048b1b9e0d678be249974c1cca5625725` |
| [Build 脚本](../../../scripts/Build-EngineTransportObservedOwnedCandidate.ps1) | `8d1601d8e41777dc608a2c8d516e8320586bdfa97f0562d1e01acd12697cd4e0` |
| [Test 脚本](../../../scripts/Test-EngineTransportObservedOwnedCold.ps1) | `d8c695800fe0bc91d372fab28fd739d13d23d2b1a057007869643ae95ebb6c1d` |

## 复现入口

这些是已存在脚本的完整参数形式，报告阶段没有执行。换机需固定原始 UnityPlayer 输入、对应 owned Doorstop/Kernel、Python 依赖和 MSVC 环境；输出目录必须不存在。

```powershell
Set-Location C:/Users/71957/Desktop/riz_toolbox
C:/Python314/python.exe -B scripts/generate-engine-transport-owned-observed-image.py `
  --image D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll `
  --facts D:/riz-build-cache-next/engine-transport-production-contract-r1-20261026/production-transport-facts.json `
  --output-directory D:/riz-build-cache-next/observed-target-repro
pwsh -File scripts/Build-EngineTransportObservedOwnedCandidate.ps1 `
  -TargetDirectory D:/riz-build-cache-next/observed-target-repro `
  -DoorstopDirectory D:/riz-build-cache-next/engine-transport-doorstop-owned-r1-20261027 `
  -OutputDirectory D:/riz-build-cache-next/observed-candidate-repro
pwsh -File scripts/Test-EngineTransportObservedOwnedCold.ps1 `
  -CandidateDirectory D:/riz-build-cache-next/observed-candidate-repro `
  -OutputDirectory D:/riz-build-cache-next/observed-cold-repro
```
