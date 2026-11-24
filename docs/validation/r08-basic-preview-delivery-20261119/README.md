# R08 基础预览离线交付（2026-11-19）

四份 r2 包已完成离线封存。当前源码编译、SDK作者验证、分发、验包和假目录安装均有独立结果与来源记录。包状态保持 `preview/unverified`；本轮没有安装、启动或操作真实游戏及账号。

使用 [Runtime-basic](../../../artifacts/delivery-20261119/r08-runtime-basic-r2/RizToolbox-0-3-0-r08-runtime-basic-preview-20261119-r2.zip) 可取得歌曲/最好成绩面板、颜色主题和 F8 快捷面板（显式自有 PCM16 WAV 播放/停止）。[Author](../../../artifacts/delivery-20261119/r08-author-r2/RizToolbox-0-3-0-r08-author-preview-20261119-r2.zip) 提供当前 SDK、模板和示例。安装按各包 `DELIVERY.md`；范围、四包 SHA256 和限制见 [交付报告](../../2026-11-19_basic-preview-delivery-report.md)。

| 证据 | 当前结果与范围 |
| --- | --- |
| [Release构建](../../../work/r08-delivery-integration-20261118/current-release-build-r2-exit.json) | 0 errors、55条测试警告；1787项编译输入前后相同 |
| [相关短验](../../../work/r08-delivery-integration-20261118/current-checks-r3.json) | 82/82；受控提供方，不签真实游戏 |
| [只读采证](../../../work/r08-delivery-integration-20261118/acceptance-r1/results.json) | 14/14；安装归属与人工声明分开记录 |
| [分发矩阵](../../../work/r08-delivery-integration-20261118/distribution-r2/results.json) | subject50/50；外层实际exit1另存，不改为成功 |
| [独立SDK作者](../../../work/r08-delivery-integration-20261118/sdk-offline-r1/result.json) | 两独立hive/缓存、本地feed，14/14；十子进程自然0 |
| [默认Doorstop](../../../work/r08-delivery-integration-20261118/doorstop-current-r4-exit.json) | 自然0；7325项来源保持、106条MSVC警告 |
| [四包清单](../../../work/r08-delivery-integration-20261118/current-deliveries-r2.json) | ZIP成员38/44/66/53，全量字节读回 |
| [有限包场景](../../../work/r08-delivery-integration-20261118/delivery-scenarios-r3/results.json) | 10/10；四正向、六负向，原件hash/大小/时间保持 |
| [假游戏安装](../../../work/r08-delivery-integration-20261118/current-install-r2/results.json) | 10/10；两安装/一卸载，十用户文件保留，零网络/真实游戏进程读取/载荷DLL执行 |
| [最终封存](../../../work/r08-delivery-integration-20261118/delivery-final-seal-r2.json) | session9484自然0；1787最终输入、12DLL、SDK消费者与四包身份保持 |

[evidence-index.json](evidence-index.json) 记录以上文件的路径、字节数和 SHA256，以及各实际退出观察；它仅索引现有证据，不复制构建和包。相对编译及独立SDK原输入只允许未参与其执行的说明生成器变化，不能把该例外解释为新运行源码也已经受测。

假目录安装只改变 clone lock 中的64字节哈希以接受纯文本游戏标记。首装212归属、二装保持全部字节；卸载210未改文件、保留2修改配置和十用户文件。它验证文件事务，不能证明游戏运行和资源可见恢复。

失败与中断原件继续保留：分发r1、短验r2、Doorstop r1–r3、r2说明汇总和包场景r2均未改写为成功。当前没有在途离线交付检查。R06正式Fresh/watch/SuppressObserver/固定consumer、R07同机60秒成本、R08三个实机场景/停用恢复/重启、真实Speed/Seek、完整B01–B19与production仍未完成；下一离线工作继续R06实际功能缺口。
