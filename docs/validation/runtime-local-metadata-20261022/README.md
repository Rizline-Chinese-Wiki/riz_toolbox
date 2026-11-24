# LOCAL 原生 metadata 实测

2026-10-22（UTC+8）的真实游戏会话已验证自建普通 `LevelMetadata` 对象图：三个独立 owner 经游戏原生 GC 后仍能读取九个 getter 和颜色；撤回其中一个不影响另外两个，最终释放全部 30 条 owner root 记录。**这只完成 metadata 图的实机验证，完整 LOCAL 曲包仍不可启动，B15 保持未勾选。**

## 实际观察

本次使用 [846 检查包](../foundation-local-metadata-20261022/README.md)，会话 `20261022T045603594Z-e4f565dd363f437fb252e4f0a28bb335` 共 **252 条完整连续事件、0 Error、3 Warning**。三条 Warning 为目录尚未完成加载、设置页未打开的模块等待/挂起状态，原日志保留。游戏通过菜单退出按钮和确认按钮正常退出，随后确认无 Rizline 进程。

| sequence | 阶段 | 观察 |
|---|---|---|
| 84 | 创建及原生 GC | 3 个 metadata owner、30 条 owner root 记录；游戏 IL2CPP `System.GC.Collect()` 返回，`canLaunch=false` |
| 85–87 | 独立读回 | 同标题的两个 provider、同 ID 的两个 revision 各有独立 native identity；九 getter 和各自颜色均匹配输入 |
| 88 | 撤回及清理 | 已释放 owner 拒绝读取；剩余两个经再次原生 GC 后可读；最终全部 owner root 记录释放 |

三个输入都使用歌曲标题 `LOCAL Metadata Sentinel`、歌曲 ID `same-song`、谱面 ID `chart-a`；差异如下。原始九 getter、颜色及各自 native identity 完整记录在 [runtime-proof.json](runtime-proof.json)。

| namespace / revision | Artist | 显示名 / 内部名 | Lv / Charter | RGBA |
|---|---|---|---|---|
| probe.metadata.a / 1 | Artist A | LOCAL_A | 2.5 / Designer A | .15, .35, .65, 1 |
| probe.metadata.b / 1 | Artist B | LOCAL_B | 2.5 / Designer B | .2, .7, .3, 1 |
| probe.metadata.a / 2 | Artist A revision 2 | LOCAL_A_R2 | 3.5 / Designer A | .6, .4, .2, 1 |

三个原生 `get_From` 均实际返回 **`LevelList`**，Status 均为 0。普通 metadata 的原版来源字段不能承载 LOCAL 身份或自有返回目标；后续启动路径须使用宿主持有的 native identity → LocalId/revision/chartId 映射与进入场景快照。

## Evidence → Finding → Path

范围是本机用户授权游戏的 metadata-only 探针，未进入关卡，未调用游戏结果或保存路径，未向原版目录注册本地歌曲。固定 GameAssembly SHA-256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；构建与安装范围见 [构建记录](../foundation-local-metadata-20261022/README.md)。本报告采用普通逆向记录结构，flavor=null。

| Evidence | 原始记录 | 支持的结论 |
|---|---|---|
| E-static | [前序静态报告](../local-metadata-bridge-20261022/README.md) | 27 个有界范围、44 个指令锚点支持独立普通 metadata 数据图及自身 getter 调用关系；该静态报告本身不承担实机验收 |
| E-build | [构建摘要](../foundation-local-metadata-20261022/build-summary.json)、[源码清单](../foundation-local-metadata-20261022/source-manifest.json) | 846/846、25 套；精确字段写入、owned Arg 线程清理和 owner 失败责任回归已构建 |
| E-runtime | [机器核验](runtime-proof.json)、[完整事件](20261022T045603594Z-e4f565dd363f437fb252e4f0a28bb335/events-000001.jsonl) | 252 条连续事件、3 个独立 owner 的创建/读取/撤回/清理；事件文件 SHA-256 为 `2998a31342e1f3bf0c186f96bf3bc3ff531e7b9b3d0018b4d7ede933eefa6031` |
| E-install | [安装核对](../foundation-local-metadata-20261022/installation-check.json)、[源码核对](../foundation-local-metadata-20261022/source-integrity-check.json)、[原游戏文件](../foundation-local-metadata-20261022/original-game-after.json) | 实测收尾时 18 个安装 hash 一致、283 个源码输入未变、三个原游戏文件未变 |
| E-restore | [marker 原状态](probe-marker-before.json)、[恢复结果](probe-marker-restoration.json) | `local-native-metadata.enabled` 原先不存在，实测后恢复不存在，`restored=true` |

F-metadata（validated，E-static、E-build、E-runtime，confidence=high）：本包可在固定游戏版本创建并持有普通 metadata 图，原生 GC 后得到本次全部预期 getter 值，支持此三 owner 范围内的独立撤回和 root 记录释放。

F-origin（validated，E-static、E-runtime，confidence=high）：普通 `LevelMetadata.get_From` 是原版固定来源，本次值为 `LevelList`；不能依据这一字符串区分 LOCAL 或决定宿主返回目标。

P-metadata（callflow，E-build、E-runtime）：验证 descriptor 与固定版本 → 精确字段/闭合泛型核验 → 创建每 owner 的 10 root 对象图 → 原生 GC → 读回九 getter/颜色 → 独立释放中间 owner → 再次原生 GC 并验证其余 owner → 逆序清理 → 冻结日志并复核安装 → 恢复 marker。

## 复核与后续

在仓库根目录可重新核验冻结会话：

```powershell
python docs/validation/runtime-local-metadata-20261022/freeze-proof.py docs/validation/runtime-local-metadata-20261022/20261022T045603594Z-e4f565dd363f437fb252e4f0a28bb335
```

脚本检查唯一 session、完整 sequence、创建/三行读回/最终验证、所有已声明断言及 18 个当前安装 hash。安装后续版本后，当前安装不再匹配时会拒绝复核；应保留本目录原冻结证据并使用对应包环境。

重新实测需先正常退出游戏并安装对应包，显式创建 `BepInEx/RizToolbox/probes/local-native-metadata.enabled` 后启动，完成后正常退出并恢复 marker 原值。该 marker 默认不存在，不赋予曲包启动能力。

30 条记录是本 owner 图的 GC root 责任记录，**不等于原生 heap 已回收、长期 GC 压力或 GPU 资源测量**。探针 `canLaunch`、`gameCatalogModified`、`gameResultOrSavePathInvoked` 均为 false。Sprite 包装器/Texture 责任、完整音乐/封面/Chart/InsideLevelParams 交接、controller 提交、LOCAL 重试/结果/返回及 provider 撤回仍须实现和实测。下一步依据为 [启动与结果隔离边界](../local-launch-20261022/README.md)，完整 B01–B19 继续按原 SPEC 验收。
