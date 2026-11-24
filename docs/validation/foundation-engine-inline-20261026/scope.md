# L 批范围

用户目标是 PC Mod 公共兼容底层，完整要求以 B01–B19 为准；本批承接协程生命周期与资源退休，不代表整体完成。用户允许继续多代理代码工作，游戏实测仍依“先继续代码，实测稍后”暂缓。

允许的对象是桌面 riz_toolbox 源码、C:/Users/71957/riz-build-cache 下的新构建与证据，以及 D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll 的离线只读分析。原生执行仅发生在本项目自有进程和自有 DLL 内，三处写入窗口属于自有测试程序；没有修改、安装、加载或操作真实游戏。

本批交付是独立侧 ABI1、真实 Start 作用域与内联 Begin/End 回执、C# 精确调用及节点代次关联、订阅撤销后的既有调用责任保留，以及这些行为的磁盘、原生和托管证据。原 ABI2 保持。指令副本与回执不授予生产发布、启动窗口、取消完成、scheduler/wait 关闭或全消费者退休许可。

纯 C# 回归的桥和身份 provider 为受控对象；原生到 CLR 联合夹具通过真实 Kernel、bridge 和 dispatcher，但 tracker provider 仍是受控标量。不得把两者称为 Unity 真实对象/GC 或游戏调度端到端测试。Unknown 继续保留未确认资源，异常输出和失败版本原件均保留。

上一个 r5 候选包不包含本批源码。本批没有推送、提交或发布仓库改动。
