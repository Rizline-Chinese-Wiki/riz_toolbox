# LOCAL metadata bridge 静态结论

现有普通 `LevelMetadata(Level, LevelDifference)` 可以继续实现为独立 LOCAL 的原生 metadata 兼容桥候选，当前不需要先实现新的 IL2CPP 接口类型注入。它的九个 getter 在本次固定构建的机器码里依赖自身对象图、已编译字面量和字符串操作，没有读取 LevelControl 目录或资产加载器。**这个结论只覆盖 metadata 构造/读取：原版结算会内联写成绩并调用 Steam 成就/统计，原版重试会按游戏资产 ID 重新加载，不能直接放行完整播放。**

本目录包含 27 个有界函数/方法范围及 44 个关键指令锚点。原生对象构造、getter 的真实 runtime_invoke、接口 assignability、局内交接与释放仍未验收。

## 范围与工具

见 [scope.md](scope.md)、[timeline.md](timeline.md)。样本为本机 `GameAssembly.dll`，SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，PE x64，CLR directory 为 0。复用了前序 Python/pefile/capstone 有界提取方法；IDA/r2 在工具索引中不可用，没有启动或安装它们。报告 flavor=null，普通兼容性逆向，不是漏洞或恶意软件报告。

[triage.json](triage.json) 先于函数解释写出，含完整静态 imports、exports、分类与工具版本。导入类别包括文件/目录、进程线程/内存、动态加载、网络、注册表及加密匹配；分类是原始 import 名称上的有界文本分类，不把导入存在当作某一函数的行为证据，也不把干净分类当作排除动态加载。DLL exports 同时保存。

## 已确认的对象依赖

下表将机器指令与 [metadata-excerpts.txt](metadata-excerpts.txt) 的字段定义交叉核对。所有偏移只用于解释固定样本的静态证据；实现应走导出元数据 API 的类型验证及字段读写，不能把本表当通用偏移写入接口。

| 方法 | 固定样本的字段路径与行为 | 范围内的外部依赖 |
|---|---|---|
| `LevelMetadata.ctor@377430` | 保存 `Level` 到 +10、`LevelDifference` 到 +18；两次写屏障 | 基类空构造/写屏障；没有目录/存档查找 |
| `get_Artist@377480` | `metadata.level.musicInfo.artist` | 无 |
| `get_MusicName@3777E0` | `metadata.level.musicInfo.musicName` | 无 |
| `get_Charter@3774B0` | `metadata.chartInfo.chartInfo.designer` | 无 |
| `get_LevelInternalName@377660` | `metadata.chartInfo.chartInfo.level` | 无 |
| `get_LevelDisplayName@377510` | 对 `ChartInfo.level` 比较四个固定字符串；非空未知名称原样返回，null 走异常分支 | 字符串常量/相等比较，不是目录查找 |
| `get_Lv@377690` | 普通 Level 读 `ChartInfo.difficultyText`，空则由 difficulty 格式化后缓存；另有 DiscO 子类分支 | 字符串/数值格式化；LOCAL 构造应精确使用普通 Level |
| `get_Status@377810` | 读 `LevelDifference.bestScore` 的 isFullCombo/isClear；null bestScore 返回 0 | 无存档读取 |
| `get_ThemeColor@377860` | `metadata.level.musicInfo.themeUiColor`，Color 结构体返回 | 应以 runtime_invoke 和已验证 Color 布局调用，不用猜直接 ABI |
| `get_From@3774E0` | 返回固定原版字符串常量，不读实例 | 本次未解析该常量的文字内容；不能因此宣称原生来源是 LOCAL |

`LevelDifference.ctor@3772B0` 创建新的对象赋给 bestScore 字段、保存 ChartInfo，然后按 `ChartInfo.level` 查传入 colorMap；成功分支在 `3773AF` 写入取得的 Color。未命名的 `7592A0`/`75C7C0` 由参数、out Color 与返回分支推断为字典查询/索引器行为，**没有宣称已取得其精确关闭泛型 MethodInfo**。失败查询有固定备用键；该键文字仍未解析。构造器本身没有调用成绩存储、LevelControl 或资产加载器。

## 可直接继续实现的数据图

以下是**实施方案**，尚未调用原生构造器验收：

1. 精确无参构造 `Level`、`MusicInfo`、`ChartInfo`，按需要补 `LevelInfo`、`IllustrationInfo`；使用受类型/静态性验证的 `il2cpp_field_set_value` 写引用与已支持值类型，所有对象各有 root。当前 NativeRuntime 没有该写入 helper，需要新增并验证。
2. 将框架 LocalId/revision/chartId 保持在宿主注册表；为原生 DTO 填入有界、非空且不撞原版的 `Level.id`、`ChartInfo.id`、`ChartInfo.level`，不登记进 Workshop UInt64 字典或游戏目录。
3. 直接从 `LevelDifference.ctor` 的第 2 参数 `Il2CppType` 取得精确 `Dictionary<string, UnityEngine.Color>` 类，核验后无参构造，调用精确 `set_Item(string, Color)` 登记所选 `ChartInfo.level` 的颜色；这样正常命中首个查询分支，不依赖未知备用键。构造与字典方法尚需真实元数据/runtime_invoke 验证。
4. 调用精确 `LevelDifference.ctor(ChartInfo, colorMap)`，再 `LevelMetadata.ctor(Level, LevelDifference)`；runtime 验证 `ILevelMetadata` assignability 和九个 getter 输出，尤其是 Color、未知难度名、From 常量。
5. 将 native metadata identity 映射到框架稳定身份；公开 snapshot 的来源/ID 使用此映射。`get_From` 是原版常量，不能作为识别 LOCAL 的依据。

getter 最小必需字段是：`Level.musicInfo` 非空，其 musicName/artist/themeUiColor 有值；`LevelDifference.chartInfo` 非空，其 designer/level 非空，difficulty/difficultyText 有界。`LevelInfo`/IllustrationInfo/List<LevelDifference> 不在这些 getter 的普通 Level 路径上；为其他游戏消费者补全这些字段之前，不能声称整个 Level 图已适配。

## 结算与保存：不能只 hook ProcessLevelMetadata

`InsideLevelResult.ToBestScore(LevelMetadata)@375380` 只是新建/填充 BestScore：

- `trackAssetId = metadata.level.id`（`375443..37544F`）。
- `difficultyClassName = metadata.chartInfo.chartInfo.level`（`375454..375472`）。
- 复制分数、完成率与判定计数。**这里用的是 ChartInfo.level，和保存 key 使用的 ChartInfo.id 不同。**

`ProcessLevelMetadata@3CFB00` 在 `3CFB5D` 转换、`3CFB81` 更新 `LevelDifference.bestScore`，改善时把固定 prefix + `ChartInfo.id` 作为 key，序列化 bestScore，在 `3CFC37` 调 `DomainWrapper.set_Item`，最后 tail-jmp `CheckAchievement@3CE440`。

更关键的是：`LevelOverControl.Start@3D0B20` 对 LevelMetadata 分支**内联了同一逻辑**，在 `3D0ECE` 调 ToBestScore、`3D0EE9` 调 Update、`3D0FAB` 调 set_Item，再 `3D0FBC` 调 CheckAchievement、`3D0FC6` 调 CheckOffset。Start 没有通过 `ProcessLevelMetadata`，所以只挂 `3CFB00` 不能隔离正常结算保存。

`DomainWrapper.set_Item@3899C0` tail-jmp `Set@389370`；Set 在 `38948F`/`3894F2` 调 `SaveData.Set@3ADA50`，并在 `389572` 调 `SaveSystem.SaveService@38E0B0`。这证明进入原版保存服务路径；本次没有执行它，也没有证明最终磁盘写入时间。

`CheckAchievement@3CE440` 中的直接调用包括 `Steamworks.Data.Achievement.Trigger@4C2430`、`SteamUserStats.AddStat@48BF10`、`SetStat@48CBE0`；还通过 `AreAllChartsInDisc@3CDD60` 调 `LevelControl.GetAllDiscs@375890`。因此对独立 LOCAL 需明确控制这些 stock 路径，不能因为原生 id 不撞名就允许自动进入。具体接入可以是有验证的 LOCAL 结果/重试页，或按稳定身份约束的适配分支；本报告不指定尚未证明的 native hook ABI。

## 首局初始化与重试

`NormalMode.GetInsideLevelParams@3BEDF0` 保存当前 LevelInformation，读取既有 LevelParams 并初始化 UI/模式；元数据来自接口 getter，封面来自资源 getter。`3BF0DB`/`3BF13C` 以当前设置覆盖 speed/offset，`3C02CF` 返回原 LevelParams。范围内没有对 LevelControl.GetLevelById 或原版资产加载器的直接调用；其他组件/Awake/回调仍可能有依赖，不能据此证明整局可运行。

两个 stock `LoadLevel` 协程（Normal `3C5F60`、LevelOver `3D3B50`）按 `metadata.chartInfo.chartInfo.id` 进入 `524F60` 的泛型资产解析候选，保存到 dump 已标明的 `AssetRef<TextAsset>` 字段，等待其对象，调用 `TextAsset.get_text`（`3C618F`、`3D3D7F`）再解析 Chart，随后构造资源引用/InsideLevelParams。泛型方法名及关闭类型地址不在 script.json 内，不把未命名地址宣称为精确 hook 目标。**这足以否定“传入自建 LevelMetadata 后 stock 重试会自动读取框架 CPU lease”的假设。**

已有 [RetryCoroutine 静态证据](../lifecycle-cancellation-20261020/disassembly.txt) 还显示按 LevelMetadata/WorkshopLevelMetadata 选择上述加载路径。LOCAL 应以保留的同 revision 请求重新实例化 Chart 与相关资源，不能依赖 stock 查找。

## Evidence → Finding → Path

证据哈希见 [evidence-manifest.json](evidence-manifest.json)。所有 Finding 的 validated 只表示该有限静态行为已由 dump 与指令交叉核验，runtime acceptance 均为 false。

| Evidence | source_ref | 可复现方式 | content_hash |
|---|---|---|---|
| E-imports / E-exports | triage.json | 下方 inspect 命令 | manifest 中的 SHA-256 |
| E-dump | metadata-excerpts.txt，原 dump 及 hash 在 boundaries.json | 下方 inspect 命令 | manifest/输入 hash |
| E-code | disassembly.txt、boundaries.json，各 range 有独立 hash | 下方 inspect 命令 | manifest |
| E-anchors | static-claims.json | 下方 verify 命令 | manifest |

| Finding | severity / status / confidence | evidence_ids | location | 结论与限制 |
|---|---|---|---|---|
| F-metadata | n/a_re / validated / high | E-dump,E-code,E-anchors | LevelMetadata `377430..377890` | 普通 Level getter 图可独立构造；尚无原生实例调用验收 |
| F-difference | n/a_re / validated / high | E-dump,E-code,E-anchors | LevelDifference `3772B0..377430` | 依赖调用方 ChartInfo/色表并创建 bestScore；泛型色表方法绑定仍需 runtime 证明 |
| F-save | n/a_re / validated / high | E-dump,E-code,E-anchors | `3CFB00`、Start `3D0ECE..3D0FC6` | stock 结算有保存、成就/统计；ProcessLevelMetadata 单点不能覆盖内联分支 |
| F-retry | n/a_re / validated / high | E-dump,E-code,E-anchors | `3C5F60`、`3D3B50` | stock 重试走 TextAsset 解析，不会自动消费 LOCAL leases |

P-construct，path_type=callflow：LocalPlayRequest 精确 revision → 宿主创建普通 Level/info + ChartInfo → 原生 LevelDifference/LevelMetadata → 自身 roots + native identity 映射 → metadata-only runtime probe。步骤依据 E-dump/E-code 与 F-metadata/F-difference；最后一步尚未执行，不能把方案作为运行成功。

P-stock-result，path_type=callflow：原版 ControllerOver 返回 → LevelOver.Start 取得结果 → concrete LevelMetadata 分支 → ToBestScore/Update → DomainWrapper.set_Item → SaveData.Set/SaveService → CheckAchievement/Steam stats。步骤依据 E-code/E-anchors 与 F-save；本次没有执行此路径。

## 复现

在仓库根执行，脚本只读样本且拒绝其他 SHA：

```powershell
& C:\Python314\python.exe docs/validation/local-metadata-bridge-20261022/inspect-metadata-bridge.py --game 'D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll' --dump 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs' --symbols 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\script.json' --out docs/validation/local-metadata-bridge-20261022
& C:\Python314\python.exe docs/validation/local-metadata-bridge-20261022/verify-static-claims.py
```

预期 verify 输出：`Verified 44 static anchors in 27 bounded scopes; runtimeAcceptance=False.`

## 交付与遗留

已完成本次分派的静态提取、导入/导出锚点、数据依赖、保存/重试判定、复现脚本与证据清单。未更改 src/tests，未构建、启动游戏、调用 Unity/Steam/保存方法或操作 UI。

下一步可以实现并实测 metadata-only 工厂；完整 LOCAL 播放仍需要：受验证字段写入与字典类解析、精确 native 参数创建、scene/controller 交接、LOCAL 结果保存/重试分支、资源所有权与撤回/取消/释放验收。此报告不完成 B15，也不完成 B01–B19。
