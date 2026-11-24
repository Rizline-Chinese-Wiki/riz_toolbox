# Canvas 缓存显式同步合同

本批复用现有 `ISeekCanvasNativeProfile` 与冻结的 `NativeSeekCanvasNativeProfile`。该 profile 已覆盖五个缓存字段和完整 Canvas producer/consumer body；无需新增重复 profile。静态证据只固定来源，实际写权限、当前数组身份和 fresh ABI 由 Runtime/backend 独立核对。范围见 [scope.md](scope.md)。

| 当前精确 InLevel.CanvasControl 字段 | 类型/偏移 | 目标 | 访问要求 |
|---|---|---|---|
| floorPositions | System.Single[] / 0x38 | Plan.Rows[ordinal].FloorPosition | 当前原引用独立 pin；只写完整实际元素 |
| floorSpeeds | System.Single[] / 0x40 | Plan.Rows[ordinal].FloorSpeed | 不额外乘 Request.Speed；只写完整实际元素 |
| xPositions | System.Single[] / 0x48 | Plan.Rows[ordinal].XPosition | 已包含 captured screenWidth；只写完整实际元素 |
| nowTime | System.Single / 0x50 | Plan.Request.ChartTime | fresh 精确 mutable instance 字段 |
| speed | System.Single / 0x54 | Plan.Request.Speed | fresh 精确 mutable instance 字段 |

五字段均要求精确 declaring native class、类型、非 static、非 initonly 及相同 offset。source profile 的 read descriptor 不构成 mutable scalar write descriptor；三个数组字段不经 Reference 写入或替换。既有 profile 仍逐次验证类、完整字段描述、MethodInfo、模块和全部完整 body/constants。

GetInformations 按实际 Transform[] 的 length 和 ordinal 遍历；CanvasMove.index 不做 remap。其 native stores 为 floorSpeeds@0x3e37da、floorPositions@0x3e3838、xPositions@0x3e3a33。SetPosition 读取 floorPositions*speed 与 xPositions，随后调用 Unity setter。Update 在 GetInformations 前复制 nowTime，而未替代显式 speed 同步。静态语义不授权调用这些 producer；显式写入只消费同一个已捕获不可变 Plan。

准备阶段必须独立持有实际源及三个当前 Single[]，完整核官方 array rank/element/size/header/length/byteLength 与 Single 4-byte ABI。ABI 不能只读热缓存；callback-capable export 后应核 lifetime、source publication、最新 descriptor 与原引用。所有计划行、实际槽、空数组和重复身份保持完整覆盖。原始值保留 raw Single bits，允许 NaN 和 -0，目标保持有限。

任何写入前按物理 (array,index) 合并别名；多个目标位相等只写一次，冲突零写拒绝。每次 typed write 进入前记录 attempt，最后核 expected raw bits 并做有界条件写；回调撤销必须阻止未进入的 dispatch。写后完整读回当前源和缓存。条件恢复只触及尝试过且仍是该命令原值/目标位的槽与标量；第三态及未尝试槽外部修改保持 sticky，不能为制造回滚而覆盖。

Root/pin 责任必须在原生发布与后续验证前登记，精确 owner 对失败保持可观察。未知 free 不能重试；release/callback 失败不能抹去 pending 责任。worker、同步 reentry、准备期撤销、返回后撤销和 source/publication 当前性都需要独立阻断。缓存恢复不能清除已有位置命令的 Unity setter 外部效果或其原 owner。完整 Seek 与生产准入继续关闭。

复现静态组合检查：

```powershell
python work/seek-canvas-cache-native-20261112/verify-cache-contract.py
```

脚本只读取仓库冻结文件与其 JSON 中已有机器码，逐字节重建并核完整 span，未打开 GameAssembly、未 map DLL、未调用游戏方法。产物为 `cache-static-evidence.json`、`cache-contract-verification.json`，它们不代表 live ABI、实际游戏缓存或 cleanup 测试通过。
