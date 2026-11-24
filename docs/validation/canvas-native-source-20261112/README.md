# Canvas 实际源捕获：本批离线验收

CanvasMove、完整 KeyPoint List、初始化后的 Ease 表、实际 down/right 静态值和 Range 转换已经接入正式 backend，并生成不可变 Canvas 目标投影。**本批只完成来源捕获与计算，不写 Transform，不开放真实 Speed/Seek。** 测试使用受控原生导出和正式 backend；没有在真实游戏中观察这些新行为。

## 本批结果

| 检查 | 结果 |
|---|---|
| 六组件 Release | 成功，0 警告 / 0 错误 |
| 判定默认全套 | 268/268，含新增 16 组 Canvas 源测试 |
| Runtime ABI 选择器 | 51/51 |
| 精确静态字段读取选择器 | 6/6 |
| AudioContent 默认完整执行 | 1001/1001 |
| 冻结构建输入 | 815 项，构建执行前后未变 |
| 测试依赖副本 | 24 份 DLL/PDB 与当前六组件对应产物一致 |
| 独立源码复核 | 当前 29 个源/依赖/测试文件哈希一致，无剩余必要阻断 |

Runtime 测试构建有 1 条既有 CS0649，AudioContent 测试构建有 27 条既有测试警告；均无错误。所有实际命令、退出码和警告详见 [原始结果](build-test-result.json)。这不是对整个解决方案或全部 B01–B19 的完成声明。

## 行为与限制

- 按实际数组槽保留顺序、重复 move/KeyPoint/Transform 引用；不使用 CanvasMove.index 重排。完整读取 List backing、size、version、尾槽及官方 Count/Item，并比对其一致性。
- 复制已经初始化的完整 EaseInfos 与每行 101 个 Single 样本，读取确切 readonly static down/right；没有缺失时的理论值回退，也不调用 easing 初始化器或 Unity producer。
- 来源独立保根，申请者 RunValue 退出后仍能验证已有捕获。构造序号把后续失败捕获与已有 owner 分开；每次验证保持原 Plan，不重新运行转换。
- 引用交接确认与来源准入分开。已取得的 root 先登记并确认交接，再检查回调撤销；已撤销的未消费 GC 引用不触发新分配。数组 pin、slot、Ease sample-size 和 Range 异常回调后只允许已知清理。
- 元数据检查覆盖当前原生类型与宽度，不能用已缓存布局掩盖 Double/Single/Int32 漂移；浮点来源比较保留原始位，包括负零。
- 双次采样不证明原子性或排除 ABA；生产实时来源、Unity 回调副作用、Canvas 目标写入、line/ring、缺失 runtime、renderer consumer、Hold 退出和完整事务仍待后续工作。

## 证据与复现

| Evidence | 内容 | 对应结论 |
|---|---|---|
| E-001：[结果](build-test-result.json)、[输入](inputs-before.json)、[索引](evidence-index.json) | 本批新目录构建、实际执行与哈希 | F-001：上述离线矩阵通过，旧 DLL 未用于验收新源码 |
| E-002：[文件核验](canvas-profile-verification.json)、[静态依据](canvas-static-evidence.json) | 10 类、7 完整范围 / 4096 字节、31 字段、7 入口、5 常量、23 指令断言 | F-002：当前固定版本的只读来源合同成立；不代表游戏实时关联已验证 |
| E-003：[增量复核](independent-source-review-supplement-r2.json)、[状态](source-review-status.json) | 首轮真实失败后的修复与当前源码哈希 | F-003：已修复错误交接记录和测试回调目标；原复核未覆盖后续修复 |
| E-004：[首轮失败](first-full-failure-result.json)、原始失败日志 | 首轮 266/268、自然退出 1 | F-004：失败及中间图保留，没有用后续通过覆盖历史 |

最终整套执行中，AudioContent 实际为 1001/1001、自然退出 0；控制器最初仅识别 `N/N passed`，漏掉 `N/N audio/content checks passed.`，所以在第 7 步误拒绝并未进入第 8 步。原控制器结果见 [解析拒绝记录](controller-parser-rejection-result.json)。修正解析后逐项重核相同源图、实际退出和日志哈希，保留原来 7 个成功子进程；只对同批产物补执行静态读取 6 项，再核 24 依赖副本。没有重跑已成功测试，也没有更换产品源码。

P-001（callflow）：独立保根实际图 → E-002 精确元数据与初始化样本读取 → 完整双次位比较 → 实际 Range 与受控返回准入 → 纯目标投影 → E-003 来源复验及单次已知清理。F-001 的执行证据不延伸成完整 Seek 准入。

在项目根运行以下命令，输出必须使用一个尚不存在的新目录；脚本会拒绝覆盖旧证据：

```powershell
C:/Python314/python.exe -B work/canvas-native-source-validation-20261112/validate_current.py --output D:/riz-build-cache-next/canvas-native-source-20261112/manual-recheck
```

脚本只在子进程环境设置自有 native 夹具路径，并先核对其哈希；不修改永久环境。窄范围迭代可加 `--canvas-only`，只执行 16 组，不可替代默认完整验收。验收报告目录：`D:\riz-build-cache-next\canvas-native-source-20261112\root-r2\final-r2-completion`。本次实际构建产物目录：`D:\riz-build-cache-next\canvas-native-source-20261112\root-r2\final-r2\artifacts`。

下一步见 [Canvas 目标位置 writer 合同](next-target-writer-contract.md)。全 B01–B19 未完成；真实 Speed/Seek 保持关闭。未安装或启动游戏，未操作 UI/账号，未更改旧 ZIP、代理设置，未提交或推送 Git。
