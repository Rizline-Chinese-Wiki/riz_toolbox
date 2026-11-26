# 修改版 Doorstop（原生初始化接线）

基于上游 UnityDoorstop 提交 `33dab9a6733862eb81869ff08431d9478b28784b`（2026-10-24 修改）。

- 上游源码与工具版本摘要：[upstream.json](upstream.json)
- 改动说明与独立源码构建方法：[RIZLINE-MODIFICATIONS.md](RIZLINE-MODIFICATIONS.md)

## 初始化流程

实际运行时的调用顺序为：

`上游 init_il2cpp` → `RizBeforeIl2CppInit`（判断前置条件是否就绪）→ `RizInvokeIl2CppInit` → 调用原 `init` 一次 → 仅当已就绪且原 `init` 返回非零时，才执行原有的 CoreCLR bootstrap。

- 保留原函数的返回值以及原生 SEH / C++ 异常；
- 如果发生重复调用或递归，会在第二次进入原 `init` 前直接 fail-fast 中止（这只是内部防重入保护，不代表首次启动的所有外部前提都已满足）。

## 构建修改版 Doorstop

在仓库根目录执行：

```powershell
./scripts/Build-Doorstop.ps1 -OutputDirectory D:/RizToolbox-build-cache/doorstop-init-contract
```

脚本会自动下载并校验固定版本的上游源码和 xmake 2.6.1，用 MSVC 2019 + Windows SDK 10.0.19041.0 编译，输出 `winhttp.dll`、LGPL 许可证以及包含全部 4 个 C/H 源码文件的对应源码包。**不会自动安装到游戏目录。**

## 离线测试

单独验证生产用的 [riz_init_call.c](riz_init_call.c)：

```powershell
./scripts/Test-DoorstopInitNative.ps1 -OutputDirectory D:/RizToolbox-build-cache/doorstop-init-call-fixture
```

测试夹具会直接编译链接 `riz_init_call.c`，覆盖 12 个返回值/异常用例，以及 4 个由父进程检查退出码的 fail-fast 用例（子进程无窗口且关闭 WER 弹窗）。

> 说明：这是离线单元测试，就绪状态和 bootstrap 都是测试替身，通过它不代表真实游戏冷启动或 CoreCLR 已经实机验收。
