# 内存优化实现来源

功能依据用户提供的 `Plain Craft Launcher 2.exe` 中 `PCL.PageOtherTest.MemoryOptimizeInternal`、`MemoryOptimize` 及 `PCL.Application` 的 `--memory` 分支移植。原实现及署名归 PCL / 龙腾猫跃及其权利人所有；已有上游许可文件继续保留。

输入 EXE SHA-256：`30EB93C62FC51EDE0898648E67455478A3D5A4F279938AE65AEC77B84CB9703F`。

保留的核心流程：管理员检查、启用 `SeProfileSingleProcessPrivilege`、以信息类 80 依次调用命令 2、3、4、读取优化前后可用物理内存。保留百宝箱按钮、重复执行保护、按需请求管理员权限与 `--memory` 返回可用内存增量 KiB 的入口。

适配：代码拆入 BCCL 核心库和异步 UI，原 VB 互操作改为 C# 的等价引用参数；原代码固定和释放装箱整数的操作改为直接传递四字节整数。完成或异常时恢复临时启用的令牌权限，子进程在 WPF 与单实例初始化之前执行。提示显示实测内存变化，不复制原提示中的 0.8 系数，也不承诺固定释放三分之一。静默入口失败通过负退出码和标准错误返回，不弹窗。

Windows 令牌状态的检查与恢复按 [Microsoft AdjustTokenPrivileges 文档](https://learn.microsoft.com/windows/win32/api/securitybaseapi/nf-securitybaseapi-adjusttokenprivileges) 处理。不关闭进程、不删除文件、不改变持久系统设置。

反编译检查结果、开发工具和完整原类仅留在工作区 Temp，不作为 BCCL 运行依赖，也不打入源码归档。
