# 第三方组件来源

项目许可证适用于本仓库自行实现的 SDK、核心、适配和示例代码。下载的第三方二进制保留各自许可证，不转换为本项目许可证。

构建脚本使用 SHA-256 固定的 BepInEx `6.0.0-be.789+6cea286` 官方 Windows IL2CPP x64 运行包，从中安装 Dobby `dobby.dll` 及配套 .NET 运行时；不使用 BepInEx 完整 chainloader。

`winhttp.dll` 由 UnityDoorstop commit `33dab9a6733862eb81869ff08431d9478b28784b` 的修改版构建，许可证 LGPL-2.1，修改日期 2026-10-24。修改为同步调用 RizToolbox 原生 pre-init producer；保留原 IL2CPP 初始化与 CoreCLR 启动链。包内 `BepInEx/plugins/RizToolbox/licenses` 附许可证和完整对应 Doorstop 源码 ZIP，含修改文件、上游来源哈希及构建命令。独立 C++ producer 属于本项目，动态加载，不把 Doorstop 许可证改为本项目许可证。构建细节见 `native/RizToolbox.Doorstop/RIZLINE-MODIFICATIONS.md`。

- 运行包来源：https://builds.bepinex.dev/projects/bepinex_be/789/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789%2B6cea286.zip
- BepInEx 项目及许可：https://github.com/BepInEx/BepInEx
- Unity Doorstop 项目及许可：https://github.com/NeighTools/UnityDoorstop
- Dobby 项目及许可：https://github.com/jmpews/Dobby
- .NET runtime 项目及许可：https://github.com/dotnet/runtime

原始下载包缓存于 `.tools`，运行时随包携带的许可文件由安装过程原样保留。发布包含第三方二进制的发行包时，应同时核对并保留所用版本的上游许可和归属说明。
