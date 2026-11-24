# 底层返回 ABI 与协程身份关联报告

2026-10-25。普通兼容底层工程，flavor=null，overlay=null。作者实现身份关联账本、三tracker受控测试和owner API回归，并参与上批CLR通道；本文不是外部独立审计。

## 概述

本批纠正了实际会影响调度语义的返回宽度：固定 UnityPlayer scheduler比较完整EAX，Execute通道现在返回uint32，Start/Release继续保留原AL，Detach仍为void。新增返回ABI2检查拒绝旧helper，Frame112/Completion32/Info112的记录布局保持Version1。另已实现managed receiver↔native owner的受root元数据投影，以及把三类tracker身份和实际node代次分开保存的标量关联账本。

root观察修正后的完整构建session `51261`实际exit0，25套 1617/1617；565项声明源码与22项暂存包逐项归档匹配。本批另在首轮成功完整构建后发现foreign遮蔽可漏记owned责任，保留修复前session `48324`的独立归档，修正代码/测试后重新全构建；旧成功结果不当最终候选。新native fixture 3402检查，五进程真实Dobby→CLR→实际账本联合 889检查。真实联合中的tracker provider与receiver/native-owner配对仍受控；actual三tracker backend路径和owner API合成元数据测试是另外的受控证据。

新包未安装。本批未签发固定UnityPlayer四入口安全首发V4、未将它们接入生产coordinator，未证明真实游戏scheduler、metadata/GC或取消完成；`EngineCancelled=false`、Pending与根退休限制继续保留。完整B01–B19目标仍未完成，不能以测试数量代替验收。

## 范围与依赖

范围见 [scope.md](validation/foundation-engine-binding-20261025/scope.md)，沿用用户“先继续代码，实测稍后”。本批包括本地实现、root唯一构建、自有native/CLR进程和固定磁盘机器码研究。没有游戏/UI操作、安装、启动/终止游戏、游戏文件修改或commit/push。报告生成器只读现有JSON、日志、源码与ZIP，写本文；缺完整终态或归档会直接失败。

复现依赖Windows x64、MSVC2019 14.29.30133、本机.NET8测试与.NET6生产目标、既有BepInEx缓存；Dobby固定SHA256 `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded`。报告用`C:/Python314/python.exe`标准库；只读研究使用pefile2024.8.26和Capstone5.0.6。当前没有可调用IDA MCP，研究采用本机有据工具解析完整选定RF/CHAININFO；没有从工具名称推定已完成IDA分析。源码ZIP不含游戏、全部依赖或工具链，不宣称换机干净复现或完整B19发布矩阵。

## 静态证据为何改变本批实现

### Execute实际消费的是完整EAX

固定UnityPlayer SHA256 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`。scheduler pump `[447D60,447FAF)`在`447EEA`执行`41FFD7`调用callback，`447EF2`的`83F801`是`cmp eax,1`；callback `[78F5E0,78F673)`尾跳Execute `[78F840,78FA2E)`。因此只有AL不能还原调用者实际检查的返回值。详情与原始指令见 [engine-installation-research.md](validation/foundation-engine-binding-20261025/engine-installation-research.md)、[research/scheduler-consumption.json](validation/foundation-engine-binding-20261025/research/scheduler-consumption.json)。

| 入口 | 固定RVA | 本批实际返回合同 | 不授予的含义 |
| --- | --- | --- | --- |
| Start | `792240` | `uint8(owner,enum,nameArg,node** out)`，保留原AL | 正常返回不等于managed Coroutine handle已回传或node一直存活 |
| Execute | `78F840` | `uint32(node,byte* out)`，保留完整EAX | uint32不是bool，不从AL重建；不宣称全RAX透明 |
| Release | `78F680` | `uint8(node)`，保留原AL | AL=0只记录该代次最后清理事实，不直接确认取消 |
| Detach | `793840` | `void(node)` | 摘链后可能仍有实际执行/continuation责任 |

上一批1597/1597的冻结报告和3191项native原件只证明当时实际低byte样本；不能扩展成完整Execute返回ABI证明。它们保持原样，本批以新的静态消费者证据和全宽动态样本修正实现。Completion的`RawReturn`本来就是uint32，不改32字节布局；历史命名`ReturnByteValid`的bit8现在指该入口返回标量有效，Execute允许全部uint32值，Start/Release限制byte。helper追加`RizEngineNodeContext_GetAbiVersion=2`，当前导出共59项；新bridge采用前强制核2，旧helper缺export会被拒绝。

### 当前pre-init不足以授UnityPlayer首次写权限

研究核EXE `Rizline.exe` SHA256 `f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e`的静态IAT：leaf1000经EXE+E240调用UnityPlayer!UnityMain2；UnityMain与UnityMain2实际同地址`808690`，不能当两份独立启动门。沿完整选定RF追至LoadLibraryW/GameAssembly和GetProcAddress(il2cpp_init)，当前Doorstop只在之后的init wrapper进入RizBeforeIl2CppInit。

更早已有UnityPlayer CRT attach，实际C initializer非空8项、C++ initializer非空3006项；两个直接TLS在process attach的EDX=1分支返回，只关闭这两条直接分支，不能替代CRT/间接行为与早期线程历史。研究既没证明四入口已被调用，也没证明从未调用，结论仍Unknown。17条已验证直接call/jmp来源比一个managed wrapper更广，扫描未覆盖全部间接调用、地址取值和动态缓存。这个缺口使当前GameAssembly pre-init、既有V3的27写/18行以及两阶段Prepare/Commit都不能直接授权四个UnityPlayer入口首次安装。

完整有限合同见 [scheduler-cleanup-research.md](validation/foundation-engine-binding-20261025/scheduler-cleanup-research.md)、[research/scheduler-contract.json](validation/foundation-engine-binding-20261025/research/scheduler-contract.json)，后者对前三份原件重新断言48个精确指令点。实际tree登记入口`44D630`接收tree、输出槽与源record，返回同一输出槽地址；`447AD0`在erase/recycle后调用缓存cleanup，`447C30`仅删除，pump在删除后才调用callback，因此“tree已空”不能替代“已消费callback排空”。整体drain `447750`还直接erase并逐条cleanup，绕过上述两个删除外层。普通、repeat重登和整体销毁责任必须分别接线。

Start还有不经过Release外层的同步最终清理：`7924CD`入口到`79257D/79259D`出口直接完成清理和null输出。选定机器码已给出内部观察候选，实际内部寄存器/flags、重放、unwind及安全发布尚未实现，不能凭外层Start的AL/null输出猜分支。异步yield另将callback/cleanup/payload/owner ID写入对象`+10/+18/+20/+28`，不经过tree；Detach清owner ID也不等于撤回已缓存派发。`5DA510`有同字段形状清理，但容器与yield对象生产来源及普通异步派发仍未闭合，不能宣称所有异步对象已关联。这些有限静态事实尚未实现完整runtime观察合同，JSON保持所有整体取消、来源/消费闭包及生产首发边界false。

## 实现和实际测试各自证明什么

### receiver root到native owner的合法观察边界

`NativeRuntime.ReadUnityCoroutineOwner`借用已保留RootHandle，先核精确managed receiver和actual MonoBehaviour类链；从正式UnityEngine.Object声明读取`m_CachedPtr`元数据，拒绝非Object声明、static、非0x10偏移、byref、非8字节System.IntPtr存储。经正式field-get两次读取，中间及末尾复核root目标、receiver类和cached owner未变；owner=0拒绝。它不额外创建/释放root，不调用游戏方法，不解引用返回native owner或node。managed receiver本身不能作为native owner。

7项新Runtime回归运行真实方法，但official export和metadata由测试提供，receiver头为真实可读unmanaged buffer；native owner是故意不可解引用的高位哨兵。测试覆盖两次实际field-get、元数据错误、非Mono、root/receiver class/owner变化、null/disposed/mismatched root和线程拒绝；所有export handler避免将断言异常穿过P/Invoke。它证明实际方法的受控边界，不证明真实游戏Object元数据、销毁/GC竞争或实际owner安全读取已实测。

### 既有三类tracker身份与node代次的关联

新`NativeEngineCoroutineIdentityLedger`独占包装一个全新`NativeEngineCoroutineNodeLedger`；接受外部已验证且互不相等的一对一managed receiver/native owner标量配对，以及三tracker既有`ReadCancellationIdentities/IsCurrentCancellationIdentity` provider。pair值本身不是生产Ready/首发凭据，验证来源仍由独立运行时边界负责。

Start先登记责任，再按真实Arg0 owner→receiver与Arg1 Enumerator读取唯一current published tracker record，复制Owner/Slot/TrackerGeneration。同步首Execute由actual node ledger取得node及独立EngineGeneration；最近foreign Start会遮蔽外层，不能借外层tracker身份。managed wrapper回传handle晚于native Start，故显式`RefreshTrackedIdentities()`只允许同Owner/Receiver/Slot/TrackerGeneration/Enumerator的单调handle升级，native node地址不是Coroutine handle。

节点最后清理、执行/OpenCalls drain与旧Start完成/Dispose之后才允许新engine代次；tracker代次仍单独保留。活地址表在相关Dispose后剔除完全清理/排空的历史代次，历史binding和快照留存；同地址稍后被foreign owner复用不会污染旧run。foreign非owned全局调用不读providers、不消耗2048个持久owned记录。另有统一2048层active物理遮蔽上限：超限保永久untracked义务并阻止任何后续capture借留存外层，已有exact上下文仍可依次unwind。provider故障/重入、owned未知enum/node、歧义重复、错frame/thread/parent、异常和容量故障保Unknown/Pending；不签发EngineCancelled、不清Pending、不释放root。

13项Audio身份用例调用真实两个账本。其中3项进一步调用实际初始化、terminal、cutout tracker实现，在受控backend里走发布Enumerator→同步native-frame观察→实际managed Start结束→scalar handle刷新路径，核新观察没有增加backend对象读取、activity读取或root数量。其余覆盖独立代次、ABA、foreign遮蔽/旧地址复用、immutable输入/快照、单调handle、异常/配对、worker/provider重入及两种容量边界。它们包含actual tracker/backend接口流程，但backend不是生产IL2CPP backend，也不代表真实native detour。

首轮完整构建后只读交叉复核发现过滤分支漏记责任：最近physical Start属于foreign时，尚未known的node即使Before.Owner等于已验证owned NativeOwner，也因`nearest.Relevant=false`被当成不相关调用忽略。旧Foreign受控用例还断言了这个较弱行为，所以其成功不证明责任不丢失。Native Start给自己分配node写入Arg0 owner，只能证明其直接首Execute归属，不能关闭同步托管回调、跨node nested执行和所有间接路径；研究也明确这些尚未全闭合，因此不能将该情形推测排除。

合法transport Frame反例为`foreign Start(B) → Execute(此前未知node, Owner=A)`，其中A是本run已验证owner；旧过滤在没有外层owned记录时可留下零owned记录且Pending/Unknown均false。root先观察首轮session `48324`真实终态0，再将其1617/1617结果、565源码和22包独立保存在before-mask-fix，明确`finalCandidate=false`，随后授权最小修正。最终Capture直接按IsRelevantFrame登记owned Call；若是未known node且最近Start为foreign，在任何node ledger/provider调用前转Unknown并保留该条标量scope。它继续不借外层、不读provider，exact Complete/Dispose仍排空自己的本地责任；Pending/Unknown保持。

修正后的同一个Foreign测试增加“有/无外层owned Start”两种反例，核Relevant、Call数、Unknown/Pending、NodeCall/Identity为空、零额外provider/节点绑定，以及精确清理后outstanding=0；纯foreign和完全清理旧地址被foreign复用仍不污染。用例总数因此仍13，完整suite总数也可能不变，源码语义和最终构建身份才区分修前修后。

root还实际运行了独立隔离replay：从修前源码ZIP提取旧账本全文，只把类名改为`BeforeMaskIdentityLedger`；当前强化测试也仅改该类名。独立net8测试程序使用现有IVT程序集名与四个Release DLL直接Reference，不重建共享项目，只执行包含nearest foreign Start的Foreign一例。编译exit0后，实际测试在`Check → Foreign`抛出`InvalidOperationException: Engine tracker identity check failed.`，程序才记录`redObserved=true`并exit0；编译、加载或其他运行失败均不算red。原始编译/运行日志、派生源码、共享DLL及输入hash保留在 [mask-before-replay/20261025T034151Z-6b979b54/result.json](validation/foundation-engine-binding-20261025/mask-before-replay/20261025T034151Z-6b979b54/result.json) 所在目录。生成器核派生账本/测试确实分别只改类名，核日志与输入仍匹配；这给出旧实现不能通过强化断言的实际反例，修后Foreign则由最终完整构建通过。两组都是受控Frame边界，真实native联合样本未触发该foreign场景，不能宣称游戏中已复现。生成器另外核修前ZIP与最终输入仅这两文件不同，并要求最终session与修前不同。

### 真实native/CLR联合链与受控provider的区别

独立native fixture在四个自有真实目标执行固定Dobby Prepare/Configure/Commit，3402检查、136次original、124组Enter/Exit；12次无观察pass-through。新增8个全宽Execute样本、10份全宽completion，覆盖高位、worker/observer fault和free-node-after-original边界；返回EAX已实际验证，全RAX和异常flags透明性仍未证明。原异常/观察异常/worker分别16/34/17，原函数释放节点6类。

五进程CLR联合共889检查，44次实际original、28个主账本capture；经真实helper/bridge/dispatcher进入实际node ledger，同时另建独立node ledger供实际identity ledger包装。每个identity work的provider DTO和owner pair仍由fixture提供，`actualTrackerBackendExecuted=false`；没有在这条实际native链中调用前述真实三trackerbackend，也没有执行owner API。不能把这些分开通过的证据拼成真实游戏端到端链。

| 模式 | 检查 | 实际original | 主capture | 独立身份链数 | 证明范围 |
| --- | --- | --- | --- | --- | --- |
| `normal` | 227 | 9 | 9 | 2 | 实际u32高位返回；同步Execute/Release与独立身份链；延后同record handle升级 |
| `original-seh` | 158 | 8 | 4 | 1 | 实际四签名SEH由native接住；不造返回或handle确认 |
| `original-cpp` | 154 | 8 | 4 | 1 | 实际四签名C++原异常；native typed catch；保Unknown |
| `detach` | 203 | 11 | 7 | 2 | 物理capture后逻辑解绑仍双sink完成/清理；真实身份链延后升级 |
| `observer-fault` | 147 | 8 | 4 | 1 | peer观察故障与不可读Before；原调用继续，身份链排空但保Unknown |

normal实际Execute返回包括`0xabcdef01`，nested为`0xa5c3fe81`；不能用其AL低byte替代完整EAX。SEH/C++没有正常Execute返回，因此这两模式的uint32验证位为false，整体全宽验证来自有正常结果的模式。所有已捕获双sink Complete/Dispose数各自相等，native/dispatcher inFlight和节点outstanding归零。每条新增身份链outstanding也归零，Pending=true/EngineCancelled=false。

normal和detach的实际身份记录如下。native owner与managed receiver不同，tracker代次17/18由测试标量provider提供；每个work是独立identity ledger，因此其EngineGeneration可同为1，不能将跨ledger的1当全局同一个node代次或推定同一ledger ABA。

| 模式/独立work | managed receiver | native owner | node | tracker代次 / engine代次 | 延后确认handle |
| --- | --- | --- | --- | --- | --- |
| `normal`/0 | `0x1000` | `0x7ff82a5664d8` | `0x26a02930000` | 17 / 1 | `0x700001` |
| `normal`/1 | `0x1001` | `0x7ff82a566850` | `0x26a02900000` | 18 / 1 | `0x700002` |
| `detach`/0 | `0x1000` | `0x7ff82a566288` | `0x22682910000` | 17 / 1 | `0x700001` |
| `detach`/1 | `0x1001` | `0x7ff82a566aa0` | `0x226829c0000` | 18 / 1 | `0x700002` |

## 完整构建、归档和保留边界

最终wrapper完成：2026-10-25 11:34:22 +08:00；root实际终态记录：2026-10-25 11:41:36 +08:00。session `51261`，exit0，完整日志SHA256 `7e86bc8daeb0ff0aa5bfec9f368731dcf146f6d0d8e8d54e5e1b14b636a1a2c7`。首次预跑Runtime177/177、Audio640/640、5模式CLI全部通过的原件保存在本批，发生在foreign-mask责任修正之前，不能替代修正后的证明；正式结果以下表与当前完整日志/源码为准。修前完整session `48324`日志SHA256 `a3523d6565a66a770de8151fc5db98b6088d61aae4ab9df08fd936c9593af7aa`、源码ZIP `a0f547649f03112235e6cf8a29ba62644c7d5c61616c61e5288f220b759823cc`、packageZIP `c472353d7bded439b8842b00234abd54982f6da6df82ca3328a156f36b3aa2bf`仅是历史原件。共94条去重编译诊断原行，不等于新增bug数或零warnings。

| suite原文 | 结果 |
| --- | --- |
| 24/24 core checks passed. | 24/24 |
| 10/10 adapter checks passed. | 10/10 |
| 20/20 hosting checks passed. | 20/20 |
| Game adapter checks: 77/77 passed. | 77/77 |
| Native runtime checks: 177/177 passed. | 177/177 |
| 13/13 SDK checks passed. | 13/13 |
| Scope checks: 30/30 passed. | 30/30 |
| Diagnostics: 16/16 passed. | 16/16 |
| 14/14 package lifecycle checks passed. | 14/14 |
| Adapter module checks: 26/26 passed. | 26/26 |
| 21/21 target and mutation checks passed. | 21/21 |
| Revisioned data checks: 24/24 passed. | 24/24 |
| 14/14 storage/localization checks passed. | 14/14 |
| 19/19 inter-plugin service checks passed. | 19/19 |
| Game color integration checks: 25/25 passed. | 25/25 |
| 189/189 exact-hook checks passed. | 189/189 |
| 30/30 gameplay primitive checks passed. | 30/30 |
| 25/25 game settings checks passed. | 25/25 |
| 71/71 visual checks passed. | 71/71 |
| 11/11 performance checks passed. | 11/11 |
| 640/640 audio/content checks passed. | 640/640 |
| UI/input checks: 79/79 passed. | 79/79 |
| Run modification checks: 17/17 passed. | 17/17 |
| Judgement registry checks: 35/35 passed. | 35/35 |
| SDK distribution checks: 10/10 passed. | 10/10 |

相对上批561项声明源码：新增4、修改14、删除0，本批565项；构建前输入、当前manifest与源码ZIP每项匹配。源码ZIP SHA256 `16c091efadc8718ec708e2f676c1511ba1775f932d6d13bba12327a51824e6e7`；manifest SHA256 `4dc6470edcfc07a057b9d7a7ba1edf6c77aa2acd3acb3845a57bbc6badd29858`；22项包ZIP SHA256 `801c83bdf788156b7f4fd281e8395a399d2262d3d17a38a7afa3106db9da992d`。生成器实际重开两个ZIP逐entry校验当前源码/manifest，不能将ZIP hash替代内容核对。

root当批staged检查另证旧安装18项、其326项归档源码及原游戏3项hash相符，新包未安装。本作者仅读这些proof并核其字段，没有再执行旧游戏或独立重开326项归档，不把此项写成游戏实测。只读研究另给UnityPlayer原始输入hash，原游戏3项核对本身不包含UnityPlayer。

| 本批源码 | 变更 | SHA256 |
| --- | --- | --- |
| [src/RizToolbox.Adapter/AudioContent/NativeEngineCoroutineIdentityLedger.cs](../src/RizToolbox.Adapter/AudioContent/NativeEngineCoroutineIdentityLedger.cs) | 新增 | `6820c398cbc009c00d1dc77aa22a83f76081cd432cad1df47bf92aa1bac456c3` |
| [src/RizToolbox.Runtime/NativeRuntime.UnityCoroutineOwner.cs](../src/RizToolbox.Runtime/NativeRuntime.UnityCoroutineOwner.cs) | 新增 | `22264a2b0ee085b802ae9f812916fed0382468bb7a4332e0718dbff2f34c776a` |
| [tests/RizToolbox.AudioContent.Tests/NativeEngineCoroutineIdentityTests.cs](../tests/RizToolbox.AudioContent.Tests/NativeEngineCoroutineIdentityTests.cs) | 新增 | `1677172cd30d5671bb149654dcb7418aa883d48da2905262e79fbbd9f8e2347f` |
| [tests/RizToolbox.Runtime.Tests/UnityCoroutineOwnerRegression.cs](../tests/RizToolbox.Runtime.Tests/UnityCoroutineOwnerRegression.cs) | 新增 | `c8bf43b98b9df8bfa4d04e3f54ec7649a0e31cbe21623692b1dc8c753f75de87` |
| [native/RizToolbox.JudgeContext/EngineNodeContext.cpp](../native/RizToolbox.JudgeContext/EngineNodeContext.cpp) | 修改 | `e75de998e6f34ea708ee603993fa6f83e72ba8564050666c5348ba6b4884b6a5` |
| [native/RizToolbox.JudgeContext/EngineNodeContext.h](../native/RizToolbox.JudgeContext/EngineNodeContext.h) | 修改 | `92115b92c0324b1038b855fe783a85f8d5d8f4aabd5f2f73629c645fbb99295b` |
| [native/RizToolbox.JudgeContext/fixture/EngineNodeFixture.cpp](../native/RizToolbox.JudgeContext/fixture/EngineNodeFixture.cpp) | 修改 | `33d080192ce6322162e2c76498c25b287de027670a0f1ddfad02feda49234325` |
| [native/RizToolbox.JudgeContext/fixture/EngineNodeManagedFixture.cpp](../native/RizToolbox.JudgeContext/fixture/EngineNodeManagedFixture.cpp) | 修改 | `4f5779304cc5ba0c04e07f2fa439c155341ec333eb2ec145d3b71ce014095451` |
| [native/RizToolbox.JudgeContext/fixture/EngineNodeManagedFixture.h](../native/RizToolbox.JudgeContext/fixture/EngineNodeManagedFixture.h) | 修改 | `e3b371f437f01f384a73d018e81fe597f289ab9e6fdba444ef3b62736daecad5` |
| [scripts/Build-JudgeContext.ps1](../scripts/Build-JudgeContext.ps1) | 修改 | `cfe14769832b9e9bcb5891945eb3b21e0ec7333358a3733292a411d8a99f6f84` |
| [scripts/Test-EngineNodeManaged.ps1](../scripts/Test-EngineNodeManaged.ps1) | 修改 | `451b5e0e87b4df1dddf4dcea220ee1151acec5f14c0e9a7c559aa441e0e9521f` |
| [scripts/Test-EngineNodeNative.ps1](../scripts/Test-EngineNodeNative.ps1) | 修改 | `031f8a64c2cd2abccfd86259791612a72d94762d43a795db6cac02603aff3f92` |
| [src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.Types.cs](../src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.Types.cs) | 修改 | `c248f5eb854d28a6be5b371cce0bf003d57fba8939d7aa043e5bdf8d309b4bbf` |
| [src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.cs](../src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.cs) | 修改 | `97dcc6647787ed8b2d6a0f768eab2f3a2f68a80cf2cfed1bc209d6d5e19f6e85` |
| [tests/RizToolbox.AudioContent.Tests/Program.cs](../tests/RizToolbox.AudioContent.Tests/Program.cs) | 修改 | `d18942088341bd187890710381f8794a0950fe50d06466cdadcd0e1dc616b722` |
| [tests/RizToolbox.Runtime.Tests/NativeEngineNodeDispatcherTests.cs](../tests/RizToolbox.Runtime.Tests/NativeEngineNodeDispatcherTests.cs) | 修改 | `337591222c32ef529a73e371430b8129fc54d4764d43b5187ff48081f285d099` |
| [tests/RizToolbox.Runtime.Tests/NativeEngineNodeManagedRegression.cs](../tests/RizToolbox.Runtime.Tests/NativeEngineNodeManagedRegression.cs) | 修改 | `4003ac267ca6c185a98e899b20cfe593484adac4e1cbbee4916ee878774c88e2` |
| [tests/RizToolbox.Runtime.Tests/Program.cs](../tests/RizToolbox.Runtime.Tests/Program.cs) | 修改 | `f318465ce0729ecb69c7243e7a69232dca2d8ef0127c73ae0a5ad69c97288e73` |

| 本批只读研究原件 | SHA256 |
| --- | --- |
| [engine-installation-research.md](validation/foundation-engine-binding-20261025/engine-installation-research.md) | `304c58a5a4120d436e39e776cf72449ce08fa4885f96251da3be19c4c8a782da` |
| [scheduler-cleanup-research.md](validation/foundation-engine-binding-20261025/scheduler-cleanup-research.md) | `b4e8fd96102e972ca6b3ace4588b20671e985a0de17a9d6dedb82ac3e81d63e6` |
| [early-loader-design.md](validation/foundation-engine-binding-20261025/early-loader-design.md) | `ada1ee90a8b01547b0c692aa7f01ee3dfb36255ffa9e1a9dd2fbb819ec2f345c` |
| [research/engine-installation-evidence.json](validation/foundation-engine-binding-20261025/research/engine-installation-evidence.json) | `c44de32c5abdc88b5f75d34c056bf4972f0bda7429505676bd6c2a5c08e2441b` |
| [research/inspect-engine-installation.py](validation/foundation-engine-binding-20261025/research/inspect-engine-installation.py) | `9e59d9e6e55dbfb437071641fff948c91b1b2baaa6a0070d26184b64101a72f9` |
| [research/scheduler-evidence.json](validation/foundation-engine-binding-20261025/research/scheduler-evidence.json) | `64ed08c89d488ab8dba3572c406a848e558e52e946cbe04567353b1db3e9f65f` |
| [research/scheduler-inspect.py](validation/foundation-engine-binding-20261025/research/scheduler-inspect.py) | `6eaf01729b13e0b74b4ae93f91cbc220a3f2f8d50e4063fc20fbfd60cdde6faf` |
| [research/scheduler-consumption.json](validation/foundation-engine-binding-20261025/research/scheduler-consumption.json) | `e4d2f484362fee99ebd37efa0c30e7a94f1d2c6d0c55c5d77d8a8e7581e8ac76` |
| [research/scheduler-consumption.py](validation/foundation-engine-binding-20261025/research/scheduler-consumption.py) | `f08bec8ad085920f7c2188e639cab29109719ebfd6df465f02754cf335b4613e` |
| [research/scheduler-xrefs.json](validation/foundation-engine-binding-20261025/research/scheduler-xrefs.json) | `d2af8b2f7c47d341d25505e8be51f251b3f3f66fc4d76b758954758ea32c9e95` |
| [research/scheduler-xrefs.py](validation/foundation-engine-binding-20261025/research/scheduler-xrefs.py) | `29f923fe050b0d5df77699b6325fb701f679c5c126d8a9a904f92efce4e587ef` |
| [research/scheduler-contract.json](validation/foundation-engine-binding-20261025/research/scheduler-contract.json) | `f9ff4d13c1387a8ab6984659357acdb65ecfa2a463e4e04d8393b13e12d8e078` |
| [research/scheduler-contract.py](validation/foundation-engine-binding-20261025/research/scheduler-contract.py) | `625433ae653ed27bafa3e2b2b18a9e9efd031ecb0113baa418d47228f5b127bc` |

## Evidence

### E-001：root观察完整构建真实终态

- observed_at: `2026-10-25T03:41:36.8684546Z`（本报告汇编时采用root终态记录时间；不代替各原件的执行时间）。
- source_type: `log`；source_ref: [build-run.json](validation/foundation-engine-binding-20261025/build-run.json)。
- content_hash: `fa62b7578ed8f0f7fd0054c7001b5861d226f8be18615c50f3bbddbb73c86109`。
- artifact_path: `docs/validation/foundation-engine-binding-20261025/build-run.json`。
- repro_command: `& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File docs/validation/foundation-engine-binding-20261025/run-build.ps1`（本机依赖和隔离范围见正文；只读研究会重写其JSON，不执行游戏）。
- raw_excerpt: `session=51261; terminalExitCodeObserved=0; 25 suites 1617/1617; no game/install`。
- linked_workitem: `n/a`（本批工程实现）；supersedes: `none`（新批修正另行记录，不改历史原件）。

### E-002：scheduler完整EAX消费与初始化时机静态原件

- observed_at: `2026-10-25T03:41:36.8684546Z`（本报告汇编时采用root终态记录时间；不代替各原件的执行时间）。
- source_type: `file`；source_ref: [research/engine-installation-evidence.json](validation/foundation-engine-binding-20261025/research/engine-installation-evidence.json)。
- content_hash: `c44de32c5abdc88b5f75d34c056bf4972f0bda7429505676bd6c2a5c08e2441b`。
- artifact_path: `docs/validation/foundation-engine-binding-20261025/research/engine-installation-evidence.json`。
- repro_command: `C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/research/inspect-engine-installation.py`（本机依赖和隔离范围见正文；只读研究会重写其JSON，不执行游戏）。
- raw_excerpt: `447EF2 bytes83f801 cmp eax,1; 17 node direct refs; CRT initializer non-null8+3006; first-write safety=false`。
- linked_workitem: `n/a`（本批工程实现）；supersedes: `none`（新批修正另行记录，不改历史原件）。

### E-003：scheduler消费及异步/continuation有限静态区间

- observed_at: `2026-10-25T03:41:36.8684546Z`（本报告汇编时采用root终态记录时间；不代替各原件的执行时间）。
- source_type: `file`；source_ref: [research/scheduler-consumption.json](validation/foundation-engine-binding-20261025/research/scheduler-consumption.json)。
- content_hash: `e4d2f484362fee99ebd37efa0c30e7a94f1d2c6d0c55c5d77d8a8e7581e8ac76`。
- artifact_path: `docs/validation/foundation-engine-binding-20261025/research/scheduler-consumption.json`。
- repro_command: `C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/research/scheduler-consumption.py`（本机依赖和隔离范围见正文；只读研究会重写其JSON，不执行游戏）。
- raw_excerpt: `selected complete RF/CHAININFO groups; no gameCodeExecuted; scheduler closure unproven`。
- linked_workitem: `n/a`（本批工程实现）；supersedes: `none`（新批修正另行记录，不改历史原件）。

### E-004：返回ABI2真实typed Dobby native验证

- observed_at: `2026-10-25T03:41:36.8684546Z`（本报告汇编时采用root终态记录时间；不代替各原件的执行时间）。
- source_type: `log`；source_ref: [engine-node-native-evidence.json](validation/foundation-engine-binding-20261025/engine-node-native-evidence.json)。
- content_hash: `9e95f70b7fd327c8d1ab2f7ecab692596e9ab62648ec92c5469543c722e9db6b`。
- artifact_path: `docs/validation/foundation-engine-binding-20261025/engine-node-native-evidence.json`。
- repro_command: `& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File docs/validation/foundation-engine-binding-20261025/run-build.ps1`（本机依赖和隔离范围见正文；只读研究会重写其JSON，不执行游戏）。
- raw_excerpt: `checks=3402; actualOriginalCalls136; observedPairs124; executeReturnBits32; fullWidthCases8/completions10`。
- linked_workitem: `n/a`（本批工程实现）；supersedes: `none`（新批修正另行记录，不改历史原件）。

### E-005：真实native/CLR/独立节点及身份账本链

- observed_at: `2026-10-25T03:41:36.8684546Z`（本报告汇编时采用root终态记录时间；不代替各原件的执行时间）。
- source_type: `log`；source_ref: [engine-node-managed-evidence.json](validation/foundation-engine-binding-20261025/engine-node-managed-evidence.json)。
- content_hash: `c62f2eff0954ab8569e636c7bab0491dd0045e0e468cf0902eccb1f854cfb112`。
- artifact_path: `docs/validation/foundation-engine-binding-20261025/engine-node-managed-evidence.json`。
- repro_command: `& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File docs/validation/foundation-engine-binding-20261025/run-build.ps1`（本机依赖和隔离范围见正文；只读研究会重写其JSON，不执行游戏）。
- raw_excerpt: `5 modes; checks=889; actualOriginalCalls44/physicalCaptures28; actualIdentityLedgerCaptureVerified=true; trackerIdentityProvidersControlled=true; actualTrackerBackendExecuted=false`。
- linked_workitem: `n/a`（本批工程实现）；supersedes: `none`（新批修正另行记录，不改历史原件）。

### E-006：实际三tracker受控backend与ownerApi合成元数据测试

- observed_at: `2026-10-25T03:41:36.8684546Z`（本报告汇编时采用root终态记录时间；不代替各原件的执行时间）。
- source_type: `log`；source_ref: [managed-preflight.log](validation/foundation-engine-binding-20261025/managed-preflight.log)。
- content_hash: `37577b9ed3a82546313c6239351c1bfa04a93e7f8d63aa08860ed5caefc1c021`。
- artifact_path: `docs/validation/foundation-engine-binding-20261025/managed-preflight.log`。
- repro_command: `& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File docs/validation/foundation-engine-binding-20261025/run-managed-preflight.ps1`（本机依赖和隔离范围见正文；只读研究会重写其JSON，不执行游戏）。
- raw_excerpt: `Runtime177/177; Audio640/640; identity13 cases incl actual three trackers on controlled backends; owner API7 cases with synthetic official exports`。
- linked_workitem: `n/a`（本批工程实现）；supersedes: `none`（新批修正另行记录，不改历史原件）。

### E-007：当前声明源码完整冻结归档

- observed_at: `2026-10-25T03:41:36.8684546Z`（本报告汇编时采用root终态记录时间；不代替各原件的执行时间）。
- source_type: `file`；source_ref: [source-inputs.zip](validation/foundation-engine-binding-20261025/source-inputs.zip)。
- content_hash: `16c091efadc8718ec708e2f676c1511ba1775f932d6d13bba12327a51824e6e7`。
- artifact_path: `docs/validation/foundation-engine-binding-20261025/source-inputs.zip`。
- repro_command: `C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/write-report.py`（本机依赖和隔离范围见正文；只读研究会重写其JSON，不执行游戏）。
- raw_excerpt: `entries=565; archive/manifest/current entry hashes matched; source diff +4/~14/-0`。
- linked_workitem: `n/a`（本批工程实现）；supersedes: `none`（新批修正另行记录，不改历史原件）。

### E-008：22项暂存包与旧安装分离的核对

- observed_at: `2026-10-25T03:41:36.8684546Z`（本报告汇编时采用root终态记录时间；不代替各原件的执行时间）。
- source_type: `file`；source_ref: [package-archive-check.json](validation/foundation-engine-binding-20261025/package-archive-check.json)。
- content_hash: `e9b578fcafbfdf8c1110006e90b65fa2afc036acef1d4fb9df945deadc4f1266`。
- artifact_path: `docs/validation/foundation-engine-binding-20261025/package-archive-check.json`。
- repro_command: `C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/write-report.py`（本机依赖和隔离范围见正文；只读研究会重写其JSON，不执行游戏）。
- raw_excerpt: `22 package entries matched; installed=false; separate previous18/326 and game3 proof preserved`。
- linked_workitem: `n/a`（本批工程实现）；supersedes: `none`（新批修正另行记录，不改历史原件）。

### E-009：修复前成功构建仍保留的身份责任缺口

- observed_at: `2026-10-25T03:25:12.334588+00:00`（root保留修复前实际成功终态；不作为最终候选）。
- source_type: `file`；source_ref: [before-mask-fix/build-run.json](validation/foundation-engine-binding-20261025/before-mask-fix/build-run.json)、[before-mask-fix/source-inputs.zip](validation/foundation-engine-binding-20261025/before-mask-fix/source-inputs.zip)。
- content_hash: build-run=`48e17923fae991ef6609af192f51d113bfc2107e969b26f3278daf15485ab01a`；源码ZIP=`a0f547649f03112235e6cf8a29ba62644c7d5c61616c61e5288f220b759823cc`。
- artifact_path: `docs/validation/foundation-engine-binding-20261025/before-mask-fix/build-run.json`；归档内源码条目`src/RizToolbox.Adapter/AudioContent/NativeEngineCoroutineIdentityLedger.cs`原SHA256=`8c90dfb75a8d6d58cfd4c370c8495067205ffc9118f9648ac672bff4ee1be185`。
- repro_command: `C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/write-report.py`（重新核修复前ZIP各entry与其build-inputs-before；不重建或覆写历史）。
- raw_excerpt: `session=48324; passed=1617/1617; finalCandidate=false; old relevant included nearest.Relevant and dropped owned unknown nodes under foreign Start`。
- linked_workitem: `n/a`（本批已修并重新完整构建）；supersedes: `none`，历史原件保留。

### E-010：旧实现与强化Foreign测试的实际隔离red复现

- observed_at: `2026-10-25T03:41:51.711332+00:00`（独立replay开始时间；root实际执行并观察exit0）。
- source_type: `log`；source_ref: [mask-before-replay/20261025T034151Z-6b979b54/result.json](validation/foundation-engine-binding-20261025/mask-before-replay/20261025T034151Z-6b979b54/result.json)、[mask-before-replay/20261025T034151Z-6b979b54/run.stdout.log](validation/foundation-engine-binding-20261025/mask-before-replay/20261025T034151Z-6b979b54/run.stdout.log)。
- content_hash: result=`5936c4003a15c2807123eaca37f44258472327ed0de535d39159ca6d14f8217a`；运行stdout=`2679bfbee2c54a71d9472e6031595803de912e3b18d00db6fe274fbb5f8f8a1c`。
- artifact_path: `docs/validation/foundation-engine-binding-20261025/mask-before-replay/20261025T034151Z-6b979b54/result.json`。
- repro_command: `C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/replay-mask-before.py`（仅独立临时project；直接引用现有四个Release DLL，不重建共享源码）。
- raw_excerpt: `buildExitCode=0; runExitCode=0; redObserved=true; System.InvalidOperationException: Engine tracker identity check failed.; stack contains Check → Foreign`。
- linked_workitem: `n/a`（F-004修前反例）；supersedes: `none`，完整修前/修后构建原件均保留。


## Findings

### F-001：Execute必须保留完整EAX，当前返回ABI2已在自有链验证

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-002, E-003, E-004, E-005, E-007]`。
- location: `UnityPlayer+447EF2/78F840`，`native/RizToolbox.JudgeContext/EngineNodeContext.cpp`，新CLR桥与typed声明。
- impact: `纠正低byte不能满足实际scheduler消费者的问题；当前自有真实链保uint32高位并拒旧ABIhelper。没有推定全RAX、所有异常PC或生产首发已通过。`
- repro_steps: `静态研究脚本重读固定输入，再用root完整构建复跑真实typed fixture；核每模式executeHighBitReturns与RawReturn。`
- remediation: `保留ABI2闸口，完整生产采用仍需单独首发owner。`
- optional_attack: `n/a`，兼容工程无攻击主张。

### F-002：身份链能保持独立代次和延后handle，但真实tracker/owner来源仍需接线

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-001, E-005, E-006, E-007]`。
- location: `NativeEngineCoroutineIdentityLedger`，`NativeRuntime.ReadUnityCoroutineOwner`及新增13+7项测试。
- impact: `本地实际代码在已测受控条件下保存receiver/nativeowner/slot/tracker generation/node generation，handle单调升级。foreign遮蔽责任缺口本批已修并重构建，详见F-004；仍不能据本Finding宣称游戏过滤全覆盖。actual三tracker受控backend、actualnative受控provider、synthetic owner metadata三类证明相互独立。`
- repro_steps: `核受控三tracker用例无额外backend读/根变化，再核真实联合identityChains与false边界，不能跨组提升为游戏端到端。`
- remediation: `在具有生产首发凭据后，由coordinator采用真实root/provider和owner验证边界；不在After解引用free-node。`
- optional_attack: `n/a`。

### F-003：四个引擎入口安全首次发布与取消完成仍未证明

- severity: `n/a_re`；category: `other`；status: `candidate`；confidence: `high`。
- evidence_ids: `[E-001, E-002, E-003, E-005, E-008]`。
- location: `Doorstop pre-il2cpp_init`、UnityPlayer CRT/scheduler、完整B01–B19 SPEC。
- impact: `现有时机没有此前完整调用/线程历史；选定直接引用和RF研究没有关闭全部异步、inline、future Start责任。缺V4/actualgame/coordinator/scheduler closure，不能解除Pending或发EngineCancelled。`
- repro_steps: `核research boundaries及真实fixture中firstInstallSafetyVerified/productionEngineHooksPublished/schedulerClosureVerified/engineCancelled等false字段。`
- remediation: `先做自有宿主验证更早准入门与失败保留，再映射固定游戏冷启动合同；随后完成生产身份关联与逐记录取消完成。`
- optional_attack: `n/a`。

### F-004：foreign遮蔽漏记owned责任已最小修正并重新完整构建

- severity: `n/a_re`；category: `design`；status: `validated`（修前归档、最终源码与受控Frame回归）；confidence: `high`。
- evidence_ids: `[E-001, E-007, E-009, E-010]`。
- location: `NativeEngineCoroutineIdentityLedger.Capture`的relevant过滤，`NativeEngineCoroutineIdentityTests.Foreign`。
- impact: `Owner值匹配本run但node未known且最近Start为foreign时，现先登记owned标量责任再转Unknown，不借outer或读provider；旧用例错误的ignore断言已更正。未声称真实游戏复现。`
- repro_steps: `核E-010隔离旧账本+强化测试实际red及Check/Foreign栈；对比before-mask-fix ZIP与最终两个源码entry，再核最终full中的同一Foreign案例通过及Calls/Unknown/Pending和精确Dispose。`
- remediation: `本批代码修正已完成；真实native可控foreign场景、生产来源和整体闭包仍须后续验证，不能升级为取消/资源退休许可。`
- optional_attack: `n/a`。

## Path与后续实施

### P-001：修正消费者ABI并在分层证据中关联身份

- path_type: `callflow`。
- start: `固定磁盘消费者cmp eax,1，以及保留的tracker receiver与独立native owner。`
- goal: `实际transport不丢EAX，身份链不串代次，同时保持生产缺口未授权限。`
- steps:
  1. action: `核scheduler实际EAX消费者，增加返回ABI2和精确u32typed入口`；evidence: `E-002, E-003`；finding: `F-001`。
  2. action: `自有native目标真实Dobby调用，保原输入/完整返回/异常及free-node After边界`；evidence: `E-004, E-005`；finding: `F-001`。
  3. action: `实际identity ledger包装独立node ledger，Start同步关联、后续同record handle升级`；evidence: `E-005, E-006`；finding: `F-002`。
  4. action: `单独测试actual三tracker受控backend和合成metadata owner API，保留全部生产false能力位`；evidence: `E-001, E-006, E-007`；finding: `F-002, F-003`。
- residual_risks: `F-004仅在受控Frame边界修正并验证；独立证据没有连成真实游戏链；没有安全首次源写、coordinator采用、全部scheduler/inline/async/future producer封口或GC根退休证明。`

后续先按 [early-loader-design.md](validation/foundation-engine-binding-20261025/early-loader-design.md) 在独立自有进程证明更早准入：静态依赖proxy的极小安装阶段必须实际先于importer TLS、CRT与四入口首调用，不能后移到UnityMain2。固定`Rizline.exe → UnityPlayer.dll → WINHTTP.dll`静态导入关系只是候选依据；现Doorstop复杂初始化和延迟加载helper/Dobby不能直接搬入DllMain。合同要求实际依赖闭包、已初始化helper/Dobby、无迟到动态加载或跨线程等待，以及正常发布、早调用拒绝、逐写故障与sticky责任保留；该自有宿主本批尚未实现，也不自动授游戏V4。

自有native宿主还须补foreign与owned交错的实际调用场景。取得固定游戏同一合同的V4生产凭据后，coordinator才能采用已验证root/owner/provider，关联精确Stop请求、actual node代次和调度撤销/执行退出。scheduler pump、异步/continuation旁路、Start内联清理及future Start封口仍须逐项接线，再设计逐记录EngineCancelled与独立资源退休。按用户安排，真实游戏实测留待后续，不代替当前代码工作；完整B01–B19逐项验收继续进行。

## 时间线与复现

| 观察 | 原件 |
| --- | --- |
| 2026-10-25 固定scheduler/启动/owner机器码研究 | [engine-installation-research.md](validation/foundation-engine-binding-20261025/engine-installation-research.md)、[research/scheduler-consumption.json](validation/foundation-engine-binding-20261025/research/scheduler-consumption.json) |
| 2026-10-25 返回ABI2自有native预跑与Runtime/Audio/CLI预跑 | [native-preflight.log](validation/foundation-engine-binding-20261025/native-preflight.log)、[managed-preflight.log](validation/foundation-engine-binding-20261025/managed-preflight.log) |
| 2026-10-25 11:25:12 +08:00 root保留修正前成功终态，finalCandidate=false | [before-mask-fix/build-run.json](validation/foundation-engine-binding-20261025/before-mask-fix/build-run.json)、[before-mask-fix/source-inputs.zip](validation/foundation-engine-binding-20261025/before-mask-fix/source-inputs.zip) |
| 2026-10-25 11:34:22 +08:00 wrapper完成；2026-10-25 11:41:36 +08:00 root观察并记录完整终态 | [build.log.run.json](validation/foundation-engine-binding-20261025/build.log.run.json)、[build-run.json](validation/foundation-engine-binding-20261025/build-run.json) |
| 2026-10-25 11:41:51 +08:00 root实际执行隔离旧实现+强化测试并取得red断言 | [mask-before-replay/20261025T034151Z-6b979b54/result.json](validation/foundation-engine-binding-20261025/mask-before-replay/20261025T034151Z-6b979b54/result.json) |
| 本报告生成时再次逐ZIP entry核对 | [source-archive-check.json](validation/foundation-engine-binding-20261025/source-archive-check.json)、[package-archive-check.json](validation/foundation-engine-binding-20261025/package-archive-check.json) |

在仓库根执行下列命令。第一条完整构建会重新产生当批产物和日志，需遵守root唯一builder并保全冻结原件；第二条只核当前证据/源码/ZIP并写本文。只读研究脚本入口见Evidence，依赖固定磁盘输入；旧批原件保持原样，不将新结果写回旧报告。

```powershell
& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File docs/validation/foundation-engine-binding-20261025/run-build.ps1
C:/Python314/python.exe docs/validation/foundation-engine-binding-20261025/write-report.py
```

详见 [README.md](validation/foundation-engine-binding-20261025/README.md)、[next-integration.md](validation/foundation-engine-binding-20261025/next-integration.md)及[完整验收清单](../.docs/foundation-expansion/CHECKLIST.md)。
