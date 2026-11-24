# 主菜单父 Canvas 精确重载修复包

本目录对应已完成的 778/778、25套回归，编译无警告或错误。包、源码、18项安装哈希和两个原生fixture明细见同目录 JSON；原游戏三个文件 before/after 哈希均相同。

该包修复实际玩家裁剪掉 `GetComponentInParent(Type)` 的问题，改为真实存在且精确核验的 `Component.GetComponentInParent(Type,bool)`。实机确实继续取得 Canvas，但随后旧布局的等比守卫拒绝创建 LOCAL 窗口。session `20261021T091529261Z-fd4e98c80f764ecba27751e8756db6b8` 的全637条、0Error、连续序号证据保存在 `../runtime-local-catalog-20261021/menu-parent-fixed-canvas-rejected-20261021T092329Z/`。0Error不意味着窗口创建通过，日志明确记录 WINDOW REFUSED。

测试结束已正常退出，三份探针配置及原创验证资产恢复原先不存在状态，见 `../runtime-local-catalog-20261021/final-probes-restoration.json`。新的四角投影实现和之后新增的边界测试不属于本目录这个已安装包，后续构建另存。
