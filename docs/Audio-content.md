# 音频和本地内容底层

插件可以加载自己的音效，也可以登记音频、封面和谱面，组成可选择的本地曲目。在 r13 之后的源码中，目录界面用 `SelectRevision` 选中玩家看到的那一版谱面。玩家可以在主菜单浏览并选谱，进入游戏选曲页后点击 **Request play**。两个页面共用同一次选择。

这个界面位于可选的开发示例 `example.panel` 中。启用开发示例后，将该插件的 `local-catalog-proof` 配置中的 `Enabled` 设为 `true` 即可查看。普通模组可以在清单中声明 `riz.content.local.selection` 和 `riz.ui.windows`，通过同一组选曲接口实现自己的目录。

只供浏览的谱面可以省略 `LocalSongChartRequest.Playback`。要让玩家开局，插件需填写该谱面的播放信息，并提供含谱线和音符的已解析谱面。在 r12 之后的源码中，对缺少这些数据的选曲调用 `RequestPlay` 会返回 `ContentInvalid`，插件可根据错误信息补齐数据后重新登记。此项检查在本地播放组件开始安装前完成。

**r11 于 2026-11-23 通过了开局请求的 Steam 实测。** 独立插件从选曲页请求播放单音符谱，游戏接受了请求并显示谱名。约 6 秒后 SDK 在初始化动画时失去跟踪。完整游玩、结算后返回以及官方成绩的赛前赛后对照仍待验证。[实机回执](../work/b15-live-local-crossscene-20261122-r11/live/20261123T024707842Z-eaeacebf6bc24a0c999e5b1f502cc82e/result.json)

| 能力 | 当前源码范围 |
|---|---|
| `riz.audio.playback` / `IAudioPlaybackService` | PCM16 WAV 读取、AudioClip 创建/上传，自有 AudioSource 播放/停止/租约 |
| `riz.audio.replace` / `IAudioReplacementService` | 活跃且已加载场景中的游戏 AudioSource.clip 优先级替换 |
| `riz.audio.mix` / `IAudioMixService` | 单声源 volume/pitch 优先级覆盖；没有混音总线或全局 mixer 能力 |
| `riz.audio.clock` / `IAudioClockService` | 独立 DSP 秒与宿主单调秒，不宣称已完成谱面时间映射 |
| `riz.content.local` / `ILocalContentProviderService` | 命名空间、本地格式/依赖/预算校验及原子登记；登记不等于游戏会解析它 |
| `riz.content.local.batch` / `ILocalContentBatchProviderService` | 1..32项内容及前向依赖验证后一起发布，共享一次修订、一个组租约；不新增游戏歌曲目录 |
| `riz.content.local.catalog` / `ILocalCatalogProviderService` | 将已验证的音频、封面和 1..16 个谱面组成带修订的命名空间歌曲；可附作者声明的播放 metadata |
| `riz.content.local.selection` / `ILocalCatalogSelectionService` | 启用作用域内的精确歌曲/谱面选择；RequestPlay 接入正式宿主事务，生产准入缺失时拒绝，不提供预览或替换成官方曲目 |
| `riz.content.workshop-chart` / `ILocalChartReplacementService` | 已知构建中，把本地 v0 图绑定到已有真实 workshop ID；显式实验选项和独占协调 |

## 加载自己的声音

2026-10-21 内容事务审查：单项与批量登记现在都在 scope 接管及依赖复核后发布，失败清理逐项执行并报告；错误线程不会消耗消费者释放委托，未确认释放的根及依赖继续计入配额。模块恢复不能丢弃该预算，要求进程重启。专项63/63，红绿证据与限制见[事务审查](validation/content-transaction-audit-20261021/README.md)。这不等于游戏新曲库或本地音乐/封面已经接入。

插件的主线程 `Enable` 或主线程回调中，在声明能力后使用：

```csharp
var found = context.GetService<IAudioPlaybackService>(AudioContentCapabilityIds.Playback);
if (!found.Available || found.Service is null) throw new InvalidOperationException(found.Error?.Message);
var loaded = found.Service.LoadWav(new ContentFile(ContentFileArea.Package, "assets/tone.wav"));
if (!loaded.Success) throw new InvalidOperationException(loaded.Error?.Message);
var playing = found.Service.Play(loaded.Value!.Asset, volume: 0.5f);
if (!playing.Success) throw new InvalidOperationException(playing.Error?.Message);
// 返回资源已归属当前 scope；需要提前结束时调用 playing.Value.Stop()。
```

WAV 仅 PCM16、单/双声道、8–192 kHz，编码上限 16 MiB、解码上限 32 MiB。读取和解码为同步有界操作，取消在读块/解码边界检查，不是流式或后台 Unity 上传。路径必须位于真实已加载包根或该插件批准的 Data 根，绝对路径、越界和链接逃逸被拒绝。

最多 32 个自有播放源，单提供者 16 项/48 MiB、全局 64 项/128 MiB 音频预算。NativeRuntime 只创建精确的 System.Single[]；使用导出元数据和空数组大小验证动态数组头，并 pin 后上传，不猜固定头偏移。上传后的样本、声道、频率会读回核对。

正在播放的 clip 与被覆盖的游戏原 clip 分别保活，借用的游戏 clip 不销毁。Stop 失败时不会提前销毁仍可能被播放的资源；自然播放结束由刷新收口。游戏自己的后续声源写入遵循协调器基线策略，声源代次失效后旧租约不能复活。替换不保证重新开始、采样对齐、音频延迟或结果不受影响。

## 登记内容并绑定已有谱面目标

每条带音符的谱线都需要能承载音符的线段。游戏按相邻的两个 `linePoints` 创建一段，再将 Tap 放到对应的段上。只有一个点时，该行写在 `notes` 里的 Tap 不会在游戏中生成。

示例使用 120 BPM、恒定卷速 1 和同一画布。起点在第 0 拍，Tap 在第 2 拍，终点在第 6 拍，对应的 `floorPosition` 依次是 0、1、3。Tap 的判定时刻为曲内第 1 秒。[完整谱面 JSON](../examples/local-content/one-note.json)

若填写 `chartDelayMs`，请用有符号 32 位整数，单位为毫秒。长按的 `otherInformations` 需要三个有限数值。r17 之后的源码会在登记内容时指出这两种字段的格式错误。带音符的行缺少第二个点时，作者仍可浏览该谱，开局请求会返回 `ContentInvalid` 并指出行号。实验性的 Workshop 替换也会检查该问题。

内容 ID 的 Namespace 固定为调用插件 ID。音频格式是 `wav.pcm16` v1，封面是严格 PNG 的 `png.rgba8` v1；已知图解析模块准备好后才接受 `rizline.chart.json` v0。坏格式、依赖缺失或取消时整个条目不发布。格式版本 0 的依赖须显式声明 `MinimumFormatVersion: 0`；不同类型的格式版本不是全局版本序列。

下面是主线程方法体的操作顺序，`workshopId` 必须来自当前安装里真实存在的目标，不能使用虚构 ID：

```csharp
// context、workshopId 是当前方法的已验证输入。
var content = context.GetService<ILocalContentProviderService>(AudioContentCapabilityIds.LocalContent);
var target = context.GetService<ILocalChartReplacementService>(AudioContentCapabilityIds.WorkshopChartReplacement);
if (!content.Available || !target.Available) throw new InvalidOperationException("Chart boundary unavailable.");
var registered = content.Service!.Register(new LocalContentRequest("my-chart", LocalContentKind.Chart,
    "rizline.chart.json", 0, new ContentFile(ContentFileArea.Package, "assets/chart.json")));
if (!registered.Success) throw new InvalidOperationException(registered.Error?.Message);
var binding = target.Service!.BindWorkshopChart(workshopId, registered.Value!.Content.Content, allowExperimental: true);
if (!binding.Success) throw new InvalidOperationException(binding.Error?.Message);
```

原图读取函数仍调用恰好一次；原始异常和空结果不被本地内容吞掉或绕过。成功解析后由唯一内部协调器选择替换返回，接收者、MethodInfo、返回类和根都验证，普通插件拿不到指针。相同实际 WorkshopLevel 的不同别名共享独占租约，冲突返回所有者；每次解析生成新的图对象，防止游戏 Mirror 等后续修改污染下一局模板。

该实现不修改原始游戏二进制或 workshop 文件，也不把只在 CPU 登记的封面/音乐当成已进入游戏的曲包。provider 退出后，不再接受新解析；已被局持有的内容按明确生命周期保活。场景离开 Level 或观察丢失触发已声明的保留租约结束规则，不据此伪造 Aborted 事件。实际当前图确认后写入 [本局修改记录](Run-modifications.md)，结果/提交路径验证标志仍为 false。

## 一起登记谱面、音乐和封面

在插件 manifest 声明 `AudioContentCapabilityIds.LocalContentBatch`；若需要读取发布列表，同时声明 `LocalContent`。图格式还要求独立 Workshop 图解析模块已就绪。下面先列谱面，再列它依赖的音频/封面，允许组内前向引用：

```csharp
var batch = context.GetService<ILocalContentBatchProviderService>(AudioContentCapabilityIds.LocalContentBatch);
if (!batch.Available || batch.Service is null) return;
var registered = batch.Service.RegisterBatch(new[] {
    new LocalContentRequest("chart", LocalContentKind.Chart, "rizline.chart.json", 0,
        new(ContentFileArea.Package, "assets/chart.json"), new[] {
            new LocalContentDependency(new(context.PluginId, "music")),
            new LocalContentDependency(new(context.PluginId, "cover")) }),
    new LocalContentRequest("music", LocalContentKind.Audio, "wav.pcm16", 1,
        new(ContentFileArea.Package, "assets/music.wav")),
    new LocalContentRequest("cover", LocalContentKind.Cover, "png.rgba8", 1,
        new(ContentFileArea.Package, "assets/cover.png"))
});
if (!registered.Success) {
    context.Log.Log(LogLevel.Warning, registered.Error!.Message);
    return;
}
// registered.Value 的 Revision 标识本次完整发布，Contents 是不可变列表。
// 租约已由 scope 持有；提前撤回整组时调用 registered.Value!.Dispose()。
```

任一格式、依赖、环、重复ID、配额、取消或作用域检查失败时不发布任何条目。配额与单项登记共享（单提供者32项/64MiB，全局128项/128MiB，已撤回但仍由局持有的内容继续计入）。撤回先让整组不可见，再释放发布引用；消费者依赖图继续到租约结束。图解析模块退出时，含图的整组一起撤回。

该接口保证的是内容服务的一次发布，不保证文件同时读取或游戏已加载新曲目；读取中被修改/缺失的文件仍会校验失败。真实目录、音乐和封面接入的静态边界及下一步见[解析边界](validation/local-content-resolution-20261021/README.md)，事务回归见[批量登记](validation/local-content-bundle-20261021/README.md)。

## 登记 LOCAL 曲目并描述播放 metadata

先用上面的内容服务登记音乐、封面和谱面，再通过 `AudioContentCapabilityIds.LocalCatalog` 取得 `ILocalCatalogProviderService`。内容 ID 的 Namespace 必须属于当前插件；同名标题可以共存，身份使用 ID 和修订，不按标题匹配。注册有界复制 metadata，并持有组成歌曲的精确资源修订。

当前 `LocalSongChartRequest` 使用 ID、名称和内容 ID 三个构造参数，另有可选 `Playback`。仅登记 CPU 曲目可以不提供它；需要请求播放的谱面必须提供，不能由框架猜计分上限或借用另一首曲目的 metadata。项目尚未发布，合同允许按最新实现调整，示例以当前源码为准。以下为可放在插件类中的方法，`audio`、`cover`、`chart` 是本插件已经成功发布的内容 ID：

```csharp
using RizToolbox.Sdk;

static Result<ILocalSongRegistration> RegisterLocalSong(
    IPluginContext context, LocalContentId audio, LocalContentId cover,
    LocalContentId chart, int declaredMaxHit, int declaredMaxScore)
{
    var catalog = context.GetService<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog);
    if (!catalog.Available || catalog.Service is null)
        return Result<ILocalSongRegistration>.Fail(catalog.Error ?? new OperationError(
            ErrorCode.CapabilityUnavailable, "LOCAL catalog unavailable.", AudioContentCapabilityIds.LocalCatalog));

    var difficulty = new LocalSongChartRequest("easy", "Easy", chart)
    {
        Playback = new LocalChartPlaybackMetadata(
            DifficultyIndex: 0, MaxHit: declaredMaxHit, MaxScore: declaredMaxScore,
            DifficultyText: "Easy", Designer: "Chart author",
            ThemeColor: new ResourceColor(0.3f, 0.6f, 1f, 1f))
    };
    return catalog.Service.RegisterSong(new LocalSongRequest(
        "song", "Song title", "Music artist", audio, cover, new[] { difficulty }));
}
```

| metadata 字段 | 含义与校验 |
|---|---|
| `DifficultyIndex` | 0/1/2，选择固定构建实际 `InLevelSettings.JudgeDataList` 中的项；不是任意 rating 或自定义判定窗口。原生来源读取时还核当前列表、版本、对象类和四个有限正判定值 |
| `MaxHit` / `MaxScore` | 作者声明的计数/计分上限，分别为 1..1,000,000 与 1..1,000,000,000；不是框架根据音符推导出的游戏计分公式 |
| `DifficultyText` / `Designer` | 非空、无控制字符，最多 256 字符；`Illustrator` 可为空，限制相同 |
| `ThemeColor` / `DifficultyRating` | RGBA 各分量为有限 0..1；评级为有限 0..1000。保留作者值，不代替生产许可 |

`Playback` 同样保存在不可变 `LocalSongChartSnapshot` 中。歌曲必须引用已验证的实际资源；重复 ID、坏 metadata、缺资源或取消不会发布半个条目。没有 Playback 时仍可登记/列举/选择，正式播放请求会明确返回 `ContentInvalid`，不会补假默认值。

## 从当前选择请求播放

下面的方法在主线程调用。`song` 是注册或列举时取得的原始歌曲快照。每张谱面的 `HasPlaybackContent` 表示已登记作者提供的播放信息，且解析到谱线和音符。目录可据此显示请求按钮。若歌曲已经撤下或重新发布，`SelectRevision` 返回 `TargetExpired`，已有选择得以保留。

```csharp
static Result RequestSelected(IPluginContext context, LocalSongSnapshot song, string chartId)
{
    var lookup = context.GetService<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection);
    if (!lookup.Available || lookup.Service is null)
        return Result.Fail(lookup.Error ?? new OperationError(ErrorCode.CapabilityUnavailable,
            "LOCAL selection unavailable.", AudioContentCapabilityIds.LocalCatalogSelection));
    var selected = lookup.Service.SelectRevision(song, chartId);
    if (!selected.Success) return Result.Fail(selected.Error!);
    using var selection = selected.Value!;
    return selection.RequestPlay();
}
```

选择句柄归属当前启用作用域，也可在主线程提前 Dispose。成功选择替换该视图的当前代次，失败/取消保留前一选择。正式请求持有同一 selected revision 的音频、封面和谱面；异步消费者在返回前自行 Retain，之后不会按同名 ID 重查。提供者撤回后禁止新请求，已接收事务的精确资源仍保留到其实际责任结束。

游戏接受 `RequestPlay` 后，目录显示 **Play request accepted.**。需要读取自己发起的 LOCAL 单局结算时，调用下节的 `RequestPlayWithResult`。游戏选曲卡尚未就绪时，请求可返回 `NotReady`；启动组件未就绪时返回 `CapabilityUnavailable`。独立目录缺少播放服务时返回 `OperationUnsupported`。目录会显示实际错误，玩家可以在条件满足后再次请求。

正式 Retry 等待旧局完整完成 receipt 后，重新捕获当下 manager/control 和该谱面 difficulty 对应的 judge；内容仍保留原 selected revision，不重查同名资源。已知来源暂未就绪时先完成该次借用清理、等待后帧；无法确认的 root/fork/free 责任保持 Unknown，不重复释放或重新发起。core-r2 已覆盖这些受控正反例，尚未把它作为真实游戏菜单重建与再开局的验收。

## 读取自己发起的 LOCAL 单局结算

SDK 1.5.0 提供 `ILocalCatalogSelection.RequestPlayWithResult`。收到成功回执后，把 `ILocalRunResultHandle` 保存到发起插件的字段。在需要显示结算时，于主线程调用 `Read()`。

```csharp
private ILocalRunResultHandle? _localResult;

private Result PlayForResult(ILocalCatalogSelection selection)
{
    var request = selection.RequestPlayWithResult();
    if (!request.Success) return Result.Fail(request.Error!);
    _localResult?.Dispose();
    _localResult = request.Value!;
    return Result.Ok();
}

private void ReadLocalResult(ILogger log)
{
    if (_localResult is null) return;
    var read = _localResult.Read();
    if (read.Value is { } round)
        log.Log(LogLevel.Info, $"{round.ChartId} 分数 {round.Result.Score}");
    else if (read.Error is { } error)
        log.Log(LogLevel.Info, error.Message);
}
```

每个句柄只绑定该请求首次进入保护登记的 LOCAL 局。用 `Song`、`SongRevision` 和 `ChartId` 识别谱面，用 `LocalSessionId` 和 `RunGeneration` 区分单局。`Result` 包含这局实际结算复制出的分数、完成度、判定计数和结束原因。

捕获完成后，返回选曲、重新选择谱面、开启下一局或由其他插件发起播放时，旧句柄仍可读取该局副本。句柄属于发起插件本次启用作用域。停用插件会使其句柄失效。每个选曲服务最多同时保留 32 个句柄。达到上限时，播放请求返回 `QuotaExceeded`。用完后在主线程调用 `Dispose()`，结束该句柄的读取并释放名额。

| `Read()` 结果 | 含义 |
| --- | --- |
| `NotReady`，`Retryable=true` | 已受理，等待实际捕获。稍后再读取同一请求。 |
| `TargetExpired` | 本局在捕获前已进入返回。 |
| `ObservationUnavailable` | 观察发生终态故障。 |
| `ScopeExpired` | 句柄已释放，或发起插件的启用作用域已结束。 |

## 为什么 Workshop 替换选择这个解析入口

[可复查的静态证据](validation/audio-content-20261020/static-boundary.json)针对已知 GameAssembly SHA-256：NormalMode.LoadWorkshopLevel 在 RVA `0x3C1138` 直接调用 WorkshopLevel.LoadChart `0x3E0D00`，随后在 `0x3C1251` 调用 InsideLevelParams 构造。`SetInsideLevelParams@0x36B0E0` 只有共享写字段/写屏障指令，因此没有把它当作已证实的解析挂点。

这证明存在编译后的直接调用路径，不证明某个运行会话实际经过挂点。真实挂接、加载本地图、两插件交错、局中撤回、结果差异、音乐和封面同步及帧耗时矩阵仍待验。复查脚本需要本机 Python 的 pefile/capstone，命令和范围见 [证据说明](validation/audio-content-20261020/README.md)。
