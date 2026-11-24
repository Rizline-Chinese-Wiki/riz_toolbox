# 原始 Vector3 缓冲区读写离线验收

2026-11-14。本批已接入主树 904 输入。完整 SDK、B01–B19 和真实 Speed/Seek 仍未完成；本批没有安装或启动游戏，也没有调用 LineInformation.SetPositions。

## 实际改变

[Runtime primitive](../src/RizToolbox.Runtime/NativeRuntime.Vector3Buffer.cs) 直接读取原 rooted Vector3[] 的完整存储，并在完整预期值一致时只写指定前缀。数组身份、别名和未写尾部均保留；恢复采用原始位，保留 NaN payload 与负零。它验证 canonical Vector3/数组类型、rank、12 字节步长、官方 header/长度/object size，以及每次实际访问所需的内存页权限。没有假定原 scratch 容量等于 101。

临时 pinned root 在调用后 guard 之前登记。即使写入已成功而临时根清理随后失败，异常仍携带原 root、完整 before、是否进入 store、已有 completed observation 和未知清理 owner；不自动重试或覆盖第三态。两轮观测不构成原子比较交换，外部 consumer 仍未闭合。

## 验证与证据

| Evidence | Finding | Path |
| --- | --- | --- |
| [新主图原始结果](validation/line-vector3-buffer-runtime-20261114/run/result.json) SHA `bd9867853e1228e020e4c1d60f1f0b11e2e8edbc3e2d31f05d1a09560ff06f99` | 六生产组件 Release 0 警告/0 错误，solution 0 错误；solution 55 项既有测试警告照实保留 | 904 输入新产物，不以旧包代验 |
| [10 项新用例](../tests/RizToolbox.Runtime.Tests/Vector3BufferRegression.cs) 与主图原始日志 | 同物理数组/别名、不同前缀、完整尾部与位恢复；过期预期值、元数据/边界、回调漂移、read-only/execute-only 页、worker、写后 guard、成功/失败后的未知临时根清理均通过 | rooted read/prefix write primitive |
| [默认 Runtime 日志](validation/line-vector3-buffer-runtime-20261114/run/default366.log) | 366/366 自然通过 | 与专项 10 有重叠，分开记录，不相加 |
| [隔离结果](validation/line-vector3-buffer-runtime-20261114/shadow/result.json) | 新图 build W0/E0，buffer10、vector7、single3、ABI51、default366 自然通过 | 仅为正式主图的上游，不代替主图验收 |
| [root 终态观察](validation/line-vector3-buffer-runtime-20261114/run/root-exit.json) | 原 exec session35568 实际 exit0/chunk b942f4 | 没有另称 kernel handle witness |
| [归档摘要](validation/line-vector3-buffer-runtime-20261114/validation-summary.json) SHA `ca67b7f90cb9ca1d3e67803d04c2fb2c250de8c1ad27dd9b918272b1466231f5` | 961 文件、29,545,410 bytes，源码/日志/控制/生产与 Runtime 测试产物/fixture 均归档 | 原子发布 exit0，直接运行归档内 publisher 的 archive-only 复核 exit0 |

首轮控制错误（fixture 目录假设）未进入构建；次轮构建 W0/E0 后因验证脚本只识别英文摘要而停止于测试前。原控制、失败记录照实保留在归档 control-iterations；修脚本后的 shadow-r3 和正式 main-r1 分别自然验收。没有修改失败记录为通过。

归档复核将测试可执行文件、依赖、fixture 和 controls 重新绑定到固定主图结果；校验实际 PASS 名称、root/publisher SHA，拒绝 symlink/reparse point，且不读取原始 C:/D: 运行路径。可在仓库根执行：

```powershell
python docs/validation/line-vector3-buffer-runtime-20261114/control/publish.py --check-only docs/validation/line-vector3-buffer-runtime-20261114
```

## 后续与边界

同一原 LineRingReader/source/endpoint owner 的接入仍是隔离候选：初版五项真实 reader 用例通过，但独立审查发现「只进入 admission、实际未 store 时可能误恢复外部 target」的恢复资格问题，修正版必须以新图重新验证，不能用初版结果验收。下一步先闭合该候选，再接精确 SetPositions(Vector3[],Int32) 与原 saved array/count/plan 观测和 distinct-Line 共享引用测试。

[原 renderer 交接](../work/line-renderer-next-20261114/HANDOFF.md) 继续有效。SetPositions profile/wrapper 候选尚未构建、调用或验收。Ring 初始化、Hold engine、真实 DSP schedule、完整时钟事务仍是后续缺口。旧 881 预览 ZIP 保持原 SHA；904 尚未重新打包。用户 README 原字节保留。
