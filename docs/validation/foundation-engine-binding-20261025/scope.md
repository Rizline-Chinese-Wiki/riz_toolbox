# 引擎返回 ABI 与协程身份关联

2026-10-25。继续完整 B01–B19。上批冻结构建为 1597/1597；本批源码已有新增和修正，不能将旧结果当作当前工作树已验证。

固定 UnityPlayer 调度器对 Execute 返回值比较完整 EAX，本批修正此前只保留 AL 的通道，追加返回 ABI 版本 2 拒绝新桥采用旧 helper。Start/Release 仍保留 AL，Detach 为 void；标量记录布局保持版本 1。节点账本独立关联既有三类 tracker 的 receiver、slot、generation、Enumerator、延后返回的 Coroutine handle；实际 native owner 与 managed receiver 分开。Runtime 通过已保留的 receiver root 和正式 Object.m_CachedPtr 字段元数据读取 owner，不能解引用已释放的 node。

root 是唯一 builder。允许本地代码、自有 native/CLR 子进程、固定游戏映像的只读机器码研究和受控元数据测试。用户“先继续代码，实测稍后”保持；不安装、不启动/操作/终止游戏、不修改原游戏文件、不 commit/push。

没有用当前 GameAssembly 初始化前时机代替 UnityPlayer 入口安全首次发布证据。scheduler pump、异步等待旁路和 Start 内联清理的实际责任仍需全部接线；本批不得将 Stop 返回、Release AL=0 或节点暂无执行变为 EngineCancelled，不清 Pending、不授资源释放或 RequestPlay 权限。只有正式终态、当前输入一致性和归档完成后才登记新包通过。
