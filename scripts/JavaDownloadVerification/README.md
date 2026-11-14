# Java 下载验证

在源码目录执行：

```powershell
dotnet run --project scripts/JavaDownloadVerification -- ../Temp/java-download-check --live
```

使用本地 HTTP 测试完整下载、校验、解压、中文和空格路径、不覆盖已有安装、取消清理以及 ZIP 路径穿越。测试包中的 EXE 是文本占位，不执行程序。`--live` 额外验证官方版本列表、Temurin Java 8 和 Oracle 最新版本的下载元数据及校验值，不下载完整在线 JDK。

同时添加 `--install` 会将 Oracle 最新 JDK 下载、校验并解压到测试目录下的 `LiveOracle/Java版本号`，不会注册到用户 Java 配置或运行安装包。
