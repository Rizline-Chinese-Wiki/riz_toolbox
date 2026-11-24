# 子作用域与取消

SDK 1.2 的 `IScopedPluginContext` 是可选扩展，旧 `IPluginContext` 的签名不变。当前宿主为每次插件启用创建独立生命周期；子作用域提供同一插件下独立的服务、事件与资源归属。

## 使用

在插件的 `Enable(IPluginContext context)` 内创建一个子作用域，并通过它获取服务或订阅事件：

```csharp
var lifetime = (IScopedPluginContext)context;
var scene = lifetime.CreateScope("scene-panel", ScopeLifetime.Scene);
var ui = scene.GetService<IUiService>(CapabilityIds.Ui);
if (ui.Available)
    ui.Service!.CreatePanel(new PanelDefinition("details", "Details", "Ready"));
scene.Events.Subscribe<FrameUpdate>(frame => { /* main-thread work */ });
// The next scene event or plugin disable expires this scope automatically.
```

方法和资源操作都要求 Unity 主线程。名称为 1–64 个 ASCII 字母、数字、点、横线或下划线；即使名称相同，每次创建的 ScopeId 也不同。配置仍归插件共享，不为每个子作用域创建一份配置目录。局部面板 ID 仍使用插件命名空间，同一个插件的两个作用域需要给同时存在的面板使用不同局部 ID。

| 生命周期 | 失效边界 |
|---|---|
| `Manual` | 显式 Dispose、父作用域结束、插件停用或故障 |
| `Scene` | 以上边界，加下一次 GameSceneChanged |
| `Run` | 匹配所绑定局的 RunEnded（含 Aborted）、可靠匹配的 RunFinished、新局/新场景或生命周期提供者撤回 |

Run 作用域应在收到真实开局通知后创建。提供生命周期服务时，它绑定原子快照中的 session、run、场景代次和修订；创建前后再次核对，避免把重入期间已结束的局留为有效 scope。旧 session/旧局的结束、未关联结果和重复的同局 legacy RunStarted 不会结束当前 scope；在新局生命周期回调中创建的 scope 也不会被随后同 ID 的旧格式开局事件立即取消。

完全没有声明生命周期服务的旧宿主保留“下一次 RunStarted/RunFinished/场景”行为。已经声明服务但查询失败、契约错误或提供者撤回时，新/旧 Run scope 保守失效并记录警告，不静默退回任意结果边界。新边界有 30 项 Scope 回归；完整实机逐局矩阵仍未验收。

## 取消与清理

父级失效先让整棵子树不可再操作，再取消令牌，最后逆序释放拥有的资源。生命周期边界处于 EventHub 的内部阶段，先于任何公开优先级监听执行。新场景监听期间创建的作用域会存活到下一次边界。取消回调里触发其他子作用域 Dispose 会延迟到本轮取消结束，避免中断其他令牌通知。

`Lifetime` 可从后台观察，停用后仍可读取取消状态。后台工作只能使用普通快照，再通过该作用域的 MainThread.InvokeAsync 应用结果。入队和执行均受有效期约束；后台完成时作用域已失效，结果不能应用。取消或释放资源发生异常时继续处理其余资源，诊断接收器异常也不能打断清理。

主线程队列默认最多 256 项，Pump 可限制每帧项数；已取消的队首项不继续占据下一次入队容量。满队列得到 `DispatcherQueueFullException`。动作执行前会检查令牌，即使取消回调被其他回调阻塞也不会执行已取消请求；已经开始的同步 Action 不可强制打断，执行中取消令牌不会谎报该动作已经回滚。状态中提供 Pending、PeakPending、Executed、Cancelled 和 Rejected 计数。

本轮只有现有 Update 入口，没有新增 LateUpdate/FixedUpdate、任意任务强制停止、后台 Unity 访问或自动重试。它们不属于当前可用接口。

## 验证

`dotnet run --project tests/RizToolbox.Scope.Tests/RizToolbox.Scope.Tests.csproj -c Release` 最新 30/30 通过：包含子作用域资源、事件、父子取消、重入、场景/局身份、Aborted、未关联/旧结果、创建窗口、能力撤回、迟到任务、线程/队列与清理故障。真实游戏中的 example.panel 也使用子作用域；此前两局失败的取消日志不代替新 Retry 原生取消边界的实机验收。
