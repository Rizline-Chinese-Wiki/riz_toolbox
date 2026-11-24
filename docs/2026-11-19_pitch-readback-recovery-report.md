# 暂停倍率读回失败后的精确恢复（2026-11-19）

已修复一个可离线验证的 R06 功能缺口：原生 `SetPitch` 正常返回并实际退出之后，第一次 API 读回失败或返回值不符，原有实现会立即撤销源并丢失恢复所需的 continuation。当前实现保留该真实写入凭据，让同一个事务有一次写回原倍率的机会；只有完整实际读回通过才记录恢复。请求自身仍是失败请求。

源对象现在登记唯一 `PitchWrite`，并在整个准备、提交和恢复期间保留原源操作。读回尚未完成时，旧 API receipt 不能被读取或当作权限；当前 provider 验证实际注册 writer 的原生 fences 和当前 Clock policy。内部恢复接口由持有该能力的真实 writer 经过 provider 包装层传递，既有 owned 模拟 provider 继续使用其原有恢复合同。

恢复前会重新核对原控制器、歌曲、源、七根对象、原生 writer/source/scheduling fences、暂停状态、媒体位置、进度、偏移、片段、stock origin 和 DSP。该步骤不要求损坏的 pitch getter 先恢复；原倍率必须在原生恢复写入后由完整独立 API 读回确认。实际读失败、进度变动或权限变化时，不会从最终 pitch 等于原值推断恢复成功。getter 内重入写入、恢复、释放，以及 copied/worker 操作均拒绝。

最终执行为 [build-and-checks-r7](../work/r06-pitch-readback-recovery-20261119/build-and-checks-r7/results.json)，验收汇总为 [validation-r7.json](../work/r06-pitch-readback-recovery-20261119/validation-r7.json)。Release solution 自然退出 0，0 errors、55 条测试编译警告；独立 stock provider 测试项目自然退出 0，0 errors、0 warnings。Gameplay **238/238**；19 个实际 owned 原生场景 **1086 项断言**全部通过。1788 项当前输入、受测 DLL 和原生夹具执行前后身份一致；用户 README 保持原 SHA256。

| 验证范围 | 当前结果 |
| --- | --- |
| 原生完成后的单次 pitch/DSP 读取失败 | 原倍率实际写回并完整读回；请求 Rejected，journal 为 Restored |
| 首次 rooted 读回通过，随后独立 participant 读取失败 | 私有连续写入凭据仍可恢复 |
| API 返回 pitch 不符 | 保留凭据并写回原倍率，不把不符读数当提交成功 |
| pitch 永久读失败，包括恢复 setter 后仍失败 | 原倍率实际内存可能已恢复，但命令 Unknown，不重试、不伪造读回 |
| 读期间同值外写、ABA、源替换或进度变动 | 旧恢复不写入；Unknown，保持真实 native fences 与释放责任 |
| 原生写后 SEH、Clock publication 撤回、GetTime original 在途 | 保持既有 Unknown 与真实物理责任；不猜测 post-store 权限 |
| 普通提交、Replay/Resume、外部 SetTime、准备后释放 | 当前字节实际执行通过，已排除第二 prepared writer 借用 |

历史失败保留在证据目录：r1 漏传 owned-call 夹具环境变量；r3 把正常退出的外写误要求为物理 Unknown；r4 使用错误的 progress fixture key；r6 旧测试尝试同时登记两个连续 writer。它们都未被改写成通过；最终 r7 使用实际 retained participant 和正确夹具注入重新执行。

本轮没有安装、启动或操作游戏，没有操作账号，也没有修改封存的 R08 预览包。原生用例使用自有 stock originals 和 synthetic metadata；**没有覆盖正式 Fresh/watch/SuppressObserver、固定游戏 consumer 正向或实机成本**。R06、R07、R08 实机项、真实 Speed/Seek、完整 B01–B19 与 production 继续未完成。下一离线工作回到正式 Fresh/watch/固定 consumer 的实际入口切片，不重复本批恢复或 R08 已闭合交付。

证据目录还记录了代理设置 `gpt-6.1-sol / max`。本轮任务传递未产生有效审查交付，实际代码和验证均由 root 完成，没有把空回复计为审查。
