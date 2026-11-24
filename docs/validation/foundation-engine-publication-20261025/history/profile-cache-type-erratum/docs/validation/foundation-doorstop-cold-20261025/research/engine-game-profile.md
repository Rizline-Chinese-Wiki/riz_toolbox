# 固定引擎游戏profile：磁盘事实

2026-10-25。仅静态提取，profileVersion=1；不授首次写入、Configure、Ready或V4。已按ida-reverse路由核工具，当前无可调用IDA后端；实际使用Python/pefile/Capstone离线读取，没有加载DLL、构建、child、游戏/UI或安装。

## Evidence → Finding → Path

| Evidence | 实际发现 | Path与限制 |
|---|---|---|
| 当前四原始文件hash、[完整JSON](engine-game-profile.json) | EXE/GameAssembly/UnityPlayer/global-metadata固定SHA全部匹配，结束前再次核对未变 | 只限定此固定版本，Windows/游戏更新后重新提取 |
| [旧installation证据](../../foundation-engine-binding-20261025/research/engine-installation-evidence.json) | 四完整函数字节/hash、全部pdata行及三PE大小/hash逐项相等 | 本批补齐精确UNWIND_INFO及可引用常量，不改写旧原件 |
| [生成头文件](../../../../native/RizToolbox.EngineBootstrap/EngineGameProfile.h) | 定长constexpr身份、四prefix/完整body、六个unwind记录及全部RF链 | 不移除RIZFIXEDOWNEDV1旧fixture marker，也不调用publisher |

| kind | RVA–完整body末端 | 前缀长度 | 实际RF数 | ABI（无隐藏MethodInfo） |
|---|---|---:|---:|---|
| Start | 0x792240–0x7929bf | 15 | 1 | uint8(owner, enumerator, nameArgument, nodeOutputSlot) |
| Execute | 0x78f840–0x78fa2e | 18 | 1 | uint32(node, outputByteSlot) |
| Release | 0x78f680–0x78f768 | 16 | 3 | uint8(node) |
| Detach | 0x793840–0x793a5b | 15 | 1 | void(node) |

Release三个RF必须保留：primary 0x78f680–0x78f69d/unwind1ac7b88；第二段0x78f69d–0x78f74b/unwind1b27a7c和第三段0x78f74b–0x78f768/unwind1b27a90均CHAININFO回同primary。不能以一个虚构的连续RF替代。JSON含所有原始unwind字节、slot解码及chain三元组；本批四函数均无未定长语言特定handler数据。

UnityMain与UnityMain2同RVA808690；Unity两个TLS实际RVA17e7708/17e7780，完整有界函数/真实RF也保留在JSON。EXE普通导入UnityMain2的IAT为e240；UnityPlayer普通按名导入四个WinHTTP名称。没有因为有这条静态边就推导实际首次调用/系统初始化顺序。

固定 global-metadata.dat 的磁盘首DWORD不是标准IL2CPP magic（首32字节00c000c020f887004de059b56363dc57301b33844f63cc403cd9e4ca176766a2）。保留完整文件SHA/长度/原始前32字节；format未知、version=null，不能把不透明字节解释成metadata版本。本批未解码或加载该文件。

node owner58、enumHandle10/cache18/type20、refCount60来自现有native复制实现；managed m_CachedPtr10来自现有receiver root+metadata重验实现，并与旧固定icall机器码研究对应。enumHandle不是IEnumerator指针，refCount不是执行深度，不读original返回后的node；receiver root、GC/销毁竞争和实际owner活性仍须外部合同。这些字段仅声明已知标量来源，没有未知字段被填造。

当前缺口仍是完整启动准入/既有执行者历史、生产固定publisher和V4封存、真实GC/coordinator/scheduler/取消退休闭包。header明确FactsOnly=true、ProductionFirstInstallAuthorized=false；profileVersion1不是Kernel ABI2/scalar1，也不是fixture publisher2或early wire3。完整B01–B19未因此完成。

## 固定性及复核

从仓库根执行：

```powershell
C:/Python314/python.exe docs/validation/foundation-doorstop-cold-20261025/research/extract-engine-game-profile.py
```

该命令仅重读固定输入并重建本批三个输出；任何hash/字节/RF/chain/ABI来源断言失败即停止，不能忽略失败继续签事实。

- script SHA256：`3b9396e53809ceb631b28615d560a93e69c892b82c4f46dd0667c2c562208a37`
- JSON SHA256：`bff37c73acbd5775b9eb75e59e24e4094d32192b026121d121d742ec8a97430e`
- header SHA256：`09b97c3fd41f46ccfb99d95404a0eb70a129a8ba6a8fc9ce713aa2e46a52d1e5`
- canonical facts SHA256：`22ebf5a0e48a5e386dfca054dc3d96d9437268ea9d2d51462dbf84e3b3e9a478`
