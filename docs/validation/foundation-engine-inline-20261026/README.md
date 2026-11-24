# Start 内部清理回执

2026-10-26，L 批施工记录。完整底层仍未验收，本批没有安装、启动或操作真实游戏。

这次补的是协程生命周期：同一个 Start 内部同步执行结束时，引擎可能直接清理节点，也可能保留分配但清掉脚本引用。只有空输出指针或返回值不足以确认完成。新增侧接口把 Begin、两种 End 和实际 Start 调用序号关联起来；结束处使用 Begin 的身份副本，不再读取可能已经释放的节点。

## 已有实际证据

| 证据 | 实际结果 | 可以支持的结论 |
|---|---|---|
| 固定游戏文件离线检查 | 三处指令、整段 Start 摘要、跳转目标和异常展开信息吻合 | 固定版本的指令位置与中继合同；没有执行游戏 |
| 独立 Kernel r1 | 严格 C++/MASM 编译及链接退出 0，旧 ABI2 与新增侧 ABI1 分开 | 本机原生组件可构建；不授予生产发布许可 |
| 自有测试程序 r3，cases r1 | 15 个独立进程全部退出 0，共 7137 条断言通过 | 自有指令窗口上的实际调用、机器状态保留、异常展开、缓存身份和异常拒绝路径 |
| Kernel 磁盘核验 r3 | 13032 条检查通过 | 15 个精确导出、7 个导入、151 条 runtime function 及完整探针指令/展开记录；无入口、TLS 或实际 CRT 依赖 |
| C# Runtime r1 | 构建 0 警告/0 错误；协调器 15/15、对象身份 10/10 | 新增 9 组订阅卸载、重入和容量回收；对象身份仍是受控夹具 |
| Audio/content r2 | 完整 718/718，含新增 20 组；构建 0 错误/17 条测试夹具警告 | 实际账本的同代清理条件、回执身份及拒绝路径 |
| Native→CLR 联合 r1 | 4 个独立进程退出 0，189 条托管及 293 条原生断言通过 | 自有 DLL 真实回执通过真实桥、dispatcher、协调器进入实际身份/节点/内联账本 |

15 个场景包括 free、retained、nested、address-reuse、原函数 SEH/C++ 异常、观察者 SEH/C++ 异常、缺 Begin、缺 End、重复 Begin、重复 End、worker、late-attach 和 wrongsite。异常场景通过的含义是保持 Unknown 并保留不确定的资源责任，不是已经完成取消或获得释放权限。

正常 free 场景在 End 之前实际释放自有内存，再验证 End 未读取该节点；retained 场景改掉节点内的身份字段，验证回执使用 Begin 的副本。测试同时核对通用寄存器、16 个 XMM、RFLAGS、方向位、MXCSR/FPCSR、LastError、栈哨兵，以及探针和中继的 Windows 异常展开。机器是否实际启用 CET 另存于原始回执，不把 PE 的 CET 标记当成已启用硬件验证。

原件均保存在构建缓存：

- [固定文件检查](C:/Users/71957/riz-build-cache/engine-inline-contract-r1-20261026/fixed-sites.json)，SHA256 `89657c7b8bbdfec2dc0a1cfba847b74e09fd35c2d8c8b1c118fd69c0caf5c1ee`。
- [Kernel 构建](C:/Users/71957/riz-build-cache/engine-inline-kernel-r1-20261026/kernel-manifest.json)，DLL SHA256 `a0e01ed1619b6c6250665aa29abdd218f2dc8fcdb7283ca3411ce2384d959884`。
- [测试程序构建](C:/Users/71957/riz-build-cache/engine-inline-fixture-r3-20261026/inline-fixture-build.json)。r1 因测试源码一处有符号比较警告被严格编译拒绝，r2 修正后可构建，r3 追加完整调用前后的 LastError 验证；旧目录保留。
- [15 场景结果](C:/Users/71957/riz-build-cache/engine-inline-cases-r1-20261026/fixture-result.json)，SHA256 `fd98dab5e7621a7c90172e1733de9bb97a9989f56bb958d3566ead6be707dff4`。每个场景另存真实 PID、退出码、stdout/stderr、原始回执及 SHA。
- [Kernel 最终核验](C:/Users/71957/riz-build-cache/engine-inline-kernel-r1-20261026/inline-pe-verification-r3.json)，SHA256 `237754bda30ff2a764a0a2ab70412a1cefba7e359c5346b233b1f7c2a5d1ba01`。前两次核验因核验器错误假设被拒绝，原件保留：COFF 的 libcpmt 提示被链接器 /NODEFAULTLIB 禁用，不能算实际 CRT 依赖；MASM 的 SET_FPREG 原始 OpInfo 为帧寄存器 13，不能强制为 0。修正核验器后对实际二进制与展开指令继续逐项核验，原生代码未为此放宽。
- [Runtime 结果](C:/Users/71957/riz-build-cache/engine-inline-runtime-r1-20261026/test-result.json)，SHA256 `27ab8b07f7c1bba9391a3c9f5886a3f34cad41119cc2f12c71b214a7a8b192b8`；315 项源输入前后不变。
- [完整 Audio/content 结果](C:/Users/71957/riz-build-cache/engine-inline-audio-r2-20261026/test-result.json)，SHA256 `da8ad27fcd24e6619309f7880dde4f5b8d0f542fb69467a268bb05e451f3ca7f`；450 项源输入前后不变，454 项源码归档 SHA256 `bb83de5ce11960bf4664da7482c22e5d5c2a72861f0aedb39565d3c0ef845781`。r1 的 717/718 失败记录和 454 项源码也保留，错误是测试先置 Unknown 再假定会接纳新节点；调整测试顺序后，生产拒绝规则未放宽。
- [原生到 CLR 四场景](C:/Users/71957/riz-build-cache/engine-inline-managed-cases-r1-20261026/fixture-result.json)，SHA256 `c7eab4d2ccd00416e4f227148f738c29fc32794864c2cfc835071e0caed6a4a3`。free/retained/nested/address-reuse 的实际 PID 为 35132/51516/50260/46712；全部退出 0，每项保留完整 JSON、输入摘要与日志。

## 已接通的责任链与剩余工作

C# 桥、实际 Start 私有身份、节点代次账本和公共协调器已接线并验证。订阅在 typed Enter 的任何 capture 之前冻结，侧回执只能使用同一真实 Start binding；回调中退订会撤回未来 Start，当前 Start 原函数内的 Execute/Release/Detach 仍由旧订阅接收，直到实际 Complete/Dispose 才撤四通道及剩余闭包。缺侧观察能力在分配本局根之前拒绝，不影响四通道通用观察。

联合夹具确实执行四个配置通道中的 Start/Execute 和三处自有指令窗口，通过真实 native callbacks 进入 CLR；Release/Detach 在这些正常内联场景未调用。tracker provider 仍为受控身份标签，没有执行真实 Unity 对象读取、GC、tracker backend 或 factory root 释放。retained 只证明自有节点字段变化后仍用缓存关联，不能代替真实 Unity 脚本析构语义；地址复用场景使用两个独立 tracker 身份，不宣称同 tracker slot 的联合复用证据。

仍有两个已知保留边界：原 typed Complete/Dispose 回调内部再触发子调用时，退休订阅的继承尚需独立 closing 身份支持；异常 side Exit 的逻辑 context 可永久保留 Outstanding，避免错误完成。当前正向证据不覆盖这两个边界，也不把 Unknown 当作资源已释放。

仍需完成生产侧三探针发表合同和启动窗口证明，再补 scheduler/wait、取消完成、全消费者最终结束与剩余资源释放。当前 InlineInfo 不返回三处 site 配置，Configured bridge 的正常联合检查不能充当生产发表凭据。生产 admission、RequestPlay 和 EngineCancelled 继续关闭。上一个 r5 候选没有包含本批代码。

下一批的[具体 scheduler/wait 施工表](next-scheduler-wait.md)已落盘，SHA256 `724b76d738fa50c1b22171d7b288db602515bfc1010b3406610fd03bd927726c`：普通 wait 派发已定位到固定 `5A5260..5A5343`，scheduler 需要覆盖登记、消费、删除和整体销毁的逐行责任。该稿是新增静态研究，不计入本批运行断言，也没有发放生产权限。

从证据到后续工作的关系是：离线指令证据定位两个清理分支，自有进程证明探针传输和中继行为，托管关联再决定同一真实 Start 的节点代次能否确认清理；这些条件不能替代整个引擎消费者已结束的证明。

[一页进度](../../../.docs/PROGRESS.md) · [正式阶段报告](../../2026-10-26_底层-Start内联清理与真实回执-report.md) · [完整验收清单](../../../.docs/foundation-expansion/CHECKLIST.md)
