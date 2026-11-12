# 本地模组列表验证

在 Windows x64、.NET 10 SDK 下，从源码根目录运行：

```powershell
dotnet run --project scripts/LocalModListVerification -c Release -- --verify
```

使用真实 WPF 控件、应用资源和生产列表代码；配置提供方在内存中，不读取账号，不启动联网详情加载，不改动现有游戏文件。启停用夹具只写入输出目录下的 `verification-data`。

默认测量 128 / 1000 条合成记录的构建、刷新与布局：优化版仅创建前 24 行内容，全部行仍保留身份和选择状态。`--verify` 运行 17 项回归，覆盖最后一行滚动补齐、屏幕外全选、筛选、搜索、排序、Minecraft 文本格式、屏幕外模组启停用及共享加载器结果归属。回归在 WPF 消息循环内执行。

2026-11-04 同机 Release 测量：128 行约 340.8 → 113.5 ms，1000 行约 2429.3 → 73.7 ms。此数字仅表示界面构建/刷新/布局耗时，不代表整合包从打开到可操作的总时间；每次运行会有波动。原始日志与源码/EXE 备份保存在工作区上级 `Temp/local-mod-list-speed`。
