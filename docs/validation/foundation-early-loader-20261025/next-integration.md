# 下一段：固定四入口的最小发布器

2026-10-25。自有依赖加载机制和四入口通道已有可执行验证。当前仍不能把固定Dobby直接带进游戏的loader阶段：[实际依赖审查](dobby-loader-audit.md)给出了CRT堆、lazy singleton、日志/迟到加载候选和未解虚调用。本页定义后续实现，未宣称最小发布器已完成；完整[B01–B19](../../../.docs/foundation-expansion/SPEC.md)范围保持。

## 交付最小native组件

新增固定四项POD描述和永久责任记录，替代Dobby在这条早期路径上的动态list、operator new、泛用routefactory及stdio日志。组件使用显式常量/零初始化的存储，只记录定长数值事件；源码、实际PE导入、机器码调用闭包及全部错误分支一起核验。现有GameAssembly V3路径独立保留，不以本组件的自有成功结果扩大它的授权范围。

每项描述明确模块身份、完整函数边界、入口前缀、指令截取长度、修改/重放计划、四种精确ABI、永久slot和epoch。对固定UnityPlayer真实非NOP前缀离线生成重放stub，不在loader内引入通用指令解码器、近址分配循环、动态虚表或未经核验的间接回调。固定预留stub如何得到正确地址、容量、RIP相对修正、返回跳转及可执行保护，必须由实际字节与运行证明；不能用本批32字节NOP目标代替。

先准备全部original/stub并核验，再对全部通道不可变Configure，最后逐项发布。只处理当前自有进程内已核验的固定范围，显式记录保护、写尝试、实际写入、回读和缓存处理；不走打开进程或动态fallback。任一写尝试后失败保持Unknown并有限退出，不重试、不假卸载、不放行Importer。未写入但已Prepare/Configure的永久责任也必须保留，DisabledNoWrite只表示源entry没有写入。

## 在自有宿主验证真实指令与失败

把最小发布器接入本批Host/Importer/Proxy形态，移除这条被测路径对Dobby的静态和运行依赖。保留真实TLS、C/C++ initializer及DllMain首次调用、完整Execute EAX、Start嵌套、加载后Attach、worker和原生异常。新增自有非NOP目标，包含与固定游戏入口相同类别的栈/寄存器保存、RIP相对访问及需要重定位的分支；证明original恰好一次、实参及声明返回位不变，实际unwind/SEH/C++沿正确路径返回。需要新unwind记录时单独核验其注册、生命周期和失败，不假定复制机器码已自动满足。

故障矩阵除了每个公开步骤前后，还要覆盖最小组件内部实际拒绝、保护设置失败、写入尝试/中断、回读不一致及恢复保护失败；每次终态与实际已产生的责任对应。由父进程独立核对真实原件，避免用mode标签代替被制造的条件。错模块、缺依赖、ABI不兼容和早期调用证据分别构造；本批受控expected/selector案例只保留其已有范围，不改名为实际坏模块矩阵。

## 再做生产发布和对象接线

自有最小组件通过后，才核真实系统WinHTTP转发、依赖唯一名称和放置、固定模块/hash/前缀、游戏最早启动点及历史线程/调用。生产独立V4或等价凭据必须包含真实发布的每行tuple和终态，CLR只读核验并Attach，不在迟到时补装。

随后把已实现的receiver root→native owner投影及tracker/engine独立代次账本接入生产coordinator，验证实际IL2CPP对象、GC和场景变化。继续补[scheduler、async与Start内联清理](../foundation-engine-binding-20261025/scheduler-cleanup-research.md)、全部writer/future/消费者责任和资源退休，才能形成取消完成凭据。最后以冷启动、暂停、正常/失败结算、重试、返回菜单、再次开局及Loader重建逐项验收。

在这些条件完成前，`EngineCancelled=false`、Pending保留、`RequestPlay`关闭；正式验收仍仅B03整项和B18a子项。游戏实测沿用用户“实测稍后”的安排，本批不安装新包或操作游戏。
