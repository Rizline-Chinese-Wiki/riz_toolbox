# 2026-10-20 实机验收记录（持续补充）

用户明确允许恢复电脑操作。范围见 [scope](../../scope.md)；验收仍按 [SPEC](../../../.docs/foundation-expansion/SPEC.md) 全部条目执行，本目录不代表底层全部完成。

## 已得到的实机结果

| 项目 | 证据 | 实际结论 |
|---|---|---|
| 新包安装 | `installation-check.json`、各 `install-*.log` 与 `package-manifest-*.json` | 正式安装器执行；每次更新后 17 个包文件与清单哈希一致 |
| 开局与失败 | `lifecycle-probe-fixed/session/`、`evidence-summary.json` | 两局开始与失败结算均关联各自 RunId、同一谱面；没有补造开始 |
| 重试 | 第一局结束后点击真实结算页重试按钮 | 第二局产生新的 RunId，开始/结束分别关联 |
| 暂停恢复 | 第二局空格暂停、真实继续按钮恢复 | seq 725 暂停、740 恢复，保持第二局 RunId；精确 NormalMode Pause/Resume 入口计数为零，不能冒充已调用 |
| 单局作用域 | 两个 SDK-only 示例探针 | 共四次取消和释放检查；取消回调观察到 `active=False`，结算监听器中已过期 |
| 判定 Hook | 两个独立包同时订阅 before/after | 总计 959 次 native/original 调用相同，两个探针各 before/after 计数相同；无参数修改或 note identity 结论 |
| 修改协调 | seq 74 三持有者与 native read-back 日志 | priority 30/20/10 共用一个真实颜色目标；本轮只验了这个交错阶段 |
| 持久停用 | `startup-fixed-status.json` 与 `configuration-before/command.json` | 启动前已将旧 disable 命令替换为唯一 status 命令，成功重启后 scores 仍保持明确停用 |
| 新 UI | `input-diagnostics/session/`、`native-button/session/` | 已输入测试文本并收到变化回调；自有原生按钮31次回调、菜单导航1次进入LevelList；原始键鼠动作及双插件矩阵仍未验 |
| 新原生读取 | 新探针的 GAME CLOCK / AUDIO CLOCK / SETTINGS / RUN MODIFICATIONS 日志 | DSP/单调秒、四个真实音量值、空审计读取得到结构化结果；菜单下无局上下文返回明确不可用 |

可重查保留会话：

```powershell
& 'C:/Python314/python.exe' 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/runtime-unified-20261020/inspect-lifecycle.py'
```

测试期间游戏原有 life-100、隐藏血条和 free-hold 修改开启，不作为纯游戏性能基线。正常完成、提前退出、各新服务的真实修改/恢复矩阵、完整本地曲包与四基线性能仍未验收。

## 保留的失败与修复

1. `first-start-before-host/`：只到桥安装，宿主未初始化；之后 Steam 启动选项出现。原因尚未确证。
2. `steam-launch-without-host/`：Steam 启动进程有本地 WINHTTP，但未加载 CoreCLR，旧 status 未更新。Steam 已运行后从游戏路径直接启动成功；不能拿此推断所有 Steam 启动必然失败。
3. `probe-serialization-failed/`：探针直接序列化 Hook descriptor 的 `System.Type`，导致两个插件回调失败并由宿主停用。改为仅记录类型名称；修复后 545/545 全量回归，随后成功进行两局实测。
4. `module-input-enum-failed/`：本构建裁剪了 `Enum.Parse(Type,String)`，保留三参数重载。首次修正后继续发现旧 `Input.GetKeyDown` 的真实调用抛异常，见 `legacy-input-failed/`；异常文本目前只记录到调用方法，未确证异常内部详情。
5. `input-position-instance-failed/`：改用 Input System 后键盘注册成功，鼠标失败为 `Vector2Control.ReadValueAsObject/0`。本构建保留的是静态 `InputControlExtensions.ReadValueAsObject(InputControl)`，已改为精确调用。
6. `input-edges-unverified/`、`input-diagnostics/`：键鼠 API 当前探测和读取成功；自动 F8/点击暂未产生回调，指针采样仍为零。有界诊断区分设备存在、原生调用成功、焦点和实际输入边沿；没有把探测成功升级为功能验收。

后续原生按钮安装包为 `package-manifest-native-button.json`，安装前17文件哈希见 `installation-check-native-button.json`；冻结会话有2434条事件、8组音频调用与释放、1次导航、2次真实开局，完整限制见 [native-button](native-button/README.md)。该会话的26条审计内容被2048字符日志上限截断，不能当完整审计通过；新探针改为有界紧凑摘要。

最新源码增加完整暂停链/独立gate、两个材质参数profile、retained Retry取消观察及绑定身份的Run scope。全量 **582/582、24项目** 见 `cancellation-final-build-summary.json`；仅诊断说明再调整后Game54重测，最终重打包见 `package-final.log` 与 `package-manifest-latest-candidate.json`。**该新候选尚未安装**：电脑操作工具查询窗口连续超时，重置后仍失败；旧游戏仍在运行。静态/离线证据不代替后续原生控制、材质和取消验收。
