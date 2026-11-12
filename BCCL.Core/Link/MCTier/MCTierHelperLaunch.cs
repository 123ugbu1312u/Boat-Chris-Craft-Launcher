using System.Diagnostics;

namespace BCCL.Core.Link.MCTier;

public static class MCTierHelperLaunch
{
    public static ProcessStartInfo Create(string executable, string workingDirectory, string pipeName,
        int parentId, long parentStart, bool creatingRoom, bool virtualAdapter)
    {
        var elevated = creatingRoom || virtualAdapter;
        return new ProcessStartInfo(executable)
        {
            Arguments = $"--mctier-helper {pipeName} {parentId} {parentStart}",
            WorkingDirectory = workingDirectory, UseShellExecute = elevated,
            Verb = elevated ? "runas" : "", CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
    }
}
