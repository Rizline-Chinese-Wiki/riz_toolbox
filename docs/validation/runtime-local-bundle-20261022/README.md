# 两个独立 LOCAL 资源包的真实生命周期

2026-10-22。会话 **`20261022T112318729Z-5dbc31fb155143ef844ba320308c4064`**，完整 **879 连续事件：845 Info、27 Debug、7 Warning、0 Error**。[校验结果](runtime-proof.json)基于完整停止会话及实际 18 文件安装清单，不截取成功片段。

两套资源均构造成功，撤回私有 provider/catalog/content 和 caller request 后，经真实 IL2CPP GC 仍保持读回；先销毁第一套不影响第二套，最后两套责任与 CPU template 全部退休。**没有注入或激活 LOCAL controller，也没有完成 LOCAL 游玩。**

## 可复核时序

| seq / frame | 观察 |
|---|---|
| 727 | 原创 WAV/PNG/一音符 chart 写入唯一私有测试目录；未改游戏曲库 |
| 728–729 / 13996 | 两图各有 9 个被记录的对象身份，两图共 18 个均不同；各 5 direct roots。真实来源 controller/JudgeData 与新图不同 |
| 824 / 13997 | 撤回来源与 caller request，两个 CPU fork 保持；原生 GC 后两图读回相同；第一套请求 Destroy，仍保留两套根 |
| 825 / 13998 | 第一套 Unity-dead 后释放根、拒绝再读；CPU prepared 变 1；再次原生 GC 后第二套读回不变，再请求第二套 Destroy |
| 840 / 14177 | 两套关闭、六个自有 Unity 资源确认 dead；10 direct roots、6 source observation roots 释放；prepared/song/content 各为 0 |

四个复制值为 `0.14 / 0.18 / 0.07 / 0.2`，分别对应 JudgeTime / NormalChallengeTime / MaxChallengeTime / BadTime；本轮没有验证这些字段的时间单位。每图音频 800 frames、单声道 8000 Hz，封面 1×1，chart 1 line/1 note，上传估算 4228 bytes。音频上传返回成功及几何读回不等于实际听觉/逐样本回读验收。

7 条 Warning 完整保留在 proof 和原始事件中：设置页、曲库等待及视觉目标容量/延后准入。普通关卡中存在真实开局、输入和暂停采样；这些不是 LOCAL 流程证据。probe 的 `gameResultOrSavePathInvoked=false` 限于 probe 自身没有调用该路径，不是全会话原版保存调用计数。

## 停止、恢复与证据限制

[进程停止记录](process-stop-check.json)确认游戏进程已经不存在。主代理没有观察到最终退出操作，不能证明正常菜单退出，也不推断终止原因。事件格式本身没有 shutdown 标记；Player.log 完整副本只作辅助记录。

[恢复记录](probe-restoration-check.json)确认新 marker 恢复原 Absent，其他五个 opt-in markers 仍 Absent。唯一测试目录中的三份原创文件先按字节冻结，再逐文件删除和移除空目录；没有递归删除。原始测试字节保存在会话子目录的 `original-test-fixtures/`。

只读重新验证完整会话：

```powershell
python docs/validation/runtime-local-bundle-20261022/freeze-proof.py 'D:\SteamLibrary\steamapps\common\Rizline\BepInEx\RizToolbox\diagnostics\20261022T112318729Z-5dbc31fb155143ef844ba320308c4064' --package-manifest docs/validation/foundation-local-bundle-20261022/package-manifest.json --stopped --expected-phase verified
```

`--stopped` 是调用方已独立核对进程停止的前置条件。脚本核事件连续性、全部阶段、对象身份、严格后续帧、CPU 计数、source 根计数以及安装 hash；不自行操作游戏、不把日志最后一行当退出证据。

根记录释放与 Unity dead 不证明 native heap/GPU 全回收或长期 GC/压力。LOCAL 激活、设置初始化、结果首次 Start 归属、Start/Retry/Back 保护实际接线、六 globals 发布/恢复、真实消费者结束仍是后续任务。B01–B19 没有因此新增勾选。
