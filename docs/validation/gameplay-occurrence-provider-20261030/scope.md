# Scope

`auth`：当前 N2 离线委派，完成具体 occurrence/source/writer provider、实际 accessor 与独立验证；遵循 checkpoint 顶部和 NEXT 的限制。

`in_scope`：新增 `NativePlaybackActualSource`、`NativePlaybackOwnedCallTransport`、`NativePlaybackOwnedCallProducer` / concrete provider；独立 owned native fixture、构建器、测试和此证据目录。只读复用固定 Pause/Replay/Resume、DSP、PlayScheduled、pitch 与 Progress 消费证据。Program/csproj/默认模块装配由根任务管理。

`network_profile`：offline；NuGet sources 清空，使用已安装工具和框架包。未操作任何账号。

`out_of_scope`：真实游戏安装/启动/UI、B 账号、真实 stock native hooks、完整外部 writer 覆盖、production GetTime authority；不改原 `NativePlaybackClock`、旧 `NativePlaybackRateProvider`、`GameplayClockNativeProfile`、`GetTimeBridge`、Runtime broker、N1、相机或退休。独立 fixture 不加入发行。

`flavor = null`。真实 Speed/Seek 保持默认关闭；本包不是整体 B01–B19 完成或真实游戏证明。
