# 原版下载验证

在 GPTcode 运行：

```powershell
dotnet run --project scripts/DownloadVerification -c Release -- ../Temp/vanilla-download-verification --live
```

直接编译生产下载器、分段恢复、源采样及共享额度代码，使用隔离的配置、日志、请求签名与文件校验适配器，不加载启动器账号或修改游戏目录。默认验证 20 MiB 分段文件、Range 不支持时回退、取消后清理；`--live` 额外从官方清单获取 1.21.1 客户端、16 个资源和 8 个支持库，并对大小及 SHA-1 独立核验。输出都位于指定目录。
