# 资源连续性＋相机缩放联合准备

联合915输入已物理组装并通过fresh solution编译：0错误／56既有警告。相对已通过验证的V，唯一源码差异是资源SDK测试中显式释放旧prepared reservation的Dispose修补；485产品输入及48原生输入保持相同。六组件的12份DLL/PDB与V的实际solution产物逐字节一致，本包没有重复跑旧功能矩阵。

待合差分为U53路径，再合V9路径；重叠1路径后总共61路径（21新增／40修改）。[三个源码ZIP](retained/preparation-result.json)分别保存主目录改前894、包含Dispose的U913、完整联合915；[分阶段差分](retained/staged-deltas.json)和01／02补丁明确各自before／after hash。不能用旧V全树ZIP覆盖该修补。

本包passed只表示准备完整性和编译结果，`mainApplied=false`、`jointBehaviorValidationComplete=false`、`integrationAdmissionVerified=false`。原准备r1曾误读映像证据索引的passed字段，实际exit1且尚未创建候选目录；原件在failed-preparation-r1，修正r2按实际325检查、六个actual0、最终hash guards和completed audit验证，没有放宽映像要求。

下一先等U当前SDK及独立审计、T完整18SDK及closure的四个真实结果，并逐项核原件。全部通过后仍需按顺序精确合主、fresh整合验证；本包没有应用文件，也不运行或安装游戏。B01–B19、完整generation／未知lower／production／Seek／effect仍未完成。
