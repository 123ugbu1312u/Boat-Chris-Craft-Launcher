using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using BCCL.Core.Utils.OS;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCCL.Core.Test.Utils;

[TestClass]
public class MemoryOptimizerTest
{
    private sealed class Backend : IMemoryOptimizationBackend
    {
        public bool IsAdministrator { get; set; } = true;
        public readonly List<string> Calls = [];
        public int FailAt;
        public Action? DuringCommand;
        private int _reads;
        public ulong GetAvailableMemory() { Calls.Add("read"); return ++_reads == 1 ? 1000000000UL : 1300000000UL; }
        public IDisposable EnablePrivilege(string privilege) { Calls.Add(privilege); return new Lease(() => Calls.Add("restore")); }
        public void ExecuteCommand(int informationClass, int command)
        {
            Calls.Add($"{informationClass}:{command}");
            DuringCommand?.Invoke();
            if (command == FailAt) throw new InvalidOperationException("Native operation failed");
        }
    }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }

    [TestMethod]
    public void PreservesOriginalPrivilegeAndThreeCommandOrder()
    {
        var backend = new Backend();
        var result = new MemoryOptimizationRunner(backend).Run();
        CollectionAssert.AreEqual(new[] { "read", "SeProfileSingleProcessPrivilege", "80:2", "80:3", "80:4", "restore", "read" }, backend.Calls);
        Assert.AreEqual(300000000L, result.AvailableChange);
    }

    [TestMethod]
    public void NonAdministratorDoesNotChangeMemoryOrPrivileges()
    {
        var backend = new Backend { IsAdministrator = false };
        var runner = new MemoryOptimizationRunner(backend);
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => runner.Run());
        Assert.HasCount(0, backend.Calls);
        backend.IsAdministrator = true;
        runner.Run();
        Assert.IsTrue(backend.Calls.Contains("80:4"));
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void FailureRestoresPrivilegeAndAllowsRetry(int failAt)
    {
        var backend = new Backend { FailAt = failAt };
        var runner = new MemoryOptimizationRunner(backend);
        Assert.ThrowsExactly<InvalidOperationException>(() => runner.Run());
        Assert.AreEqual("restore", backend.Calls[^1]);
        backend.Calls.Clear();
        backend.FailAt = 0;
        runner.Run();
        Assert.IsTrue(backend.Calls.Contains("80:4"));
    }

    [TestMethod]
    public async Task ConcurrentClickDoesNotStartAnotherOptimization()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var backend = new Backend { DuringCommand = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); } };
        var runner = new MemoryOptimizationRunner(backend);
        var first = Task.Run(runner.Run);
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
            Assert.ThrowsExactly<InvalidOperationException>(() => runner.Run());
        }
        finally { release.Set(); }
        await first;
    }

    [TestMethod]
    public void ExitCodeMatchesOriginalKilobyteProtocolWithoutOverflow()
    {
        Assert.AreEqual(1, new MemoryOptimizationResult(0, 1024).ToExitCode());
        Assert.AreEqual(0, new MemoryOptimizationResult(4096, 2048).ToExitCode());
        Assert.AreEqual(int.MaxValue, new MemoryOptimizationResult(0, (ulong)int.MaxValue * 4096).ToExitCode());
    }

    [TestMethod]
    public void NativePrivilegeLookupAndTokenLayoutMatchWindowsAbi()
    {
        var type = typeof(MemoryOptimizer);
        var token = type.GetNestedType("TokenPrivileges", BindingFlags.NonPublic)!;
        Assert.AreEqual(16, Marshal.SizeOf(token));
        Assert.AreEqual(new IntPtr(4), Marshal.OffsetOf(token, "Luid"));
        object?[] args = [null, "SeProfileSingleProcessPrivilege", 0L];
        var lookup = type.GetMethod("LookupPrivilegeValue", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.AreEqual(true, lookup.Invoke(null, args));
        Assert.AreNotEqual(0L, args[2]);
    }

    [TestMethod]
    public void NativeMemoryApiRejectsInvalidClassWithoutOptimizingTheSystem()
    {
        var native = typeof(MemoryOptimizer).GetMethod("NtSetSystemInformation", BindingFlags.NonPublic | BindingFlags.Static)!;
        object[] args = [-1, 0, 4];
        var status = (uint)native.Invoke(null, args)!;
        Assert.AreNotEqual(0U, status);
    }
}
