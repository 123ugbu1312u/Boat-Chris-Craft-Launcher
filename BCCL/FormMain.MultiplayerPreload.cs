using System;
using System.Windows.Threading;

namespace BCCL;

public partial class FormMain
{
    private bool multiplayerPreloadScheduled;

    private PageMultiplayerOriginal InitializeMultiplayerWorkspace()
    {
        return multiplayerPage ??= new PageMultiplayerOriginal(MultiplayerSurface);
    }

    private void PreloadMultiplayerOnLoaded(object? sender, EventArgs e)
    {
        if (multiplayerPreloadScheduled) return;
        multiplayerPreloadScheduled = true;
        // Loaded runs before the first usable frame. Start extraction immediately,
        // and build the permanent native host at the real workspace size now.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!Dispatcher.HasShutdownStarted) _ = InitializeMultiplayerWorkspace().PrepareAsync();
        }));
    }
}
