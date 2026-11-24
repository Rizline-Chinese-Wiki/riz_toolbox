# 固定System32 WinHTTP转发验证器：接口与证据边界

2026-10-25。本页记录新验证器作者的接口核对，尚不是实际构建或fixture通过报告。root是唯一builder/child runner；本验证器只读磁盘、原始记录及parent持有的文件身份，不加载WinHTTP、不启动child、不发送网络请求。既有604/576/12输入历史和旧verifier均不修改。

## 官方ABI核对

已只使用Microsoft官方资料核对四个精确名称，按名称绑定，不把本地代理ordinal交给系统DLL。

| 固定slot | 名称 | SDK参数与返回的关键约束 |
|---:|---|---|
| 0 | [WinHttpOpen](https://learn.microsoft.com/en-us/windows/win32/api/winhttp/nf-winhttp-winhttpopen) | 五参数；第5个DWORD flags必须通过x64栈参数原样保留；返回HINTERNET指针 |
| 1 | [WinHttpGetProxyForUrl](https://learn.microsoft.com/en-us/windows/win32/api/winhttp/nf-winhttp-winhttpgetproxyforurl) | session、UTF-16 URL、options指针、output指针；返回完整32位BOOL |
| 2 | [WinHttpGetIEProxyConfigForCurrentUser](https://learn.microsoft.com/en-us/windows/win32/api/winhttp/nf-winhttp-winhttpgetieproxyconfigforcurrentuser) | 一个in/out配置指针；返回完整32位BOOL；非空输出字符串由调用方GlobalFree |
| 3 | [WinHttpCloseHandle](https://learn.microsoft.com/en-us/windows/win32/api/winhttp/nf-winhttp-winhttpclosehandle) | 一个HINTERNET参数；返回完整32位BOOL |

失败后立即取GetLastError。成功调用没有由本页假定LastError必为零；host应先设置独特种子，并与实际direct system baseline精确比较。Open两次成功得到的handle指针不同，应该核各自关联/关闭及非零返回，不硬比地址。BOOL也不能只看AL或当作一字节。

本轮不提供PAC、WPAD或请求：outside调用使用Open NO_PROXY/synchronous，GetProxy仅null或实际自有session加固定本地无效URL、全部autoproxy flags/autodetect/reserved/URL字段为零，Close自有handle，IE只读本机配置。GetIE可能读registry，因此四个真实TLS/C/C++/DllMain phase只安排fixture最终合同指定的最小同步本地拒绝调用：Close(NULL)和GetProxy(NULL,invalid-local-url,zero-options)。Open/IE的正向比较、完整四API基线及Importer额外outside export调用都在LoadLibrary返回后执行。保留四个普通import名称不等于在每个初始化phase执行全部四API，受控执行也不推导系统WinHTTP全部loader路径安全。

## 已固定的canonical协议

[原生头文件](../../../native/RizToolbox.Doorstop/riz_winhttp.h)是字段/状态的实际来源：Info版本1、1248字节，系统路径208、加载路径728，各260个UTF-16 WCHAR；四个真实地址在152，四个保护值184。状态依次为Uninitialized、Resolving、Ready、DisabledNoPublish、Unknown。GetInfo不等待，保留调用方LastError；有效snapshot必须有独立1248字节原件，JSON不是代替品。

生产精确7个导出为三个Riz API（Initialize/GetInfo/GetVersion）及四个WinHttp名称；测试profile只有额外固定SetFault(stage,index)，不接受路径/模块/地址/任意callback。Info分别保active/initial/poison/resolved表地址及系统模块、持有文件、实际文件id/大小、PE字段和解析地址。重复/重入毒化后不能把保留resolved地址误读成仍允许forward：应同时核active实际指向poison、状态永久Unknown及后续类型正确的NULL/FALSE/ERROR_DLL_INIT_FAILED。已经取到旧resolved表的进行中调用与之后新调用要分开解释。

typed stub按实际ASM核`mov r11,[rip+activePointer]; jmp [r11+slot*8]`；四个精确导出RVA必须引用同一个对齐active指针，代码为RX、指针为非执行可写数据。没有在tail-jump之后执行resolver/计数/日志API，因此不能改真实系统返回值或LastError。单次原子整表发布是实现及实际指令合同；仅两张前后snapshot不能被描述为已穷尽所有并发中间态。

## 父级proof接口

`verify-winhttp-forward.py`的PE阶段将接受生产DLL、同basename独立测试DLL、host及Importer的绝对文件路径。System32路径只能由parent真实GetSystemDirectoryW推得，没有任意system-path参数。每个PE保存完整SHA256/长度及同一文件句柄前后GetFileInformationByHandle身份；runner必须从此阶段直到所有child结束一直持FileShare.Read拒绝写/删除，不能只靠这个脚本短暂读取替代全期锁。

PE parser只从[历史早期链验证器](../foundation-early-fixed-20261025/verify-early-fixed.py)抽取四个定义（EvidenceError/require/sha/inspect_pe），先验完整SHA256 `056b494994c86558444d873da2a841c99fd31cd82297884fa41b288f20730d64`；不执行旧模块bootstrap，也不隐式载入其fixed verifier。该实际旧文件需要列为本批传递输入。独立检查AMD64、7/8准确导出及无隐藏ordinal导出、无entry/TLS/delay/loadconfig/CLR、严格OS import清单、真实stub bytes及普通四名称依赖。系统DLL身份限定本机当前文件，Windows更新后必须重新冻结，不能套用旧系统版本哈希。

case阶段读取host实际JSON及同目录native Info、typed call rows/Importer snapshot二进制，直接依实际已冻结头文件解码：Info1248、Row320（args40/strings152/PrivateString56）、ImporterSnapshot10272。每份raw必须同目录basename、实际SHA匹配；Info及每个typed row的全部JSON字段与二进制独立解码相等。host序号与Importer序号是两个独立来源，各自完整，不能要求跨route唯一。Initializer真实四phase各两条Close/Proxy拒绝，outside phase11四条完整调用，phaseMask0x8f0、12实际Importer rows；unused有界行全部0。

父级独立核System32磁盘四个export RVA与实际GetProcAddress地址减moduleBase一致、实际VirtualQuery归属同系统image且可执行、loaded path/fileid与parent proof一致；forward调用地址属于held forwarder四stub。initial/poison两个不同static表的指针内容独立由held磁盘解出并与实际读取slots对比；initial表身份又须等于四stub共用active pointer的磁盘初值。resolved-prefix/fault phase、重复初始化毒化和持有责任逐字段核验，不能只看host的checks/passed。正常direct/forward pair独立比较完整BOOL、LastError及盐hash私有输出；每个成功Open均须有真实关联Close，worker使用实际三个独立线程。

case CLI必须由parent另外传入`--child-pid N --profile production|fixture --fault-stage N --fault-index N`，并与actualProcessId、实nativeInfo selector及实际命令相同；不能把host自报selector当作独立预期，以免遗漏命令参数导致多次normal冒充故障覆盖。输出回填actualChildPid/profile/faultStage/faultIndex给parent再次交叉。PE阶段CLI为`--inspect --production-forward ABS --fixture-forward ABS --host ABS --importer ABS --output ABS`，`--forward`仅为production-forward的同义参数；case阶段为`--case ABS --pe-proof ABS`及前述四个必需预期参数。所有child仍由parent负责，脚本不自行启动。

当前仅实际执行Python语法解析与精确hash固定四定义AST载入，均通过；尚未实际核验本批PE或host case，不虚构通过数字。后续首跑若暴露schema/实际API差异，应保留失败原件再做最小修正。

本轮只证明受控四API转发及loader外resolver。实际游戏生产入口、WinHTTP与Kernel首次绑定时机、V4、真实GC/coordinator/scheduler及EngineCancelled闭包仍需独立工作，不能因系统四个名称能解析就勾选全部B01–B19。
