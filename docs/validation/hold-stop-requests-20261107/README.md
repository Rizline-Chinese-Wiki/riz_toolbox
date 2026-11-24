# Hold 停止请求接线：2026-11-07

六类 Stop 的请求观察已接入 `JudgementModule` 和 `NativeJudgementBackend`。最终源码离线构建成功，六组件与判定测试项目零警告、零错误；判定套件 **70/70**，通用取消记录回归 **26/26**。AudioContent 测试项目有 27 条测试源码编译警告（24 条默认字段、3 条可空引用），零错误，原日志保留，未隐藏警告。具体文件哈希和日志见 [证据索引](evidence-index.json) 与 [构建结果](build-test-result.json)。

## 此次完成

- 观察 Enumerator、Coroutine handle、StopAll、两个 managed wrapper 和 named Stop 的实际原调用窗口。正式模块使用现有 pre-init channel，六个观察通道先于 Hold Start/Move 接线；缺失 bootstrap 时单独报告 StopUnavailable。
- 每个 run/receiver 拥有独立 identity provider。保留 iterator、Start 返回 handle、slot/generation 和对象引用身份；相同地址的新局不能借用旧局身份。
- 原调用开始前取得作用域引用；暂停、跨局、嵌套调用、逻辑解绑和异常结束仍完成原请求的清理责任。请求记录在 run 退休后保留，已知 native roots 可正常释放，不伪造引擎完成。
- 完成异常归属复核：旧 Stop 内换局并抛异常时，broker fault 只影响原精确调用，不广播到新局。即使取得作用域失败、尚未登记请求，也保留原 provider 的 Unknown/Pending 责任。
- Hold 请求使用上限 65536 的通用记录能力，实际测试调度 257 个不同 iterator/handle 后执行 StopAll，确认无旧 256 上限截断。其他调用方仍显式使用其既有容量。

## 验证范围

新增 9 组 Hold Stop 测试，原判定 61 组保持；另外独立执行原通用取消及引擎关联的 26 组测试。覆盖六入口、错误引用、foreign receiver、嵌套 physical mask、同代暂停、换局、异常/跳过/解绑、worker 与错误 MethodInfo 的零 native 读取、4096 个作用域耗尽、257 个对象及伪造同值 owner。

所有测试都直接执行 `hold-stops-r2` 新构建目录中的 DLL。构建前后输入哈希相同，测试引用的 SDK/Core/Runtime/Adapter DLL 和 PDB 与本次六组件产物逐字节一致。未使用旧 C/bin 或交付 ZIP 代验。

第一轮 r1 的 69/69 与 26/26 实际通过，但独立复核随后找到了旧局异常 fault 广播问题。r1 原结果保留，只作为历史；新增回归并修复后的 r2 才是当前版本验收。

## 尚未完成

这次使用受控 native bridge 和合成 IL2CPP 导出验证实际 broker/backend 组合，未启动 Unity 或真实游戏。正式 bootstrap 的实机采用尚未验证。Stop 请求返回不等于 engine cancellation，named Stop 不猜测具体 iterator，所有这些测试的 `EngineCancelled` 均保持 false。

Hold 专属引擎 consumer、managed/native owner、source closure、Detach 最终清理及调度退出证明仍缺失；`ProductionStartupAdmissionKnown` 和 `FullCoverageKnown` 仍关闭。完整 Speed/Seek 与 B01–B19 未完成。下一可独立离线实现的是 Judge 队列及输入集合写入，依赖见 [Seek 组合计划](../../../.docs/foundation-expansion/NEXT-SEEK-COMPOSITION.md)。

今日已经交付的 SDK/runtime 开发预览 ZIP 保持冻结，本轮新源码未放入那个旧包。未安装/启动游戏，未操作游戏 UI 或账号。
