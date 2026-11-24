# Doorstop native init 接线

固定 UnityDoorstop `33dab9a6733862eb81869ff08431d9478b28784b`，修改日期 2026-10-24。上游源码和工具摘要见 [upstream.json](upstream.json)，分发说明与独立对应源码构建命令见 [RIZLINE-MODIFICATIONS.md](RIZLINE-MODIFICATIONS.md)。

生产路径为：上游 `init_il2cpp` → `RizBeforeIl2CppInit` 的真实 producer 判定 → `RizInvokeIl2CppInit` → 原 init 一次 → Ready 且原 int 非零时才调用原 CoreCLR bootstrap。原返回值和 native SEH/C++ 异常保留。重复调用与递归在第二次原 init 前 fail fast；此内部 guard 不证明首次安装的启动前提。

在仓库根目录构建和暂存修改版 Doorstop：

```powershell
./scripts/Build-Doorstop.ps1 -OutputDirectory D:/RizToolbox-build-cache/doorstop-init-contract
```

它固定下载并校验上游源码和 xmake 2.6.1，在 MSVC 2019 / SDK 10.0.19041.0 下构建，生成 `winhttp.dll`、LGPL 许可证及包括全部四个生产 C/H 文件的对应源码 ZIP。不会安装到游戏。

独立验证真实生产 init-call C 文件：

```powershell
./scripts/Test-DoorstopInitNative.ps1 -OutputDirectory D:/RizToolbox-build-cache/doorstop-init-call-fixture
```

fixture 直接编译并链接 [riz_init_call.c](riz_init_call.c)，覆盖 12 个返回/异常案例和 4 个由监督进程核验实际退出码的 fail-fast 案例。子进程隐藏窗口并关闭 WER 界面。Ready 输入受控，bootstrap 为原生测试函数；结果不证明真实 producer、CoreCLR、游戏初始化或游戏冷启动已经验收。
