# WinHTTP builder / parent runner 有限静态审查

2026-10-25。仅只读核对 [Build-WinHttpForwardFixture.ps1](../../../scripts/Build-WinHttpForwardFixture.ps1)、[Test-WinHttpForwardFixture.ps1](../../../scripts/Test-WinHttpForwardFixture.ps1) 与当前冻结 native/宿主接口；未运行 builder、子进程、游戏或安装。本页不是编译/测试通过证明。

结论：当前两个脚本与七/八导出、1248 字节 Info、fixture selector、宿主 CLI 和固定 21 个 cases 的接口一致；没有发现需要阻止首编的脚本错误。

- Builder 从正确 canonical `native/RizToolbox.Doorstop/riz_winhttp.{h,c}` 与 ASM 取源。C 以 `/TC /std:c11` 编译，production 无 fixture 宏，fixture 精确宏值 1。ASM 与 C 内部 active pointer 符号直接链接。
- `.def` 只含 `LIBRARY`，不重复 C 的 dllexport 或 ASM 名称的四条 linker directives。实际七/八导出交由独立 PE verifier 核实，内部 active pointer 不应成为导出。
- Forward 两 profile 均 `/NOENTRY /NODEFAULTLIB /OPT:NOICF /GS- /Zl`，链接 Kernel32 与单项 SEH import lib；默认库不可偷偷补齐 CRT。宿主/Importer 则明确 `/MD`，不能把整个测试链描述为无 CRT。
- Importer 的普通 import lib 来自 production profile，导入 DLL 名与两个 profile 相同；宿主先以绝对 profile 路径载入选定 forward DLL，再载入 Importer。fixture-normal 应由实际 module/slot/四名 PE 证明采用已加载的 fixture DLL，不能仅凭 import lib 路径断言运行的是 production。
- 宿主带 `RIZ_WINHTTP_FIXTURE=1` 编译只是声明 setter 类型，运行时以 `--fixture` 控制要求 setter 存在；production-normal 不带该参数，并明确拒绝多出 setter。两 profile 均以 GetProcAddress 取得自有组件 API，没有静态导入 setter 导致 production 无法加载的依赖。
- Parent CLI `--forward/--output/--fault/--fault-index/[--fixture]/[--importer]` 与实际宿主参数解析相同。两个正常 profile，加 1..10 共 19 个固定 fault selectors，为 21 个独立 children；仅 5/6/10 展开四个索引，其他阶段索引 0。
- Builder 的四 binary 行为 production/fixture forward、Importer、Host。Parent 验证这四条精确 path/profile/role，持有其文件、manifest 中七 source、runner/verifier/固定旧 PE reader/Python/manifest/系统 WinHTTP，使用 ShareRead 阻止写入和删除，并在全部 cases 后重算 held hash。
- 旧 PE reader 通过 verifier 内固定 SHA 的四 AST 定义提取，Parent 也持有该实际文件；不存在依赖未锁的另一个本地 verifier 源文件。Python 标准库、系统 API、SDK 和完整工具链闭包不由这个锁集合证明。
- Parent 只启动已声明自有宿主，超时只终止本次 Process 对象；原始 process JSON 与 stdout/stderr 保留。Native 返回 0 后仍执行独立 verifier，不以宿主的 `passed` 或命令日志替代实测证据。

保留一个 provenance 边界：Builder 的 source manifest 在编译后才计算源 hash，并没有自身在每个命令前后锁定输入。因此 root 的正式 613 源 pre-freeze 与前后核对仍是必要的独立凭据；不能单凭 builder manifest 声称任意并发编辑场景下“编译时输入等于编译后源”。当前 root 已要求全源冻结，此点不要求为本轮首编改脚本。

本页核对时 runtime verifier 仍属于其作者负责的收尾文件；本页不签其所有语义断言或未产生的进程结果。故障 native 源保持冻结，后续如有真实编译错误由 root 给终态后再协调修改。
