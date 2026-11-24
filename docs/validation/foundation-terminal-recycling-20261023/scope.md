# 本批范围

本批只补已自然结束的 Cutout/Fade 观察根回收，并在 Ready 帧泵调用；初始化两协程已有的完整结束清理也接到同一帧泵。16 条限制改为尚未退休记录的同时上限，32 个生产 owner 配额不变。

记录先撤 Published 身份，保留原 enum/代次用于识别迟到回调，再读真实 native activity；非零等待后帧，未知故障保责任。独立 enum/Coroutine 根每个只尝试一次 free，确认后才能复用记录。故障期间即使根已成功释放，记录也不得报告退休成功。累计计数与 required Cutout 结束事实独立于可复用记录保存。

新增故障用例使用受控 backend；没有直接向生产 NativeLocalTerminalCoroutineBackend 的真实 IL2CPP gchandle free 注入异常。既有真实 Dobby/native fixture 继续证明其原有 transport 范围，不扩为本批新 backend 的游戏验收。Reset 两函数只抛异常的结论是固定二进制静态证据，未执行 Reset，也不是停止消费者的操作。

未来 producer 仍被观察；ObserveEnded 和本批根回收均不签 whole-run consumers-ended receipt。controller/GO 与音频实际失效、全部 UnityEvent/外置 callback 关闭、globals/menu 条件恢复、活动图/包及协调器退休、完整 Retry/Return/Loader 组合仍缺。B01–B19 目标不缩小，只有 B03 整项及 B18a 子项勾选。

用户“先继续代码，实测稍后”保持。本批不安装、不启动或停止游戏、不操作 UI/游戏输入、不修改原游戏文件、不 commit/push。
