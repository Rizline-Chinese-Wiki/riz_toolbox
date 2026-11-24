# 实际 CLR 采用与初始化调用合同

继续完整 B01–B19 兼容底层目标。上一目标轮为 progress：1515 全构建 exit0、474 源/22 包冻结、原生映像实际三处发布和拒绝证据已归档。本轮补真实 producer→CLR 采用以及生产初始化调用函数的动态分支。

用户授权继续代码、多代理、离线验证；“实测稍后”保持。实际固定 GameAssembly 只在独立测试进程以 DONT_RESOLVE_DLL_REFERENCES 映射，验证真实 producer 与生产 managed adopter，不执行游戏 DllMain、init、原函数、IL2CPP 元数据或 listener。MethodInfo/Class 输入只作受控身份，不能冒充真实 IL2CPP 解析或 GC。

初始化调用 fixture 编译实际生产 C 函数，调用受控 native original/bootstrap，核一次调用、真实 int 返回、SEH/C++和拒绝。受控 ready/disabled 不证明实际 pre-init 成功或游戏冷启动时序。自终止故障仅发生在独立 fixture 子进程；禁用错误对话框，不操作桌面或游戏进程。

三代理分别实现 managed 子进程、生产 C 包装及 fixture、独立有限审查；root 唯一 builder，构建期间冻结全部 src/tests/native/scripts。新批完成前不安装、不运行游戏、不修改原文件、不commit/push。B01–B19不扩大勾选，RequestPlay继续关闭。
