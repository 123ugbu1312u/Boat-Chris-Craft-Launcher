using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace BCCL.Diagnostics;

// Package smoke test runs before launcher services, accounts and configuration load.
internal static class PopupVerification
{
    public static int Run(string? coordinationDirectory)
    {
        try
        {
            if (coordinationDirectory is not null)
            {
                Directory.CreateDirectory(coordinationDirectory);
                File.WriteAllText(Path.Combine(coordinationDirectory, "ready"), "ready");
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (!File.Exists(Path.Combine(coordinationDirectory, "continue")))
                {
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("Popup verification coordination timed out.");
                    Thread.Sleep(50);
                }
            }
            foreach (var name in new[] { "Accessibility", "LiteDB" })
            {
                var assembly = Assembly.Load(name);
                if (string.IsNullOrEmpty(assembly.Location) || !File.Exists(assembly.Location))
                    throw new InvalidDataException($"{name} was not extracted to a stable runtime path.");
                Console.WriteLine($"PASS: {name} loaded from the extracted runtime cache.");
            }
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            for (var i = 0; i < 3; i++)
            {
                var popup = new Popup
                {
                    AllowsTransparency = true, StaysOpen = true,
                    Placement = PlacementMode.Absolute, HorizontalOffset = -10000, VerticalOffset = -10000,
                    Child = new TextBlock { Text = "BCCL popup verification" }
                };
                popup.IsOpen = true;
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                if (PresentationSource.FromVisual(popup.Child) is null)
                    throw new InvalidOperationException("Popup did not create its native window.");
                popup.IsOpen = false;
            }
            app.Shutdown();
            Console.WriteLine("PASS: transparent WPF popup opened and closed three times.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
