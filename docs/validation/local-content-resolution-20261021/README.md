# 本地曲包解析边界：静态证据与下一步

2026-10-21。代理在完成21个有界函数的提取后因额度中断；主代理复核输出并整理本说明。本目录没有新增 hook、运行游戏、修改原版二进制或触发下载。

`triage.json` 记录原生x64 PE的导入/导出及工具版本；`boundaries.json` 绑定 GameAssembly SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79` 和 dump/script.json 哈希。`disassembly.txt` 为机器指令，`metadata-excerpts.txt` 为带行号类型定义。脚本的符号签名可能省略实例接收者，且一个地址可能有多个共享泛型/折叠函数名，不能直接当作已验证 hook ABI。

已确认的静态调用：

- NormalMode.LoadWorkshopLevel 在 `0x3C1138` 调用 `WorkshopLevel.LoadChart@0x3E0D00`，在 `0x3C1251` 调用 `InsideLevelParams` 构造；LevelOver路径对应 `0x3CF908` 和 `0x3CFA1C`。现有图替换仅覆盖这些已有 Workshop 对象。
- `WorkshopLevel.Initialize.MoveNext@0x3D3040` 调用 Steam Item 的 IsInstalled、NeedsUpdate、DownloadAsync，然后 Validate；`Validate@0x3E0FD0` 与 LoadChart 都读取 Item.Directory。因此直接伪造一个 WorkshopLevel 不等于离线本地 provider，还可能进入原本的 Steam 工作流。
- `LoadMusic@0x3E0EA0` 返回 IEnumerator；真正异步加载在 `MoveNext@0x3D59B0` 及 OGG/MP3 子协程。OGG 在 `0x3D5EC4` SendWebRequest、`0x3D5F4B` 取 audioClip；MP3先经过 NAudio 转换，再在 `0x3D5557` SendWebRequest、`0x3D55FF` 取 audioClip。返回一个普通 clip 无法替代协程/回调生命周期。
- 卡片封面 `LoadCover.MoveNext@0x3D39F0` 在 `0x3D3A5E` 读 `WorkshopPreviewImageCache.Get@0x3E2610`，在 `0x3D3ACF` 赋给 Image.sprite。开始游玩的协程也在 `0x3D7CD4` 读同一缓存，并在 `0x3D7915` 构造 InsideLevelParams；卡片显示和局内封面引用是不同消费期。
- InsideLevelParams 元数据含独立的 `IResourceReference<Sprite>`、`IResourceReference<AudioClip>` 和 Chart。`ResourceReference<T>` 元数据包含 Release、released 和 canDestroy 构造参数，但当前共享泛型地址信息不足以证明具体关闭类型的销毁语义。

下一步按实际接入顺序：

1. 明确一个独立本地曲目描述符（稳定命名空间ID、标题/作者、谱面与音频/封面依赖），沿批量登记先完整验证，不借用伪造Steam Item/ID。
2. 追踪选曲目录构建、刷新、排序/选择及该描述符到加载请求的转换。当前提取仅确认 WorkshopLevels 的字典/事件元数据，尚未验证所有目录调用点。
3. 验证关闭泛型 ResourceReference<Sprite/AudioClip> 的创建、Release、canDestroy及失败路径；确定原始调用失败保留、封面缓存和局内引用的独立所有权，再接音乐/封面的解析边界。
4. 实机验证初始化、进入/重试/结算/退出、加载中和局中提供者撤回，以及结果修改记录。没有这些证据不能把批量CPU登记称为完整曲包加载。

复现（仓库根目录，现有本机 Python + pefile/capstone）：

```powershell
& C:\Python314\python.exe docs/validation/local-content-resolution-20261021/inspect-local-content.py --game 'D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll' --dump 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\dump.cs' --symbols 'C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\script.json' --out 'docs/validation/local-content-resolution-20261021'
```
