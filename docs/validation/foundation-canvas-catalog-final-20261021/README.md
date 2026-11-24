# 787 final：Canvas投影与LOCAL目录

787/787、25套回归、0编译警告/错误，18项安装hash匹配、268源码输入当时未变。Visual71、AudioContent78、UI/input77；两个真实原生fixture分别31261/61358项，inFlight=0。原游戏三个文件before/after哈希均相同。

真实MainMenu的四角投影、owned-root补偿、同名歌曲独立选择、收藏、Play明确拒绝与进入LevelList释放已观察；返回FakeMainMenu未挂载，失败保留。实机session `20261021T094900103Z-5d3bca7a402a4aec8cefcda43ebf42a7` 正常退出后完整1060条、0Error在 `../runtime-canvas-catalog-20261021/normal-exit-menu-return-red-20261021T100230Z/`；此前935条部分快照未替换。6份临时文件已恢复，见该目录probe-files-restoration.json。

只读资源metadata完整13行已实际采集，其中Sprite闭ResourceReference ctor/Release身份与entry成立，AudioClip concrete和实际接口派发仍unknown。原生LOCAL音乐/封面播放、重试/结果链与完整B15尚未完成。详细证据见 `../runtime-canvas-catalog-20261021/README.md`。
