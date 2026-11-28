# Steam 版播放控制实测，2026-11-28

## 首局开销

分别在 #53 和 #54 用新进程启动《竹》SP，每版一次。两次适配器维护帧中记录的分段耗时合计由 25.9002 秒降至 20.6401 秒。首次捕获和随后接入两次 GetTime 校验由 2736.8 / 2726.7 毫秒降至 2.1 / 2.1 毫秒。此处只统计这两帧记录的工作，标题初始化另有单帧耗时 16.545 秒。

## SDK 暂停与恢复

同一进程的第二局选择《Pastel Lines》EASY。插件通过 SDK 发出 Pause，随后观察到 playing=false、paused=true，曲目时间连续停在 16.874584 秒。Resume 返回后，同局同控制器再次报告 playing=true、paused=false，曲目时间继续推进至 38.99 秒。两个指令都有实际派发和返回记录。取样及指令回执摘要见 [observations.json](observations.json)。

从《竹》切到这首歌后，SetSpeed 和 Seek 都报告缺少可供新一局接管的前一局播放源，本局没有派发这两个指令。

## 首局暂停后的变速与跳转

重新启动 #54，首局进入《Pastel Lines》EASY。SDK 暂停后音源时钟可用。SetSpeed(0.75) 成功，随后独立的暂停状态采样读回 0.75 倍。请求跳到游戏时间 75 秒时，在准备 Hold 列表阶段收到 UnsupportedSignature。之后再次调用 SetSpeed(1) 成功并恢复播放。[指令回执摘要](first-session-commands.json)

游戏报告闭合类型 List<NoteInformation>.Add 入口为 0x9d7710、槽位 11。PE 的运行时函数表给出完整函数长度 97 字节，SHA-256 为 0596c0f5ab09172e65a0b7786a5d7026573dd4175183f2f24a9d4997122f4ea7。实现更新列表版本、检查容量、在快速路径写入引用并执行写屏障；容量不足时跳至既有的扩容实现 0x9d73f0。仅更新 Hold 的闭合类型绑定，保留声明、参数类型、槽位与完整函数体检查。

## 修正 Hold 绑定后复测

进程 53368，Adapter SHA-256 为 2f6778599d36ba4551a606f145793054c5e9dd619356d5ca96ec754dd3ffbd2a。新进程首局《Pastel Lines》EASY，SDK 暂停成功。再次请求跳到 75 秒时已通过原先的 Hold 检查，随后在渲染器校验阶段返回 `UnsupportedSignature: A complete renderer source body changed: UnityEvent.Invoke`。跳转仍待实机通过。

磁盘上的 624 字节与渲染器预期 SHA-256 一致。运行时的原生安装器会在该函数内安装观察点，并保存修改前后的代码。下一步需要让渲染器同时验证当前观察点的归属和原函数的完整内容，并覆盖原生安装器校验范围之外的末尾两个字节。
