# InsideLevelParams 标量在 stock LevelStart 中的来源

2026-10-22，仅读取既存静态证据；未提取新二进制范围、修改源码、构建或操作游戏。`runtimeAcceptance=false`：本记录不把本批参数构造探针当作 controller/LevelStart 实测。

**结论：stock `NormalModeInLevelController.GetInsideLevelParams` 会在返回本次参数前覆盖 `speed` 和 `offset`。`speed` 来自 `SettingsControl.get_MappedSpeed()`，`offset` 来自该设置的静态 `offset` 值除以 `1000.0`。构造探针读到 `speed=0` 不表示启动前必须改成 `1`。**

## Evidence → Finding

| 证据 | 既存位置与锚点 | 可证明的关系 |
|---|---|---|
| E-fields | [metadata-excerpts.txt](metadata-excerpts.txt)，dump 91686–91695、91785–91796 | `InLevelController.LevelParams` 在实例 `+20`；`InsideLevelParams.speed` 是 `Single +38`、`offset` 是 `Single +3C`；构造函数只接受五个引用参数 |
| E-entry | [前序 disassembly.txt](../local-metadata-bridge-20261022/disassembly.txt)，`GetInsideLevelParams@3BEDF0..3C0310` | `3BEE07` 将 receiver 保存至 RDI；`3BEF51` 保存实际 LevelInformation；后续的 `[rdi+20]` 是该 controller 的既有 LevelParams |
| E-speed | 同一范围 `3BF0B2..3BF0DB` | `3BF0B9 mov rbx,[rdi+20]`，`3BF0CD call SettingsControl.get_MappedSpeed@390A80`，`3BF0DB movss [rbx+38],xmm0`。并非读取构造值后相乘，也没有把常数1写入参数 |
| E-offset | 同一范围 `3BF0E7..3BF13C`；[boundaries.json](../local-metadata-bridge-20261022/boundaries.json) | 再取 `[rdi+20]`，从同一设置类型的静态存储 `+14` 读出 Single；`3BF134 divss` 的 `ripRelativeRva` 是 `17C4C50`；`3BF13C movss [rbx+3C],xmm0` |
| E-setting | 既存 dump.cs 的 SettingsControl 声明 92505 起（本次只读）；同一静态设置类型指针 RVA `1E514A8` 被上述代码复用 | SettingsControl 静态 `offset` 位于 `+14`，公开 getter 名为 `get_OffsetInMillisecond@390B90`。字段名/单位声明辅助解释来源，单位转换的数值另由 E-constant 支撑 |
| E-constant | [gameplay static-proof.json](../gameplay-semantics-20261020/static-proof.json)，`constants.deltaDisplayMultiplier` 与 `chartDelayMsDivisor` | 同固定游戏 SHA-256 下，已有两处指令的数据源 `17C4C50` 均记录浮点值 `1000.0`；复用该已提取常量，没有新读二进制 |
| E-return | 前序同一范围 `3C02CF` | `mov rax,[rdi+20]` 后返回：两个标量写入的是最终返回的同一 LevelParams，而非另一个临时构造对象 |
| E-launch | [launch README](README.md)、[launch disassembly](disassembly.txt)，Awake 六回调与参数 setter | GetInsideLevelParams 是局内 LevelStart 消费入口；`SetInsideLevelParams@36B0E0` 只是保存引用，两者不能混为一谈 |

F-overwrite，status=confirmed-static，confidence=high，evidence_ids=[E-fields,E-entry,E-speed,E-offset,E-return]：在该固定构建的此 stock 正常返回路径，参数字段 `speed` 被 get_MappedSpeed 的返回值覆盖，偏移被当前设置换算值覆盖。不能从字段名推断它控制音频播放速率；该值的具体 note-scroll/playback 语义与映射公式没有在这里验收。进入该函数前的 UI/对象前置依赖若失败，会进入其异常路径；不能仅凭两条赋值保证实际 LOCAL 关卡能启动。

F-conversion，status=confirmed-static，confidence=high，evidence_ids=[E-offset,E-setting,E-constant]：该偏移赋值为设置 `offset / 1000.0f`，不是构造参数的旧 offset。这里没有另外证明局内后续所有时间源、谱面延迟和音频校准的完整单位；不能把这一条转换扩大成全链路时间验收，尤其不构成 B11 判定 delta 单位的标定证据。

## 下一批的执行含义

保留参数工厂当前“只构造五引用并观察标量默认值”的责任；不要为 scratch 参数读到零增加未经证实的 `speed=1` 写入。inactive-parent clone 探针应先验证 clone/Awake 的时序和全局责任，真正接入 stock LevelStart 后再记录同一参数实例进入/返回时的标量，以及当时 `SettingsControl.get_MappedSpeed()` 和 offset 设置。该函数本身会启动延迟/等待协程、操作触摸阻挡和 UI；不能为了读两个值把它当无副作用 getter 调用。

现有范围足够判定覆盖行为，不缺新的二进制范围。**仅当需要解释 MappedSpeed 的具体映射公式时**，当前证据缺 `SettingsControl.get_MappedSpeed@390A80` 函数体；最小候选补充范围为 `390A80..390AF0`（后一个已命名入口是 get_MasterVolume）。本次没有提取，也不推断该映射恒等于用户原始 speed 或固定1。

## 固定输入

游戏 SHA-256：`853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。

- metadata bridge disassembly SHA-256：`baeedffbee2a550ef1b819c6dc1a3d54887c52ef101034ef4fc293118b40f867`。
- launch disassembly SHA-256：`450d4ff3e1b56f8b91a02dad66053173ff255d188072d46d2bfd4498d759aed4`。
- launch metadata-excerpts SHA-256：`c4831cfa66cc07c7891e37d24a1fd05dd499f544121fffa42a958320461f635e`。
- gameplay static-proof SHA-256：`3c444ed13f998e28426c8fe8faddb468f5b683ec69d42be15c818088b5b4ca9b`。

复查只需阅读上述已有文件。前序 `verify-static-claims.py` 已包含两个写入与最终返回锚点，但本次没有重跑其二进制提取或宣称新实机通过。
