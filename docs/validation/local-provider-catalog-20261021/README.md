# 本地曲目目录与资源所有权边界

2026-10-21，静态分析，flavor=null。当前证据足以确定可实施的适配边界；尚不足以宣称独立 LOCAL 曲目已经在游戏中出现或可播放。原生目录、收藏、选中项保存、卡片和加载路径绑定 `WorkshopLevel` 与 UInt64 Steam ID。独立本地曲目应以自己的描述符、选择状态与加载请求贯穿这些边界，不能给 `WorkshopLevels.LevelsById` 填一个假 Steam ID 就声称接通。另找到共享资源构造实现与候选释放实现：后者会销毁 Sprite 的 Texture，因此共享封面需要明确的租约责任。

范围与授权见 [scope.md](scope.md)。未执行游戏、网络操作或二进制修改；未安装 hook。只新增本目录文档和可复现提取脚本。

## 证据与复现

| E-id | source_ref | repro_command | content_hash |
|---|---|---|---|
| E0 | [前序分诊](../local-content-resolution-20261021/triage.json) | 前序 README 的 inspect-local-content.py 命令 | 样本 SHA-256 见 scope.md |
| E1 | [boundaries.json](boundaries.json)、[disassembly.txt](disassembly.txt) | 下方命令 | JSON 含样本/dump/script 哈希 |
| E2 | [metadata-excerpts.txt](metadata-excerpts.txt) | 同下，保留 dump.cs 原行号 | dump SHA-256 `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb` |

仓库根目录执行：

```powershell
& C:\Python314\python.exe docs/validation/local-provider-catalog-20261021/inspect-catalog.py --game 'D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll' --dump 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs' --symbols 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\script.json' --out 'docs/validation/local-provider-catalog-20261021'
```

脚本验证样本哈希，仅提取25个指定有界范围。函数签名有时漏实例接收者，一个地址可能存在不相干的折叠符号；原始指令、字段偏移与调用上下文优先于符号标签。`0xAEF1F0`、`0xAEF2C0` 不在 script.json 的符号表中，范围为明确选定，绝不把它们冒称为已确认的关闭泛型 MethodInfo。

## 发现

所有 severity 均为 n/a_re；下表 status 为静态分析状态。

| F-id | evidence_ids | confidence | location | status / finding |
|---|---|---|---|---|
| F1 | E1,E2 | high | LoadLevels.MoveNext `0x3D4060` | confirmed：订阅查询与 WorkshopLevel 构造是目录生产端；不能作为 LOCAL 接入消费端 |
| F2 | E1,E2 | high | ChangeDisc `0x3DF190` | confirmed：实际选曲集合是实例 `+0x68` 的 `List<WorkshopLevel>`，从 UInt64 字典或收藏解析构建 |
| F3 | E1,E2 | high | ChangeLevel `0x3DF880`、UpdatePosition `0x3E02D0` | confirmed：选中、行渲染、卡片、预览、保存选中 ID 都消费这个集合 |
| F4 | E1,E2 | high | `0xAEF2C0`、StartPlayLevel `0x3D7630` | confirmed instruction behavior / inferred generic identity：构造存 resource，released 初值为 !canDestroy；音频 true，缓存封面 true，默认封面 false |
| F5 | E1,E2 | medium | `0xAEF1F0..0xAEF2BE` | candidate Release：一次性检查、置 released、Destroy(resource)，若资源确切类型为 Sprite 则另取 texture 并 Destroy(texture)；关闭泛型虚表映射尚未证实 |
| F6 | E1,E2 | high | LevelOver.OnDestroy `0x3CFA50` | confirmed：Sprite 与 AudioClip 引用分别在 `+0x158/+0x160` 走接口 slot1 释放并清零；未证明所有中断路径 |

## P1：目录生产、刷新、选择的真实调用路径

path_type=callflow，依据 E1/E2、F1/F2/F3。

1. `WorkshopLevels.LoadLevels.MoveNext@0x3D4060` 调用 `Query.Items@0x3D4511`、`WhereUserSubscribed@0x3D459A`、`WithKeyValueTags@0x3D4623`、`GetPageAsync@0x3D46EE`；`0x3D4AD6` 调 `WorkshopLevel.ctor`。这是 Steam 生产端，最终以 ID selector `<LoadLevels>b__16_0@0x3D9700` 读取 WorkshopLevel `+0xA8` 的 UInt64 ID。
2. 最终化在 `0x3D4D5C` 调未命名的泛型集合函数；随后 `0x3D4DB8` 写静态 `LevelsById` backing field，`0x3D4E13` 清 Loading，`0x3D4E34` 释放 semaphore，`0x3D4E47` 读取 OnLevelLoaded 后调用。`<LoadLevels>b__16_1@0x3D9720` 读取静态 `+0x18` 的 OnLevelChanged 并调用。这里只证明发布边界，未把所有泛型集合函数命名为已证实的 LINQ 方法。
3. `WorkshopLevelListControl.Start@0x3DE090` 在 `0x3DE1FB` 调 `ReloadAll@0x3DD7C0`；ReloadAll 重建空列表/收藏状态、选择 Disc、更新可交互状态，空目录路径在 `0x3DDD24` 停预览。仅发布 CPU contentbatch 不会触发这套游戏 UI 消费。
4. `WorkshopLevelLoopList.ChangeDisc@0x3DF190` 是关键读取边界。普通页 `0x3DF535` 读静态字典，`0x3DF54D` 通过接口 slot2 取集合，`0x3DF55C` 物化；收藏页调用 `DomainWrapper.GetAll@0x3DF370`，经 FavoriteResolver/过滤后物化。`FavoriteResolver@0x3D92B0` 对键取 `Substring(4)`，再 `UInt64.TryParse@0x3D931F` 并查字典。
5. `0x3DF5B7` 运行 Ordering.Sort；恢复选中项时 `0x3DF637` 把持久值解析为 UInt64；`0x3DF6D8` 写实例 levels `+0x68`，设置 targetIndex `+0x74`、nowPos `+0x70`，`0x3DF775` 刷新位置，`0x3DF7D0` 交给卡片 SetInfo。
6. `WorkshopLevelListItem.OnClick@0x3DE650` 将行 index 交给 `ChangeLevel@0x3DF880`。ChangeLevel 按列表长度取模，取 WorkshopLevel，`0x3DF95C` 调卡片 SetInfo；再读该对象 `+0xA8` 的 UInt64 ID，`0x3DF9D5` 转字符串并 `DomainWrapper.set_Item@0x3DF9EF` 保存。`UpdatePosition@0x3E02D0` 在 `0x3E051D` 调行 SetInfo，`UpdateSorting@0x3E05A0` 对同一列表 Sort。
7. `WorkshopLevelCardControl.SetInfo@0x3DC080` 更新标题/作者/难度/成绩/收藏，停止旧预览与封面协程，调用封面缓存 Load；`PlayLevel@0x3DBC40` 在 `0x3DBE76` 调 `WorkshopLevel.LoadChart`，再使用预览 clip 和 StartPlayLevel。替换 Steam Item 构造或仅替换已有 LoadChart，均未覆盖前面这些消费者。

## P2：播放引用与释放责任

path_type=callflow，依据 E1/E2、F4/F5/F6。

- `InsideLevelParams` 明确分开 `IResourceReference<Sprite>`、`IResourceReference<AudioClip>` 和 Chart。`StartPlayLevel` 状态机的 `+0x48` 是 clip、`+0x50` 是 `ResourceReference<Sprite>`。
- `StartPlayLevel.MoveNext@0x3D781B` 用 RCX=this、RDX=clip、R8B=1、R9=MethodInfo 调 `0xAEF2C0`；`0x3D7D9F` 对 Sprite 调同一共享实现。缓存封面分支 `0x3D7D7B` 设 true；defaultCover 分支 `0x3D7D8C` 设 false。`0xAEF2E4` 写 `resource@+0x10`；`0xAEF2F5..0xAEF2F9` 写 `released@+0x18 = !canDestroy`。所以 false 使该引用从构造时就不承担销毁。
- 缓存封面取得后还安排异步移出缓存工作。`TakeOwnership@0x3E2840` 也调用 Get 后 Task.Run；其状态机 `0x3D8E60` 等 semaphore，再对 cache、cacheKeyIndex、cacheKeys 执行移除调用。具体 generic Remove 身份仍依赖未提供的泛型元数据映射，不将未命名函数单靠位置定名。
- 候选 `Release@0xAEF1F0` 在 `0xAEF221` 检查 released，`0xAEF234` 置1；资源运行时类型与 Sprite 类指针做相等检查，若相等在 `0xAEF254` 调 Sprite.get_texture；`0xAEF27E` Destroy(resource)，`0xAEF28B` 比较 texture 非空，`0xAEF2AE` Destroy(texture)。这份机器码行为明确，方法名/关闭类型绑定仍是推断。Sprite 子类也不能假定走同样 texture 分支。
- `LevelOver.OnDestroy` 在 `0x3CFA99`、`0x3CFAB6` 调接口 slot1，分别消费字段 `+0x158`/`+0x160`，随后清零。接口派发器 `0x3000` 通过接口偏移表解析 slot，并尾调用解析的方法。尚缺运行时或原生注册表证明该 slot 最终指向上述候选 Release。

## 可实施的 LOCAL 适配方案

此段是设计提案，未安装 hook，不能当作运行时验收。

1. **目录与身份**：建立 `LocalSongDescriptor { LocalId, ProviderId, ProviderRevision, title, artist, charts[], audioPath, coverPath }` 的不可变快照，LocalId 继续保留字符串命名空间。ProviderRevision 与选中请求共同持有租约。建立本地 tab/列表控制器；读取 validated contentbatch 后发布快照并在主线程刷新 UI。该 controller 不写原版 UInt64 字典，不复用其收藏/last-ID 解析器。
2. **行与选择**：可复用游戏视觉 prefab 的 Text/Image/Button 组件，但由本地 row/card adapter 设置这些组件并绑定事件；本地行列表使用 descriptor 索引，排序、收藏、last-selection 均按 LocalId 自己存储。不要对 LOCAL 行调用 stock `WorkshopLevelListItem.SetInfo`、`WorkshopLevelCardControl.SetInfo`，它们会读 Steam Item、原版成绩和封面缓存。原版列表仍走原路径。
3. **预览与确认**：本地 selection controller 维护请求 generation 和 cancellation。选择改变时停止前一预览、释放前一封面租约，异步结果只有在 generation 与 provider revision 匹配时可显示。点击 Play 用同一 descriptor 构造 `LocalPlayRequest`，完整解析谱面/音乐/封面后在主线程转换为游戏可消费的 `InsideLevelParams`，再交给已观察到的 controller 加载入口；转换前任一步失败应释放新租约并保留原选中状态。此段可以先实现独立 C++/SDK 层契约与独立 UI，不需要伪装 WorkshopLevel。
4. **局内、重试和结果**：不能因构造参数接受 ILevelMetadata 就认定全生命周期支持自定义类型。NormalMode/LevelOver 的重试路径有 `LoadLevel(LevelMetadata)` 与 `LoadWorkshopLevel(WorkshopLevelMetadata)` 两条类型特化入口；需要专门的 LocalMetadata/LocalPlayRequest 映射与 result/retry 适配，保持 LocalId 和 revision。未完成前最多标记目录/选择可用，禁止宣称完整本地曲包播放。
5. **资源责任**：为显示卡片和局内请求分别取得引用，避免把同一可销毁 ResourceReference 对象复制成两个 owner。共享默认封面/图集应为借用引用；独占生成的 Sprite + Texture 与 AudioClip 可交给一次性 owner。若复用共享 texture，应使用自己的租约引用实现，或在交接时创建独占资源副本；不能只对共享 Sprite 包 canDestroy=true，因为候选 Release 会连 texture 销毁。提供者撤回仅从后续目录中隐藏；已加载/局内请求继续持有旧 revision 的资源，直至最后消费者释放。

## 尚缺的精确证据

- `ResourceReference<Sprite>` 与 `ResourceReference<AudioClip>` 对应的 Il2CppClass/MethodInfo、泛型实例、vtable slot1 → `0xAEF1F0` 映射；当前 script.json 只有泛型定义，get_Resource/ctor 共享占位 `0x2F9AF0`，Release 占位 `0x1AFA00`。这些占位不是能直接 hook 的关闭实例地址。
- 缓存移出与释放的并发顺序，以及撤回/取消、加载失败、场景切换、重试/结算/退出对 owner 的完整转移。现有 Get→Task.Run 是异步移除，不能仅据函数名保证返回时已原子转移。
- 自定义 metadata 的局内结果、暂停重试、结果页重试分派与 UI prefab/event 的实机验证。当前均未测试，因此不声称 B01–B19 已全部验收。

## 时间线与阶段结论

- 继承前序 SHA/导入/导出分诊后，核对真实 tool-index：本机 `C:\Python314\python.exe`，pefile/capstone；不依赖标记缺失的 IDA/r2。
- 按指定目录/选择范围完成25个有界提取；新增泛型共享实现与接口派发器证据，未宽泛转储函数。
- decision_delta：目录接入点从生产端推进到真实 `ChangeDisc → levels → row/card → Play` 消费端；资源 canDestroy 初始化已明确，Release 的关闭泛型绑定仍保留证据缺口。
- carry_forward_refs：scope.md、前序 triage.json、本目录 boundaries.json。动态阶段依父任务运行安排，本文不对运行时成功作结论。
