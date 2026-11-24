# Finding 与 Path

## F-001

- title: v2 的完整新输入和指定离线验证已完成
- severity: info
- category: other
- status: validated
- evidence_ids: [E-001, E-002, E-012]
- location: runs/r2/result.json
- impact: 新 1172 输入图和辅助 Build 有精确记录；r2 只有 Runtime/Module/GameCamera 普通套件获得本次执行接纳。r1 其余四套件仅属 v1 历史。
- confidence: high
- repro_steps: 执行归档 checker；核对六个 r2 子步骤的实际退出、日志、源和控制器 SHA。
- remediation: 本结论限离线范围；后续源变更需要新执行，不复用旧套件的成功认证新输入。
- optional_attack: n/a

## F-002

- title: 无关或重入 invoke 只作前置检查；相关 tick 仍需新完整证明及第二次通道身份检查
- severity: info
- category: design
- status: validated
- evidence_ids: [E-003, E-004, E-013]
- location: RuntimeBridge.AcceptsTick / NativeRuntimeInvokeBridge.AfterAdmission.Observe
- impact: 4096 个无关调用受控测试没有完整 proof、SDK 分发或绑定；首次选择会在 proof 前读取缓存的元数据委托。每个接受 tick 保留当前完整 VerifyReady 和稍后 identity/in-flight 检查。没有并发原子保证；具体主机自身吞掉的 tick 异常继续由原路径局部记录。
- confidence: high
- repro_steps: 对照生产源码及 8 项新专项原始日志；checker 验证二者的冻结身份。
- remediation: 按新边界审查相关调用；不要将有限检查点顺序解读为覆盖任意并发变化的事务。
- optional_attack: n/a

## F-003

- title: 实际内置模块能共同注册，camera 正规数间隔保持逐次 advancing tick 重应用
- severity: info
- category: design
- status: validated
- evidence_ids: [E-005, E-006, E-007]
- location: GameCameraModule.PerHostTickIntervalSeconds / BuiltinManifestContractChecks
- impact: 不改变统一正数间隔校验。v2 用最小正正规 Double 和精确 bit 断言，避免 double.Epsilon 的次正规数依赖；未观测或修改实际 Unity 线程 FP 状态。负控只证明旧零间隔会被真实 camera 注册拒绝，不能替代 v2 新测试。
- confidence: high
- repro_steps: 核对 v2 overlay、Module31 日志、负控29/31以及 checker 的 source/hash 绑定。
- remediation: 后续模块清单或间隔变化需更新真实构造器聚合测试并重新执行。
- optional_attack: n/a

## F-004

- title: 当前 managed 新输出进入已完成安装包
- severity: info
- category: other
- status: validated
- evidence_ids: [E-008, E-009]
- location: install/result.json / install/package-manifest.json
- impact: 原始 installer wait=0，23 项精确安装记录含当前六 DLL、五处同一新 Examples；配置保留和游戏 SHA 未变由安装器记录。本归档不重新访问游戏验证。
- confidence: high
- repro_steps: checker 核对安装清单、23 项自有字节、当前六 DLL/PDB 与原 wait 验证依赖。
- remediation: 安装成功不能替代游戏初始化验收。
- optional_attack: n/a

## F-005

- title: 新一次游戏启动在 native 分配证明阶段阻断，managed startup 尚未进入
- severity: info
- category: other
- status: validated
- evidence_ids: [E-010, E-011]
- location: runtime-r1/native-preinit-current.log
- impact: PID15924 的 operation.prove.block-present stage145/row14 result0 使 producer 返回0；原始 IL2CPP 初始化仍返回1，游戏正常标题页响应并正常退出。没有当前 Loader 增量，无法据此评判 managed 两修正的游戏行为；SDK 启动未通过。
- confidence: high
- repro_steps: checker 绑定 snapshot 与 install result SHA；对照三条 PID15924 日志及空当前会话 Loader 日志。
- remediation: 继续单独处理 native allocation/proof 阻断；该后续实现和验证不在本包。
- optional_attack: n/a

## P-001

- title: 从冻结候选到有限游戏启动的接纳路径
- path_type: callflow
- start: 已接纳1171输入图与两个独立冻结overlay
- goal: 可携带地审查本次实际完成范围，保留未完成的SDK启动门槛
- steps:
  1. action: 组合1172项当前输入加辅助Build并运行指定r2构建/测试 — evidence: E-001, E-002 — finding: F-001
  2. action: 对固定内部消费者先筛选，再为相关tick做完整proof和第二通道检查 — evidence: E-003, E-004, E-013 — finding: F-002
  3. action: 通过真实30内置构造器和camera逐tick契约 — evidence: E-005, E-006, E-007 — finding: F-003
  4. action: 已完成安装器将当前six与Examples写入23项自有包 — evidence: E-008, E-009 — finding: F-004
  5. action: 有限启动观察在native stage145之前关闭managed验收 — evidence: E-010, E-011 — finding: F-005
- residual_risks: 未获得当前managed启动游戏行为；未做新完整managed matrix或Judgements；顺序检查不是并发事务；后续native修正需要自己的源与新执行证据。
