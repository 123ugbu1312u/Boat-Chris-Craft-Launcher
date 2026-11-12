# CPU 优化原生验证

在源码根目录运行：

```powershell
dotnet run --project scripts/CpuOptimizationVerification -c Release -- --native ../Temp/cpu-verification
```

使用生产协调器和核心 Windows API，运行 22 项验证：设置失败回滚、插电/电池模式回读、游戏 HighQoS/优先级/动态提升、核心亲和性不变、新游戏自动应用、状态持久化、恢复原值、尊重外部电源修改，以及启动器进程退出后由新进程恢复仍运行游戏的原设置。

该命令会短暂更改实际 Windows 性能电源模式，并对工具自行创建的等待进程应用游戏策略。测试结束时恢复原电源和进程设置，不使用真实 Minecraft。运行前将电源快照写到指定隔离目录的 `power-before.json`；正常完成时输出所有检查通过和已恢复提示。此工具不是单核跑分，不提供 FPS 或性能提升百分比。
