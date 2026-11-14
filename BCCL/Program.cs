using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using BCCL.Core.App;
using BCCL.Core.App.Essentials;
using BCCL.Core.App.IoC;
using BCCL.Core.Utils.OS;

namespace BCCL;

internal static class Program
{
    /// <summary>
    /// Program startup point
    /// </summary>
    [STAThread]
    public static void Main()
    {
        // A silent elevated helper must run before WPF/lifecycle and the main launcher's single-instance lock.
        var commandArgs = Environment.GetCommandLineArgs();
        if (commandArgs.Length is 2 or 3 && commandArgs[1] == "--verify-popup")
        {
            Environment.ExitCode = Diagnostics.PopupVerification.Run(commandArgs.Length == 3 ? commandArgs[2] : null);
            return;
        }
        if (commandArgs.Length == 5 && commandArgs[1] == "--mctier-helper" &&
            int.TryParse(commandArgs[3], out var parentId) && long.TryParse(commandArgs[4], out var parentStart))
        {
            try { Multiplayer.MCTierHelper.RunAsync(commandArgs[2], parentId, parentStart).GetAwaiter().GetResult(); }
            catch { Environment.ExitCode = 1; }
            return;
        }
        if (commandArgs.Length == 2 && commandArgs[1].Equals("--memory", StringComparison.OrdinalIgnoreCase))
        {
            try { Environment.ExitCode = MemoryOptimizer.Run().ToExitCode(); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = -1; }
            return;
        }
        if (Basics.CommandLineArguments.Contains("--console")) KernelInterop.AllocateConsole();
#if DEBUG
        if (Basics.CommandLineArguments.Contains("--debug"))
        {
            Console.WriteLine("Waiting for debugger...");
            while (!Debugger.IsAttached) Thread.Sleep(50);
        }
#endif
        Console.WriteLine("Welcome to Boat Chris Craft Launcher (BCCL)!");
        // Preloading tasks
        ApplicationService.Loading = static () =>
        {
            // The single-instance lock has been acquired before this callback.
            // Migrate before configuration providers and the logger open files.
            if (string.Equals(Paths.Data,
                    Path.Combine(Basics.ExecutableDirectory, Paths.DataDirectoryName),
                    StringComparison.OrdinalIgnoreCase))
                DataDirectoryMigration.Migrate(
                    Path.Combine(Basics.ExecutableDirectory, LegacyCompatibility.LocalDirectoryName), Paths.Data);
            var app = new Application();
            app.InitializeComponent();
            return app;
        };
        MainWindowService.Loading = static () =>
        {
            var form = new FormMain();
            return form;
        };
        // From dotnet/wpf #2393: fix tablet devices broken on .NET Core 3.0+
        _ = Tablet.TabletDevices;
        // Start lifecycle
        Lifecycle.OnInitialize();
    }
}
