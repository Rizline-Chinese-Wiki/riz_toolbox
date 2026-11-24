# 目标时间状态策略：2026-11-08

`RunValue.CaptureSeekTargetStatePlan` 从独立保留的实际音符、谱面/BPM、Judge 缓存和当前初始化队列，计算指定进度秒数下的新练习状态。输出直接对接已有 Judge 队列、flags、fingerIds、renderer 和 cursor 部件。规划只读，不回放历史判定或计分事件。

新 DLL **156/156 判定测试（新增 20 组），51/51 Runtime 回归**通过。六组件/判定/Runtime 构建警告依次 0/0/1，错误均 0；660 项输入前后相同，两个测试目录 16 份依赖 DLL/PDB 与本次六组件一致。见[构建结果](build-test-result.json)、[源码复核](source-review.json)和[证据索引](evidence-index.json)。

精确实现原生嵌套交换排序、float 窗口和严格比较、Tap/Drag 迁移与过期判断；区分初始化前后窗口、真实当前初始化 occurrence、挑战标记和计数。保留同谱面位置的多实例，缺失实例明确报告。跨目标 Hold 区分头判定窗口、严格尾边界和显式 Skip 策略，均不签引擎退出。正式入口测试还把自动输出交给官方队列写入，验证提交、确认及完整恢复。

固定文件离线核验覆盖 5 个完整方法体（23,936 字节）、74 个字段和 2 个常量；两时间转换的实际 MethodInfo/entry 均重核。受控导出测试不等于真实游戏验收。Hold 两 List/目标几何、输入与引擎退出、renderer 延后 consumer/source closure、初始化状态实际写入及完整 Seek 事务仍待补齐。真实 Speed/Seek 关闭，B01–B19 未完成。

失败记录保留：r1 是测试夹具方法名错误；r2 新增 20 组全过、总计 155/156，旧 renderer alias 断言误把按地址排序的行当作夹具顺序。最终仅修夹具调用及按准确对象引用取行，原数量/共享身份断言保持，未改 renderer 产品源码。

11 月 7 日预览 ZIP 原 SHA 保持，本批源码未入旧包。旧 typed 五例 r2 仍按重启中断未通过记录；未安装/启动游戏或操作 UI/账号。阶段双采样不是原子 CAS，不能排除未观察的外部 ABA。
