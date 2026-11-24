# 下一步：生产 V4 与新核采用

固定四 API 已接入实际 Doorstop DllEntry，并在自有普通依赖调用链通过冷启动。保持本批候选开关和历史默认路径可区分。下列是下一实施包，尚未完成。

1. 增加独立 `RizToolbox.EngineBootstrap.dll` 生产 owner，静态依赖无入口的新 Kernel，代理普通依赖 Bootstrap。三者部署必须真实满足 Windows 普通依赖搜索；不能在 DllMain 中临时加载 Kernel 或改搜索目录。Bootstrap 无 CRT/TLS/动态初始化。
2. 直接接真实 DllEntry 的 reason 与 reserved 参数，区分静态启动和动态加载。动态加载不授首次写。对固定 EXE/UnityPlayer 验文件与映像、四完整函数和六份实际 RF/unwind，使用本批 EngineGameProfile 的定长常量；原 fixture marker 检查保留在原路径，不能删除后改成泛用 patch API。
3. 内部固定四 source/RVA，静态 originals/continuation，四 kind/slot28 Configure 后读真实 epoch/entry，再 Bind 与四次按序 Commit。V4 分开记录首次准入依据、producer epoch、Kernel ABI2/scalar1、四行身份、所有写尝试/完成和保护责任；新V4不能是fixture publisher2或wire3改名。
4. 发布过程中任何源写尝试或保护责任后失败都保留 Unknown 并阻止卸载后继续执行，后续代理初始化失败也要合并责任。不得留下源跳转进入已卸载模块。完全无写且无责任时才允许 DisabledNoSourceWrite。
5. 增加 CLR 私有构造的 NativeEngineFirstInstall owner 与 bridge/dispatcher AdoptPublished。只用已加载 Bootstrap/Kernel 的 GetModuleHandle 与 GetExport，逐行复核凭据及实际映像；Attach前后再验。不能在 CLR 中 Load/Configure/补patch，新核和旧GameAssembly V3仍分别核对。
6. GameAssembly 不在固定 EXE/UnityPlayer 普通依赖中，过早阶段不能要求它已加载，也不能为满足检查提前加载。其实际模块与 IL2CPP 条件留给现有 pre-init/CLR 接续；必要的磁盘身份单独验证。
7. 使用自有等形目标验证完整 producer→Kernel→CLR、四首次调用、重入/故障/保护与失败不卸载；随后在用户恢复实测后观察真实游戏的发布先于 Unity TLS/入口/首次引擎调用。静态依赖、已映射模块和非空 reserved 各自只有有限含义，不能单独当成所有历史执行者闭合证明。

之后接真实对象根与 GC、coordinator、scheduler/async/Start 内联收尾、取消完成及所有消费者资源退休，再验整局、重试、返回和多插件。完整 B01–B19 保持原范围。
