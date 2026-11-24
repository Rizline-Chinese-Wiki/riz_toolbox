# r4固定WinHTTP转发：原件有限复核

2026-10-25，flavor=null。已读取[真实终态](terminal.json)：root唯一session40901退出0，成功证据明确选择`attempts/r4`。本页作者负责过verifier及归档脚本，以下是对原件的有限自查，不是外部独立审计；只读磁盘、解码及离线重算，没有构建、运行child、安装或启动游戏。[范围](scope.md)仍仅限本机System32四名称转发及自有调用者。

## 证据、发现和边界

| 证据 | 本次复核发现 | 结论与后续路径 |
|---|---|---|
| [汇总](summary.json)、[21实际case原件](raw/cases/fixture-result.json) | 逐项aggregate result/process等于各目录原件；离线对21份case重新执行当前冻结verifier，结果逐字段完全相同 | 真实21用例完成；5653项父断言、2426项host检查、456次API调用、132对直接/转发比较。父断言不是原调用数 |
| [PE proof](raw/cases/pe-proof.json)及实际DLL磁盘 | production恰7个nonforwarded命名导出；fixture额外SetFault，恰8个；无隐藏ordinal导出。两版entry/TLS/delay/loadconfig/CLR均无，普通导入仅12项声明OS API | 生产/测试身份未混用；无CRT合同仅针对forward组件，Host/Importer仍明确使用CRT，不推导整条OS链无运行库或loader闭包 |
| 两normal的1248 Info、320-byte calls及10272 Importer原件 | 两例各152次调用/66对比较；四stub确为mov R11再按slot tail-jump，共同active指针；实际四系统地址减base等于held系统PE对应名称RVA，VirtualQuery属于该MEM_IMAGE/RX页 | 有实际系统调用与ABI保持证据，不是只证明名称存在；调用样本及故障集合仍有限 |
| [成功源码清单](source-final-manifest.json)、[成功613 ZIP](source-final-inputs.zip)、[raw清单](raw-manifest.json) | 613项清单/当前文件/ZIP逐项匹配；17项held前后/当前hash相同；260 raw成员和4 binary成员ZIP逐项匹配 | 归档采用r4真实成功输入，不拿根目录初始失败ZIP冒充；23旧包及游戏18已装/4原文件均另有匹配记录 |

## 四API真实语义

两个normal均先在host的loader外显式Initialize，再加载Importer。系统模块在该例Initialize之前为0；Ready后四slot一次发布，Info为state2/flags63/resolved4/publish1。直接调用系统导出与通过四ASM stub调用按相同种子比较LastError、完整返回及输出语义；每例有三个不同于主线程的真实worker，每线程四轮五对比较。

Open第五个DWORD参数保留在原x64栈位。实际非零探针`0x40000000`在direct和forward两条路径均返回NULL、LastError87；普通NO_PROXY会话成功并有真实关联Close。BOOL记录和比较均使用32位，不截AL；本轮系统BOOL实际样本只有0/1，不能称已覆盖所有非规范32位返回模式。完整RAX/参数不被后处理截断的结构依据是精确的leaf tail-jump字节，不是穷举所有可能API输出。

所有call JSON字段与实际Row二进制独立解码一致。21例共108个非空私有输出字符串，对应108次GlobalFree尝试、0失败；字符串只保存进程内盐hash、NULL/长度等字段，不在报告展开用户配置。direct/forward的hash仅能在相同进程盐下比较，不应跨child要求hash相同；输出pointer也不作为跨调用相等条件。

两个normal的Importer实际phase4/5/6/7各仅两条`Close(NULL)`、`GetProxy(NULL,invalid-local-url,zero-options)`拒绝；phase11在LoadLibrary返回后才执行Open、IE、Proxy、Close四条完整工作负载。实际mask0x8f0、12行及全部raw序号/头字段一致。没有把IE读取registry或成功Open放进这四个初始化phase。resolver本身只在host loader外运行；这不证明任意WinHTTP路径或resolver在DllMain安全。

## 故障与永久责任

19个故障case各8次真实typed拒绝、零pair、零Importer/worker。下表由实际initialized Info逐项读取，全部publish0，部分解析slot仍保留且active从未指向未完成resolved表。

| stage | 本次实际事实 | 覆盖性质 |
|---|---|---|
| 1/2/3/4/7 | 不同固定阶段返回失败；阶段、持有flags及0或4解析slot与源码吻合，state3 DisabledNoPublish | 固定stage结果注入，不是自然OS失败 |
| 5×四索引 | 实际查固定不存在名称，错误127；只保此前0..3个已验证地址 | 测试固定错名，不是系统真实导出丢失 |
| 6×四索引 | 固定替换为组件自身函数，独立地址归属检查拒绝，错误487 | 受控self-address，不是任意外部pointer接口 |
| 8 | 全四地址已解析后真实递归Initialize；attempts2/state4 Unknown/flags95/failure2/publish0，active poison | 真实受控重入；不声称自然并发竞争已覆盖 |
| 9 | 固定触发文件identity比较拒绝，错误1006，实际两个文件仍是同一冻结System32身份 | 受控比较失败，不是实测加载了错误系统文件 |
| 10×四索引 | 解析第N名称之前固定失败，只保0..3个prefix slot | 固定stage结果注入，不是OS GetProcAddress自然失败 |

两个normal末尾真实再次Initialize，均state4 Unknown/flags127、publish仍1、attempts2、active poison；原systemModule/heldFile及resolved地址保留，之后四种调用均NULL/FALSE+1114。已完成解析地址的保留不表示还准许新forward。实际worker在repeat之前已join，因此本轮没有实测“旧调用已读resolved表、同时另线程撤销”这一并发窗口；源码允许旧调用完成，仍需单列覆盖。

## 文件身份、历史和证据范围

本次系统文件SHA256为`2509edaea64d4ed9e9993b80fa370f431f37117401ef77cf367533a31b82c180`。parent同句柄测fileid及SHA、native持有系统文件、实际loaded path/fileid、四名称export地址/VM均与该冻结文件匹配。它限定当前Windows文件；旧轮次fileIndex/volume可能不同，不可把文件号当跨重装、复制或更新的永久身份。stage9尤其没有证明另一份WinHTTP能被安全拒绝的全部真实替换时序。

最终production DLL SHA为`0811d17ea57198d69c9794db74b557618ac2940593ef5301604120bd6aa3a912`，fixture DLL为`57f233e49cad6ec35fb6413ae1e986651a330fd62b0db376cd7e7fafb2702f56`。成功source ZIP为`ab5337683eb06df481a5101afadf3f6d4df04b10406d547fe46c431e28817ecc`，与`attempts/r4/source-inputs.zip`逐字节相同；根初始失败ZIP为`a26ba5a8b6f4dacd20b40e68ff068228d48d269c9e4b45313eb239f8bcfbe728`，两者保留且明确不同。归档脚本强制terminal选择本批attempts直接子目录，不会回退读取根command/environment/source ZIP。

失败史未删：初次编译诊断、r2真实CFG fastfail（TLS读取需CRT动态填写的全局Api）、r3真实缺C/C++初始化项导致严格阶段断言失败，均在[history](history)保存。[Importer复核](importer-initializer-review.md)记录最终栈内IAT表与外部具名/INCLUDE保留初始化项；没有通过关闭CFG或减少阶段合同来取得r4通过。

没有发现要求阻止本批归档的确切不一致。网络范围来自固定无PAC/WPAD/请求调用及实际代码记录，未捕获网络trace，不能升级成对整个系统进程“零网络活动”的抓包证明。只有四个当前游戏静态所需名称，不能称完整WinHTTP替代。实际游戏代理首次时机、与Kernel固定早期链合并、生产V4/GC/coordinator/scheduler及取消退休闭包均未完成；本批不授gameReady/EngineCancelled，不勾完B01–B19，也不重复计为全量1617回归。
