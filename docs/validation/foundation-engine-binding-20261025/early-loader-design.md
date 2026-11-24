# 下批自有早期加载 fixture 的验收合同

2026-10-25。**先在独立自有进程证明：proxy 的极小安装阶段确实先于 importer 的 TLS、CRT 和首次目标调用。** 固定静态导入图 `Rizline.exe → UnityPlayer.dll → WINHTTP.dll` 是候选依据；安装点是依赖 proxy 的初始化阶段，不能后移到 UnityMain2。该图不授予游戏安全首写或 V4 Ready；本页仅定义下批实现，不修改本批冻结源码，不启动游戏。

现有 Doorstop 的 `DllEntry` 顺序是 `init_crt → fix_cwd/logger/paths → load_proxy → inject`；真正 `RizBeforeIl2CppInit` 到 init wrapper 才动态加载 helper，producer 再加载 Dobby。因此不可直接把现有 producer 搬进 DllMain。[实际启动证据](engine-installation-research.md)及 [riz_preinit.c](../../../native/RizToolbox.Doorstop/riz_preinit.c)保留该边界。

## 自有宿主与观测原件

构造普通静态依赖 `OwnedHost.exe → OwnedImporter.dll → OwnedProxy.dll → 已静态加载的 helper、固定 Dobby`，另有已初始化的纯 C recorder。先用唯一自有 DLL 名验证机制；它只类比 WINHTTP 依赖位置，不能声称已测试真实系统 WinHTTP 转发。普通 import 必须在 PE 中实际保留，不能是 delay-load、运行后 LoadLibrary 或被链接器优化掉的引用。依赖不能循环或在初始化中反向执行 importer。

Importer 含真实 TLS callback、C/C++ CRT initializer、DllMain 和四个独立原生目标，分别在这些阶段直接调用目标；Execute 为 uint32，其余保持 Start/Release uint8、Detach void。再覆盖 Start 同步 Execute/Release、原生异常、无观察者转发、worker 和 Attach 后配对；CLR/复杂观察者只在 loader 窗口外启用。四目标与节点页均属于 fixture，使用现有真实 helper ABI 2 和固定 Dobby Prepare/Configure/Commit。

Recorder 使用编译期初始化的定长记录与 Interlocked 序号，不依赖待初始化的 C++ 对象、logger 或线程 TLS 析构。逐条保存实际 OS 线程、模块实例、reason/阶段、进入/退出、四目标首次调用、各次 Prepare/Configure、每次机器码写尝试与完成；日志溢出即 Unknown。父进程在子进程退出后导出共享记录，避免 loader 阶段格式化/等待。每行发布 tuple 至少含 `module/hash/RVA/kind/slot/target/original/entry/epoch`、原字节/发布字节/保护、写序号及最后状态。

## 必须实际通过的分支

| 场景 | 子进程必须留下的结果 |
| --- | --- |
| 冷启动正常 | helper/Dobby 所需 TLS、CRT 和 attach 完成 → proxy 安装进入 → 四项全部 Prepare/不可变 Configure → 四次实际 Commit 完成/复核 → Ready → importer TLS/CRT/DllMain/目标首调用。顺序由真实事件证明，不写死预期顺序再比较自己生成的模拟日志 |
| initializer 与 worker | TLS、CRT 各自最早的四目标调用均走已发布原通道，保实参、完整 Execute EAX 与原异常。Importer 可在初始化中请求创建 worker，但不得等待；host 在 loader 窗口外等待并核验实际 worker 入口时刻。另用已存在 worker／已加载 importer 的迟到安装场景验证拒绝，不能用“启动时没看到 worker”代替历史 |
| 写前拒绝 | 错模块/hash/位数、坏 prefix/ABI、缺依赖/版本、先前目标调用、重入/重复初始化、记录不完整、依赖未初始化：写计数必须为 0，无 Ready；有确定无写证据时可记录 DisabledNoWrite，其余 Unknown |
| 每个故障点 | 对每项 Prepare/Configure 和第 N 次 Commit 前后分别注入真实失败；保存真实已配置/已写 tuple。首个写尝试后故障为 sticky Unknown，禁止继续 importer、重试/假回滚/卸载。独立 child 有限失败退出并保留原件，不能假装未安装后正常放行 |

## Loader lock 与发布责任

Loader lock 下不能任意动态加载、跨线程等待或调用尚未初始化的动态 CRT；创建线程也不能视为初始化任务已执行。Microsoft 将这些列为 DllMain 限制，现有 Doorstop 的复杂初始化不能构成本 fixture 的安全论据。[DLL best practices](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-best-practices)、[DllMain](https://learn.microsoft.com/en-us/windows/win32/dlls/dllmain)

因此必须逐项审计本次 proxy→helper→Dobby 的实际调用/import 闭包：无迟到 LoadLibrary、delay import、托管初始化、provider 隐式加载或等待外部线程；C++ 静态表、CRT 分配和加密/日志依赖的可用时刻均需实证。父进程可先核 hash 并保持禁止写入的文件句柄，但这不自动证明游戏内 bootstrap 的身份验证/初始化安全。若固定 Dobby 无法满足 loader 阶段合同，本方案就拒绝生产发布，不能用绿色自有顺序日志掩盖它。

后续 package 必须决定 helper/固定 Dobby 的唯一静态导入名称及可确定的放置或 SxS 策略；PE 的 basename 不会自动解析到现有 `BepInEx/plugins`、`BepInEx/core`。不得靠 loader 中临时修改搜索路径或换名后误认另一已加载 DLL。manifest 应记录普通/延迟/转发依赖、完整路径与实际模块身份、文件和 source hash、OS/架构、TLS/CRT 表与顺序、工具链、原件 hash、每个故障终态；代理系统 WinHTTP 的转发/命名与依赖闭包另行验收。此 fixture 只证明自有宿主的实际加载和四入口发布，不证明真实 UnityPlayer 首发、scheduler/async 封口、取消完成或全局退休；`EngineCancelled=false`、Pending 和完整 B01–B19 范围保持。
