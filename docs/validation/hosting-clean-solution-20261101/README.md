# 干净解决方案构建已修复

2026-11-01，`flavor=null`。原解决方案没有登记 Hosting 的五个现有夹具项目；完整测试批预先产出夹具后能构建，但直接从干净源码执行 `dotnet build RizToolbox.sln -c Release` 会缺少夹具 DLL。本次把这五个项目及 Debug/Release 配置加入解决方案，随后将已验证的同一份补丁合入主目录。范围见 [scope](scope.md)。

正式独立原件在 `D:/riz-build-cache-next/hosting-fixture-solution-fix-r1-20261101/`，主目录合入原件在 `D:/riz-build-cache-next/hosting-fixture-solution-main-integration-r2-20261101/`。[证据索引](evidence-index.json)记录原件和本地副本的哈希。

| Evidence | 已执行与实际范围 |
|---|---|
| E-BUILD | 修补后的全新源码直接构建 Release：actual exit 0，0 errors、53 个既有 warnings；没有预编译或复制旧 DLL，binlog 与原始 stdout/stderr/exit 保留 |
| E-HOST | 直接运行本次编译的 Hosting Tests：actual exit 0，20/20；私有 V1/V2 隔离、共享 SDK 身份、禁止默认私有回退通过，五个 fixture 输出与复制目标字节相同 |
| E-MAIN | 主目录应用前 SHA `dfe03678ca5257623df10c536c964b1de2cbf830222eb68278f73befb990e785`，`git apply --check` 与实际 apply 均 exit 0；应用后 SHA `0c4f50c7578452ec68c2135e60abe0934d4c68515a0bba995afc35d03e16bd3f` 与独立验证源码逐字节相同 |

F-BUILD（severity=n/a_re，category=other，status=validated，confidence=high，evidence_ids=[E-BUILD,E-HOST,E-MAIN]）：解决方案配置缺项已修复并合入主目录。位置：`RizToolbox.sln`。产品 C# 改动为零；没有增加 ProjectReference 回退，也没有跳过 fixture copy。可按独立原件中的 `solution-build.command.json` 和 `hosting.command.json` 重现，须使用其中固定生成 profile 与本机 .NET 依赖。

P-BUILD（path_type=callflow，E-BUILD/E-HOST/E-MAIN → F-BUILD）：仅含源码的冻结目录 → 正常解决方案项目依赖与配置 → 编译五个夹具 → Hosting 自有加载器隔离测试 → 精确补丁应用到主目录。

此项只修复构建配置。27 套 2465 项仍引用其既有批次，本次没有重跑整套，也没有签当前未合入的 MusicPlayer writer。没有安装、启动游戏或操作账号；生产准入、完整 N2 与 B01–B19 尚未完成。
