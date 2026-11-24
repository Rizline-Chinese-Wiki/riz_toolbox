# 音源身份校验调用结构修复

本批已实现显式同步的固定只读校验过程及同一借用图的去重，通过**650项相关检查和4个宿主场景**，六组件Release0警告/0错误，25文件安装匹配。完整真实ClockReady、Speed/Seek、B01–B19与生产准入未完成。`flavor=null`。见[范围](../work/game-seek-20261117/source-verification-r1/scope.md)与[时间线](../work/game-seek-20261117/source-verification-r1/timeline.md)。

[预览包](../artifacts/delivery-20261117/RizToolbox-0.3.0-source-verification-preview-20261117.zip)，37,374,223字节，SHA-256 `ca19dc112db701d6fb739b54cce74c5ba8adab682ebd1751a049bb005e2f5c32`。新包须用新游戏会话复测；上一包的61秒和5分钟只用于定位。

## Evidence

| ID | 原件 / SHA-256 | 事实与限制 |
| --- | --- | --- |
| E01 | [game-result.json](../work/game-seek-20261117/owner-transition-r2/game-r1/game-result.json) · `05607bf019bb31be28518bc899c25d97a2207ccf7147b0417726df1471d57895` | 上一包实际source61.075015秒、Paused→PreparingClock307.269065秒；SDK Pause Error19，无Ready。 |
| E02 | [paused-r1-profile-r2.json](../work/game-seek-20261117/owner-transition-r2/game-r1/paused-r1-profile-r2.json) · `422a8941c5247b6fb7b0215b207db121d243c71913a2bfcc602e990496eacfe1` | 12秒managed线程采样；主要在MemoryFact、VerifyReadable、ReadRanges和FirstInstall验证。不是本新源码的实机验收。 |
| E03 | [source-freeze.json](../work/game-seek-20261117/source-verification-r1/source-freeze.json) · `dc918da965d2c4b38fa53dfcf0b2bc28b6820b0ef6c372206652b34e60951ab2` | 1513输入，相对owner-transition-r2四Adapter文件变化和一新Runtime文件；SDK/native/Examples未改。 |
| E04 | [managed-build-result.json](../work/game-seek-20261117/source-verification-r1/managed-build-result.json) · `e23c750d5939f4ec47fb685ff51a3b3ea86850528c0b3d355212265264094bcb` | 六组件Release0警告/0错误，Hooks210、formal-host3通过；五步自然exit0。 |
| E05 | [result.json](../work/game-seek-20261117/source-verification-r1/checks-runner/checks-r1/result.json) · `e4cbac5f8a6e39b56b1ea08bc37e075eb6718de805a395e37569d7ea8ab591c7` | 新Runtime/Adapter绑定的四进程338检查通过，真实23次Prepare/Commit和3pair；owned范围。 |
| E06 | [scope-checks-r2.json](../work/game-seek-20261117/source-verification-r1/scope-checks-r2.json) · `c02699c108531e3d4f911a01d0d3d508331c4a48f33dcd6496d94b44fb765843` | 实际23成员scope正常/入口helper破坏/投影后helper破坏/投影后PE头破坏，共99检查通过；恢复字节仍sticky。含copy/thread/expired/异常解锁。 |
| E07 | [fixture-correction.json](../work/game-seek-20261117/source-verification-r1/fixture-correction.json) · `4ca1ac5fb7a5fd6b461be30cdc5283e427a2d9734ff623f2575e40a8fbce7742` | r1复制夹具遗漏新mode白名单，进入native前失败；仅改work测试，新r2路径执行，产品未改。 |
| E08 | [host-checks-r1.json](../work/game-seek-20261117/source-verification-r1/host-checks-r1.json) · `dd380f56893d00c4e5277194034ae508ba6e7f4aa986f0224eaf67376805f8bb` | 新链接Loader实际宿主的4个受控场景通过。 |
| E09 | [source-verification-preview-receipt.json](../artifacts/delivery-20261117/source-verification-preview-receipt.json) · `aec8c33809372b0cb153a66fd153a622e9f038feb875135d08475a15e387e7e9` | 40成员/25运行文件完整ZIP读回；新650+4，不借用旧832。 |
| E10 | [install-observation.json](../work/game-seek-20261117/source-verification-r1/install-observation.json) · `b21e2ffcf579542918d8ef405332c1083f1aa56dc31aab777332bc6d4c353db3` | 20:51:43.9303613安装25文件实际SHA匹配；安装器没有启动游戏。 |

本批新数为210+3+338+99=650，另4Host。相机63、旧832、native442及Examples27/发送端7不计入新总数。99范围是实际owned物理组与metadata bootstrap；没有正式Stock foundation、成功fresh票、正式Adoption或Clock正向，不造成功票。

## Finding

| ID | 状态 / 置信度 | 结论 |
| --- | --- | --- |
| F01 | 已定位 / 高 | 旧实机的主要开销在物理页和基础身份验证，非复制；调用树层叠导致一张图从Accessor到Rooted/Receipt反复全组验证（E01/E02）。采样按最近托管caller归属，不能作为精确CPU或调用次数。 |
| F02 | 已实现及定向通过 / 高 | 新typed scope持原双锁、注册this/线程/generation，入口和出口完整Group body；同traversal仍完整验证foundation/target/Dobby/helper/ordinary出口。scope只提供receiver、protocol和已claimed-ticket读取，无通用Action或原生操作授权（E03–E06）。 |
| F03 | 已实现 / 高 | fresh Accessor用同capture完整图与精确binding对象/pause/七句柄引用身份；Rooted同一同步graph末不再递归验证同capture。保留run关联、class/field/active/clip/GetTime、ticket/epoch和实际消费者独立fresh入口（E03/E04）。正式stock路径仍待新实机。 |
| F04 | 未验证 / 高 | 650+4不证明新包实时响应、ClockReady、完整Speed/Seek或生产准入；新源码不能借旧游戏验收（E01/E09/E10）。 |

## Path

**P01（E01/E02 → F01）：** 实际阻塞线程栈与12秒采样 → 源码确认Capture/Actual/Rooted/Readback重复进入Group → 改调用结构，不继续小数组优化。原始trace保留；微软工具用法见[dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)。

**P02（E03/E04/E06 → F02）：** 显式SourceVerificationScope创建 → 完整物理入口 → 固定只读图/元数据/ticket查询 → 完整物理出口与所有父凭据出口 → 释放锁。没有跨调用、跨线程或ambient缓存。入口失败、出口被破坏或issuer改变仍sticky撤回；wrong receiver本身不污染健康组。实现[Runtime scope](../src/RizToolbox.Runtime/Hooks/ExactHookBroker.Playback.SourceVerification.cs)。

**P03（E03/E04/E05 → F03）：** Capture一次scope内两图与claimed native snapshot → Accessor只验证同对象projection，不再次经ReadActive递归同图 → Rooted保留独立run和注册。所有DSP/media采样、AudioSource写入、真正original/Clock消费在scope外，继续独立fresh检查。实现[Capture](../src/RizToolbox.Adapter/Gameplay/NativePlaybackFreshSourceCapture.cs)、[Adoption](../src/RizToolbox.Adapter/Gameplay/NativePlaybackFreshSourceCapture.Adoption.cs)、[Actual](../src/RizToolbox.Adapter/Gameplay/NativePlaybackActualSource.cs)、[Rooted](../src/RizToolbox.Adapter/Gameplay/NativePlaybackStockRootedSource.cs)。

复核命令与实际子进程退出码在E04/E05/E06/E08。原r1 fixture失败保留，不覆盖；产品字节未因测试模式修正而改变。下一只续新安装的正式Steam会话，观察source响应及SDK Pause→独立Paused→Resume/ClockReady/SpeedSeek。

## 本包实机闭合

**E11 → F05 → P04：** [game-result.json](../work/game-seek-20261117/source-verification-r1/game-r1/game-result.json)记录新PID2748/Start20:54:03.3819851，25文件安装匹配，source **14.8488秒**（上一包61.075015秒）。单SDK Pause nonce6bef360b…success且immediate true，sender至回执18.884785秒，0.227853秒后独立native生命周期同run Paused；没有Resume/Speed/Seek请求。

**E12 → F06 → P05：** [完整新日志](../work/game-seek-20261117/source-verification-r1/ready-evidence-r1-toolbox.log)在13:08:06.9729770Z首次记录 **ReadyExperimental**，距Pause回执498.455726秒。这是实际host完成当前准备链的阶段证据；不等于SDK时钟读出、速率修改、Seek、可闻同步或生产验收。查询心跳仍停在12:59:31Z，分钟级阻塞未闭合。

**E13 → F07 → P06：** [Ready后实际栈](../work/game-seek-20261117/source-verification-r1/game-r1/ready-query-r1-stack.log)确认ObserveControlsAfterMaintenance→GameplayNativeBackend.CaptureSourceDescriptor→Session.CaptureRateSourceDescriptor→VerifyControlledProvider→Publication.IsCurrent→VerifySource→整组/基础凭据。只读取描述也错误地开启完整音频事务，是下一四Adapter文件的具体修复对象。新源码不能借此旧包Ready实测验收。

**E14 → F08 → P07：** [退出记录](../work/game-seek-20261117/source-verification-r1/game-r1/game-exit.json)保留正常sky AltF4请求后至少83.7003163秒仍无响应，21:13:42按PID/路径/Start三核结束，sky窗口消失；当前无游戏。案例所有未执行的value/readout测试保持未验证。
