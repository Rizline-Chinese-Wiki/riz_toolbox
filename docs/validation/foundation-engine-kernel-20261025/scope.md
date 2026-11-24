# 独立协程运行核的实现与验证范围

本批承接用户的完整 PC Mod 兼容底层目标，以及“先继续代码，实测稍后”的安排。root 唯一构建和执行；三个代理分别实现 canonical 常量入口表、独立入口描述符宿主、父 runner/离线 PE 验证并交叉核原件。只编译和执行自有 fixture，不启动或安装游戏，不操作 QQ/Steam/UI，不 commit/push。

`RizToolbox.EngineNodeKernel.dll`直接复用现有 EngineNodeContext.cpp/h，限定8个 ABI2 export、record V1、32永久槽以及四类 typed ABI。四表改为 typed constexpr 是编译期保证；此前 MSVC 已实际折叠旧代码，不能编造之前发生了动态初始化崩溃。新DLL无入口/TLS/CRT/Dobby，链接禁止默认库；只保留实际必要的系统 SEH 与 fail-fast 导入。它与旧59-export JudgeContext并存，不能以重命名或混用状态/epoch替代身份验证。

独立入口 fixture 只读取全部128入口和Fresh信息、检验拒绝参数，不调用未配置的typed入口；父按实际DLL磁盘独立核只读4×32指针及128项DIR64。模块的mapped header字段与磁盘preferred ImageBase分开，地址换算使用实际module.base；不把重定位后的头字段冒充磁盘值。原生136调用/观察异常与五组真实 native→CLR→dispatcher/ledger由既有测试复用，Dobby仅存在于测试出版目标一侧，不成为新Kernel依赖。

新Kernel纳入统一构建和声明包，完整托管25套及现有原生检查仍须实际跑完。旧完整576源码的4项会有意变化：canonical cpp、Build.ps1、两个旧runner的可选绝对HelperPath参数；旧归档原件不改，新批冻结前后hash并重新归档。上批73早期链的14输入和独立57组件的12输入保持历史各自身份，不用它们假装新Kernel已完成早期绑定。

本批没有实现实际游戏bootstrap/系统WinHTTP执行/V4采用，没有证明实际IL2CPP对象与GC、coordinator、scheduler/async/Start内联清理及所有消费者退休。EngineCancelled=false、Pending保留、RequestPlay关闭。完整B01–B19继续，只有已具备各自证据的项目才能勾选。
