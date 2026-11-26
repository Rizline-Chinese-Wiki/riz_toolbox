# 参与贡献

1. 基于最新 `main` 建工作分支，优先提交单一、可审阅的改动。
2. 用测试或合成数据说明问题，按 README 运行构建与冒烟测试。
3. 开 Pull Request，注明改动动机、测试结果，以及对公开契约的影响。未验证或需讨论的改动可先开草稿。
4. 枚举值、结果 JSON、公开方法签名如有变化，请说明兼容性和迁移方式。

请只提交有权公开的源码、文档及合成测试数据。不要提交游戏文件、游戏内存或反汇编导出、闭源依赖、构建输出、密钥、个人配置或含个人信息的日志。新增第三方代码或资源时先确认许可证及署名要求。

## GitHub Actions 构建

提交 PR 或向 `main`、`candidates/*` 分支推送后，GitHub Actions 会在 Windows x64 上运行 `scripts/Build.ps1 -SkipTests`。也可从 **Actions → Build Windows x64 → Run workflow** 手动构建指定分支。

成功后，从该次运行的 **Artifacts** 下载 `RizToolbox-Windows-x64-Basic.zip` 或 `RizToolbox-Windows-x64-Runtime.zip`。Basic 预装歌曲和成绩、主题、快速面板三个插件。两个 ZIP 均为 `preview/unverified`。关闭 Rizline，解压后使用 PowerShell 7.2 或更高版本，按 `delivery/DELIVERY.md` 安装。

插件开发可在同一次运行下载插件 SDK 引用包或 `RizToolbox-Plugin-Author-Kit.zip`。作者包提供同次构建的 SDK、项目模板和示例，解压后按其中的 README 即可编译插件。每次 CI 都从生成的 ZIP 创建并编译一个示例插件。实机验收与发行记录见[项目进度](.docs/PROGRESS.md)。
