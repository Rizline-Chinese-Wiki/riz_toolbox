# 644 检查包：资源安全、身份注册表及输入初始化失败

2026-10-20。此目录保留第一次 644 包的独立证据；后续移除已裁剪版本接口的修订见相邻 `runtime-input-capture-fix-20261020/`，不能混用两包结论。

## Evidence

- E-build：`build.log` / `build-summary.json`，25 个检查项目，644/644。Visual 55、UI/input 52、Hooks 27、新 Judgements 17。`build-initial.log` 是最终源码小改前的成功构建；随后完整重跑，只有后者用于正式包。测试量不代表能力覆盖率。
- E-install：`package-manifest.json` / `installation-check.json` / `install.log`，启动前核对 17 项实际发布文件 SHA-256。
- E-init：`input-init-failure-final-session/`，诊断 session `20261020T122239690Z-278bb6f29ebf405cb36acb20a398bedd`，640 条连续事件。第 71 条明确记录 `Application.get_unityVersion/0` 缺失，输入采集没有安装；设备返回 Unsupported，注册失败。不得宣称本包 F8 通过。较早的 497 条导出保留在 `input-init-failure-session/`。
- E-direct：`direct-start-session/` 仅有 2 条启动事件，`direct-start-Player.log` 记录界面前关闭。随后 Steam 启动确实进入了原游戏，但代理 DLL 存在时 CLR/宿主未加载。
- E-environment：`steam-launch-environment.json` 是对选定实际游戏进程的只读检查，工作目录正确；允许列表内 `DOORSTOP_DISABLE` 和 `DOORSTOP_INITIALIZED` 均为 TRUE。其他环境值未导出。
- E-launcher：`launcher-self-test.json` 记录 8/8、不创建进程的启动准备检查；`launcher-last-launch.json` 记录实际子进程 25388，随后 E-init 的新宿主 session 初始化成功。启动器记录本身不证明宿主成功，须连同该新 session 使用。

较早 607 包的补充会话原样保留于 `predecessor-session/`（1737 条事件），其所属包仍是 `runtime-material-slots-20261020/`。不能把其中未归属操作人的动作当作本次新包自动测试。

## Finding → Path

F-resource（validated，生产类离线）：恢复 setter 失败时立即销毁克隆并不安全。现用独立 assignment pins 与原始 baseline、进程级有界 retirement 保留原生根/依赖/预算，直到赋值真正解除或原生目标死亡。最后一个 override 恢复失败不再被后续 idle refresh 收为新 baseline。实际原生失败矩阵、可见像素和 GPU 仍未验收。

F-identity（validated，内部注册表离线）：同一保留 Note 对象的多次判定共享 run 内身份，跨局隔离、图引用歧义/缺失、捕获重入、在途结束延后释放均有回归。注册表和原生 helper 尚未接入完整公开判定服务；见 `docs/Judgement-observation.md`。

F-input（validated，实机失败）：E-init → 输入构造依赖一个实际未保留的接口 → 没有 after-input 采集，也没有成功的按键动作证据。后续包改用已经固定哈希的 GameAssembly 和 globalgamemanagers 校验，随后必须重新实测。

F-launch（validated，本机）：E-environment → 启动子进程清除四个继承的 Doorstop 初始化键并指定正常 Steam 应用身份 → E-init 的实际宿主恢复。没有修改 Steam、全局环境、原版二进制或持久 disable switch。启动器独立于游戏安装包，需要 .NET 8；不代表整个 B19 已完成。

## 临时构建目录归档

C 盘曾剩余 0 字节。自动批准审查拒绝删除临时目录，返回 `blocked by policy`。改为完整迁移两个本轮生成的 SDK 分发测试目录，保留文件：

- `artifacts/sdk-distribution-3906d908f91e44eab9dfc868c917f6ec` → `D:/RizToolbox-build-cache/sdk-distribution-3906d908f91e44eab9dfc868c917f6ec`
- `artifacts/sdk-distribution-06738555aa64441bacdd394fde9432f8` → `D:/RizToolbox-build-cache/sdk-distribution-06738555aa64441bacdd394fde9432f8`

正式源码、游戏包和本目录实测证据保留在桌面仓库。旧日志中的测试目录路径仍指向迁移前位置，复查对应文件时使用上面的归档位置。
