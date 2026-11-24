# 返回原语之后的接线

2026-10-23。以下是后续实现顺序，不是已完成或已实机确认的能力。原 B01–B19 范围继续。

1. **菜单对象和锁的连续所有权。** 在原生启动前捕获 GameManager 的 mainCamera/mainParticle 所属 GameObject；借助新 Boolean 参数 Hook 连续观察精确 SetActive。只恢复本局确实改变、且期间未被其他调用接管的状态。对 normalLock/BackLock 观察 MultiLock.Lock/Unlock/UnlockAll 的成对实际调用；仅比较 count 无法排除“先释放再重新加锁”的情况，不能盲目 UnlockAll、改 count 或吞别人的锁。
2. **native Judge 在途补全。** 现有保护门、Progress/初始化/Timeline 活动检查并不覆盖全部 native Judge 窗口；RuntimeNoteRegistry 的 managed lease 在途也不能替代。为真实 Judge bridge 增加可观察的完整原生边界活动，在旧角色分类撤回后再次 drain。
3. **活动 controller 退休。** 从本局真实初始化图捕获独立观测根，确认真实返回完成和原 Level 卸载；全局委托只条件恢复本局仍拥有的槽。只停止本局独占 controller 的协程、禁用并销毁其对象，再跨后续帧观察完整实际图 Unity 失效。不得复用 never-activated 路径，不得全局 ClearDontDestroy。
4. **所有消费者结束与资源释放。** 组合 Judge/其他 native 在途、UI 回调真实退出、菜单账本、旧回调、globals、controller/音频等证据后，才由实际 verifier 提供 registry retirement proof；先撤分类、再 drain、再独立 free。未知责任继续占原配额，不作重试或成功回滚假设。
5. **命令与组合根。** Return 调用 Registry.BeginReturn、实际返回 owner 和完整退休器；Retry 先退休旧局，再按同一管线创建新 bundle、Chart、run generation。最后在 Loader/模块装配保护宿主、bridge、handler、sink、coordinator 和命令，统一开放能力状态，完善失败撤回。

静态研究候选包括 GameObject.SetActive `0x1538AB0`、MonoBehaviour.StopAllCoroutines `0x153D6C0`、Object.Destroy `0x153E680`、op_Implicit `0x15409B0`，以及 MultiLock.count `+0x10` / Lock `0x38B2D0` / Unlock `0x38B350` / UnlockAll `0x38B310`。这些来自现有逆向资料和先前子代理研究，**本文件没有独立核对全部 PE 字节**；进入新生产绑定前必须逐项核验构建、声明方法、ABI 和实际根对象。

现有目标场景和 AsyncOperation receipt 不证明上述步骤已经完成。结果 sink 的类型化 Retry/Return 回调也不等于已有完整命令处理器。
