# 基础模组与离线交付进展（2026-11-19）

三个普通 SDK 模组已接入当前主工程：歌曲/最好成绩面板、颜色主题、F8 快捷面板与显式自有 WAV 播放。它们复用宿主的能力需求、作用域和资源仲裁，不为模组 ID 增加专用权限。完整 B01–B19 与实验播放控制继续保留各自状态；本报告不代表完整项目交付。

本轮修复两处 WAV 边界。调用者的 `ContentFile.MaximumBytes` 默认 16 MiB，基础面板显式使用 1 MiB；音频服务在打开文件后、分配缓冲区前执行限额，因此先前的预读不能批准后来被替换的大文件。Play 返回成功但没有停止租约、返回 null 或抛异常时，面板保留片段和作用域、拒绝替代播放并请求宿主清理。

当前 Release 解决方案构建 0 错误、55 条测试代码警告。1787 项源码/构建输入在构建前后保持相同，六个正式组件和基础模组均以本次生成的 DLL 为准。当前基础模组14、SDK10、Core24、Hosting24及十个音频/内容定向 case 合82/82，受测程序集前后相同。四个新增限额 case 覆盖同路径替换大文件、精确1MiB与低一字节、无效限额在目录批准前拒绝、最小值/默认属性/空文件；最后一项没有测试实际16MiB文件边界。

当前只读采证工具14/14完整通过。它记录当前清单、实际安装字节、安装归属、模组和所选WAV，并区分文件一致性与人工场景声明。Pending或伪造归属不会被采证过程修复为成功。记录始终保持 `preview/unverified`。

| 证据 | 范围与状态 |
| --- | --- |
| [当前构建](../work/r08-delivery-integration-20261118/current-release-build-r2-exit.json) | Release solution，0 errors；源码前后1787项一致 |
| [当前相关短验](../work/r08-delivery-integration-20261118/current-checks-r3.json) | 82/82，14个进程自然退出0；使用受控提供方 |
| [采证结果](../work/r08-delivery-integration-20261118/acceptance-r1/results.json) | 14/14；合成声明/安装归属与只读来源验证 |
| [原生复用核对](../work/r08-delivery-integration-20261118/native-reuse-current-r1.json) | 172项当前来源/字节相同；没有转移旧整包PASS |

第一次分发执行在末端之前会话中断，虽然保留50条中间PASS，但缺最终结果和退出记录，不能签为整套通过。当前短验r2也在末端汇总运行器的OrderedDictionary字段时失败；修复汇总后r3重新自然完成。失败原件及其日志均保留。

50项schema3分发矩阵已全部完成并生成完整结果，测试 subject 与1787项当前构建输入仍一致。外层工作运行器最后错误读取未定义的原生命令退出码，进程实际退出1；[过程观察](../work/r08-delivery-integration-20261118/distribution-r2-process-observation.json)保留这一失败，不将其改写为退出0，也不因此重复已完整完成的subject检查。

当前 SDK 编译引用包已生成，并通过两个独立模板 hive、CLI home、NuGet cache 的离线作者验证。两个本地 feed 提供当前 SDK 和框架引用资源；正式 loader 14/14，十个子进程自然退出0。包 SHA256 为 `95453b6565552e480dd40ba35dd929edd3aff8f1a051525fe7e38955195efea3`，实际引用 DLL 为 `5a93faf44ba2f1df064c26d39f666fc867ca0125498f1dbf4c362494289160ef`。见[当前 SDK 结果](../work/r08-delivery-integration-20261118/sdk-offline-r1/result.json)。

默认 `legacy-default` Doorstop 在浅目录 r4 成功新建，7325 项源码、工具链和归档输入前后相同，真实进程自然退出0。主 `Build-Doorstop.ps1` 未改，未启用另外的 WinHTTP/EngineBootstrap profile。前3次分别因 rc.exe、winres.h 和260字符依赖路径失败，原件保留。当前 WinHTTP 为 `da1405e5dd5baed30eb2e4f26579ed048f3d569b7f6e1e1fb6095a662a604f89`；编译记录有106条 MSVC 警告，不能写成零警告。见[构建结果](../work/r08-delivery-integration-20261118/doorstop-current-r4-exit.json)和[警告汇总](../work/r08-delivery-integration-20261118/doorstop-warning-summary-r1.json)。

四份当前分发和完整依赖ZIP均已打包。r2仅修正操作说明：只有实际选择主题的包才示例停用 `basic.theme`，宿主命令 ID 来自实际插件列表，源码构建命令注明仓库上下文。1787项编译输入中只改说明生成器，12个受测DLL与四份package-manifest不变；没有借旧字节签新实现。r2首轮工作汇总误取readback返回值未提供的archiveBytes，真实exit1保留；已完成的Runtime-empty经过新核对后保留，finish进程自然exit0并生成其余三包。

| 最终离线包 | 载荷 / 开发资源 | ZIP成员 | ZIP字节 | SHA256 |
| --- | --- | ---: | ---: | --- |
| [Runtime-basic](../artifacts/delivery-20261119/r08-runtime-basic-r2/RizToolbox-0-3-0-r08-runtime-basic-preview-20261119-r2.zip) | 21 / 0 | 44 | 37122958 | `4a389ff9ae332d5f95b81991672a629d625f17dbda4103084b69158ab46ae6bf` |
| [Runtime-empty](../artifacts/delivery-20261119/r08-runtime-empty-r2/RizToolbox-0-3-0-r08-runtime-empty-preview-20261119-r2.zip) | 15 / 0 | 38 | 37044187 | `f033c95927494b2f487ccac56a28d758464942232ea92424d0aebb6360ccbd11` |
| [Author](../artifacts/delivery-20261119/r08-author-r2/RizToolbox-0-3-0-r08-author-preview-20261119-r2.zip) | 15 / 28 | 66 | 37575319 | `dcf42f90025284e3cf5d9acc1c44b5e60171a7d26d638aadcbe40ef6bbdc4c85` |
| [Validation](../artifacts/delivery-20261119/r08-validation-r2/RizToolbox-0-3-0-r08-validation-preview-20261119-r2.zip) | 15 / 15 | 53 | 37524326 | `0539eb6eb71da7ed8b8b334de93cabe52951a5631b7502dec84f2fa800959f86` |

每份ZIP均完整读回，成员清单和收据独立存放；载荷、固定完整依赖ZIP、安装脚本、用户文档和配置样例一并提供。[有限包场景](../work/r08-delivery-integration-20261118/delivery-scenarios-r3/results.json)已10/10、进程自然0，覆盖四包正向、原输出拒绝、错误标签、展开文件改动、ZIP成员改动/缺失和收据伪造production；原件与工具hash/大小/时间保持一致。旧 `delivery-scenarios-r2` 因工具连接中断缺终态，session handle及实际进程均消失；217个原来源文件hash/大小/时间再核相同，旧现场保留，未计通过。

[当前假游戏安装](../work/r08-delivery-integration-20261118/current-install-r2/results.json)已10/10、外层与实际child均自然exit0。runner只替换clone lock中的64字节GameAssembly哈希，使原样安装脚本接受纯文本假游戏标记，主lock和包不变。完整228文件依赖ZIP、187文件dotnet及dobby按字节核对；首装21载荷和总212归属，pending=0。第二次安装保留所有游戏文件和10份明确用户文件；卸载移除210份未改归属文件，保留2份修改配置及启用意愿、日志、存储和48字节WAV，源归属hash继续记录原值。只执行假目录的Prepare/Install/Uninstall，两次安装、一次卸载、零网络、零真实游戏进程读取，未加载任何载荷DLL；这证明文件事务，不证明运行表现。

安装与包场景均核对候选及主脚本前后hash/大小/时间不变。[最终seal](../work/r08-delivery-integration-20261118/delivery-final-seal-r2.json)已自然退出0（session9484），再次确认1787最终输入、12个DLL、四个ZIP/清单/收据和SDK独立消费者保持。相对原编译/SDK输入只允许未参与其执行的说明生成器改动；四包support文件逐项匹配当前文件。过程观察与实际运行器复核另存，历史失败原件未改；不重复已闭合功能矩阵。完整入口见 [正式证据索引](validation/r08-basic-preview-delivery-20261119/README.md)。

本轮未安装、启动或操作真实游戏及账号。三个游戏内场景、停用后的可见恢复、重启、同机60秒运行成本、正式Fresh/固定consumer正向以及真实倍率/Seek仍待验；R06、R07、R08、B01–B19和production未整体完成。用户README修改保持原SHA256，无commit或push。
