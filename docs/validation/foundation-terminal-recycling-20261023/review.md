# 审查记录

root 核对了前一轮实际源文件和测试注册，发现首轮类型别名与全局 Fixture 冲突后修正。后续七组新增测试故意保留未知责任，累计超出原生产 32-owner 配额；采用既有 IsolatedAudioRegression 子进程隔离这些用例，没有扩大配额、清静态 owner 或绕过 production 权限。第一次编译错误和 489/490 失败日志保留，最终定向见 [490/490](audio-targeted-confirmed.log)。

activation_finish 子代理独立只读检查先撤身份/redrain、worker/reentry、根释放、代次和地址复用。root 发现末次 activity 读取期间 fault 的成功误报后，子代理确认并指出生产 backend 释放前同类窗口，以及 CaptureCore/unused detach 的读内故障一致性问题。修正为先读取 activity，再复核 sticky fault；backend Known 也包含 observation fault。子代理复核修正后未发现剩余确定问题。它未编辑、构建或操作游戏；这是有限代码审查，不是独立构建 runner。

settings_finish 子代理独立核对 pinned PE 的 Cutout Reset@3C5A00、Fade Reset@3C5C40：各64字节，调用 NotSupportedException ctor@F1B1C0 后进入 raise 路径@264FB0，无 enum state/current/controller 写入。root 的 [12范围/991指令与6个直接call候选核对](terminal-evidence-check.json)另冻结并重新核对全部范围、逐指令字节和三输入哈希。

never_active_retirement_code 只读核对实际 bridge 为 NativeLocalRunTerminationBoundaryState，并给出下一步 active 失效/未来来源关闭顺序；没有把 Cutout 自然结束当作整局结束，也没有新增拼 bool 的 verifier。后续要求见 [下一步](next-integration.md)。前一轮属于实际源码和测试的进展，本轮沿当前工作树完成收尾，没有重新冻结历史包。

生产 root free 故障仍无直接注入 fixture，controller/音频实际销毁和整局退休尚无本批游戏证据。所有检查数量都不得换算为 B01–B19 完成度。
