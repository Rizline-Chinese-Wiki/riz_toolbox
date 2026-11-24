# 788包：返回场景白名单

788/788、25套回归、0编译警告/错误，18项安装hash匹配、268源码输入未变，原游戏三个文件before/after哈希相同。支持MainMenu/FakeMainMenu的同一真实组件图验证、实际Scene元数据及菜单输入上下文；新增回归证明旧窗口/焦点/排队输入撤销和新菜单输入派发。

实测session `20261021T100328262Z-0637fccb72724092a5d1f3bfe13a0319` 发现LOCAL探针迟到公共场景通知丢失活窗口句柄，导致稳定ID Conflict。完整325条日志、0Error保存在 `../runtime-return-menu-20261021/initial-scene-window-conflict-red-20261021T100535Z/`；0Error不表示功能通过。游戏已正常退出。后续显式Dispose探针修复及79项UI回归属于下一789包，不能补证本次失败会话。
