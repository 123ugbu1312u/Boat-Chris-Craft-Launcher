using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace BCCL.Core.Link.MCTier;

/// <summary>Private IPC across UAC elevation, restricted by ACL and verified endpoint process IDs.</summary>
public static partial class MCTierControlPipe
{
    public static NamedPipeServerStream CreateServer(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new IOException("无法识别当前 Windows 用户。");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        // CurrentUserOnly also checks elevation on Windows and rejects the intended elevated helper.
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, security);
    }

    public static NamedPipeClientStream CreateClient(string name) => new(".", name, PipeDirection.InOut,
        PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);

    public static void VerifyClient(NamedPipeServerStream pipe, int expectedProcess)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var actual)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (actual != expectedProcess) throw new IOException("联机辅助进程身份不匹配，已拒绝连接。");
    }

    public static void VerifyServer(NamedPipeClientStream pipe, int expectedProcess)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actual)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (actual != expectedProcess) throw new IOException("启动器控制通道身份不匹配，已拒绝连接。");
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint process);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint process);
}
