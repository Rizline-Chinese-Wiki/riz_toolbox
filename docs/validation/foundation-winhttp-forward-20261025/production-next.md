# 下一步：接入生产启动链

独立四 API 转发已通过 21 个自有进程。当前生产 Doorstop 仍是旧代理路径，新运行核仅在自有启动链采用。下面是后续实施顺序，未标为完成。

1. 在 `native/RizToolbox.Doorstop` 增加明确的生产适配层。保持现有 82 个代理导出和其既有 ABI；不能把独立测试 DLL 的 7 个导出直接当作游戏代理替换包。四个实际 WinHTTP 导入须按名称绑定，不能沿用系统与代理不一致的序号。
2. 审查生产入口的 CRT、日志、路径及现有 load_proxy 调用顺序，确定经证实的系统加载位置。独立 resolver 的 outside-loader 成功不证明能在 DllMain 中加载；也不能把 UnityPlayer 的 TLS/静态初始化首次调用假定晚于 il2cpp_init。先用自有宿主复现生产入口关系，证明可行路径与拒绝路径，再谈生产首次发布凭据。
3. 将新 Kernel 的固定发布记录与生产版本、模块文件身份、四入口前缀和首次发布时机绑定；补齐独立 V4 证明与 CLR 只读采用。保留旧 helper 的独立身份，不把测试模块或记录混入生产。
4. 接 coordinator、真实对象根与 GC、Unity 线程与调度器；处理 async/Start 内联返回、取消完成与全部消费者结束后资源最终释放。责任未结束则 Pending 保留，不能提前宣布 EngineCancelled 或开放 RequestPlay。
5. 完成代码和自有宿主验收后再恢复游戏实测。按当前用户指示，先继续代码、游戏实测稍后；后续真实整局、失败、重试、暂停、返回与多插件共存的结果分别记录。

完整验收继续以 `.docs/foundation-expansion/SPEC.md` 的 B01–B19 为准。
