using System.Security.Authentication;
using System.Windows;
using BCCL.Core.App;
using BCCL.Core.App.Localization;
using BCCL.Core.Minecraft.Profile;

namespace BCCL;

public partial class PageLoginMs
{
    public PageLoginMs()
    {
        // Handles
        InitializeComponent();
        BtnBack.Click += BtnBack_Click;
        BtnLogin.Click += BtnLogin_Click;
        BtnLoginSettings.Click += (_, _) => ConfigureMicrosoftLogin();
    }

    private void BtnBack_Click(object sender, EventArgs e)
    {
        ProfileService.IsCreatingProfile = false;
        ModBase.RunInUi(() => ModMain.frmLaunchLeft.RefreshPage(true));
    }

    private void BtnLogin_Click(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Secrets.MSOAuthClientId) && !ConfigureMicrosoftLogin()) return;
            _ = MicrosoftOAuthConfiguration.Normalize(Secrets.MSOAuthClientId);
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Launch.Account.Microsoft.Error.ClientNotConfigured"), ModBase.LogLevel.Msgbox);
            return;
        }
        BtnLogin.IsEnabled = false;
        BtnLoginSettings.IsEnabled = false;
        BtnBack.Visibility = Visibility.Collapsed;
        BtnLogin.Text = Lang.Number(0d, "P0");
        ModBase.RunInNewThread(() =>
        {
            var previousProfile = ProfileService.Current;
            var loginSucceeded = false;
            try
            {
                ProfileService.Select(null);
                ModLaunch.mcLoginMsLoader.Start(ProfileUi.GetLoginData(ModLaunch.McLoginType.Ms), true);
                while (ModLaunch.mcLoginMsLoader.State == ModBase.LoadState.Loading)
                {
                    ModBase.RunInUi(() => BtnLogin.Text = Lang.Number(ModLaunch.mcLoginMsLoader.Progress, "P0"));
                    Thread.Sleep(50);
                }

                if (ModLaunch.mcLoginMsLoader.State == ModBase.LoadState.Finished)
                {
                    loginSucceeded = true;
                    ModBase.RunInUi(() => ModMain.frmLaunchLeft.RefreshPage(true));
                }
                else if (ModLaunch.mcLoginMsLoader.State == ModBase.LoadState.Aborted)
                    throw new ThreadInterruptedException();
                else if (ModLaunch.mcLoginMsLoader.Error is null)
                    throw new InvalidOperationException(Lang.Text("Launch.Account.Microsoft.Error.Unknown"));
                else
                    throw ModLaunch.mcLoginMsLoader.Error;
            }
            catch (Exception ex) when (ex is ThreadInterruptedException or OperationCanceledException)
            {
                if (!loginSucceeded && previousProfile is not null) ProfileService.Select(previousProfile);
                HintService.Hint(Lang.Text("Launch.Account.LoginCancelled"));
            }
            catch (Exception ex)
            {
                if (!loginSucceeded && previousProfile is not null) ProfileService.Select(previousProfile);
                if (ex.Message == "$$")
                {
                }
                else if (ex.Message.StartsWith("$"))
                {
                    HintService.Hint(
                        Lang.Text("Launch.Account.Microsoft.LoginFailed.WithDetail", ex.Message.TrimStart('$')),
                        HintType.Error);
                }
                else if (ex is AuthenticationException && ex.Message.ContainsF("SSL/TLS"))
                {
                    ModBase.Log(
                        ex,
                        $"{Lang.Text("Launch.Account.Microsoft.LoginFailed.Message")}\r\n{ex.Message}",
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Launch.Account.Microsoft.Error.OperationFailed"));
                }
                else
                {
                    ModBase.Log(
                        ex,
                        Lang.Text("Launch.Account.Microsoft.LoginFailed.Title"),
                        ModBase.LogLevel.Msgbox,
                        userSummary: Lang.Text("Launch.Account.Microsoft.LoginFailed.Title"));
                }
            }
            finally
            {
                ProfileService.IsCreatingProfile = false;
                ModBase.RunInUi(() =>
                {
                    BtnLogin.IsEnabled = true;
                    BtnLoginSettings.IsEnabled = true;
                    BtnBack.Visibility = Visibility.Visible;
                    BtnLogin.Text = Lang.Text("Launch.Account.Login");
                });
            }
        }, "Ms Login");
    }

    private static bool ConfigureMicrosoftLogin()
    {
        try
        {
            string existingId;
            try { existingId = MicrosoftOAuthConfiguration.Read(MicrosoftOAuthConfiguration.FilePath); }
            catch { existingId = string.Empty; } // Permit replacing a malformed local configuration.
            var input = ModMain.MyMsgBoxInput(Lang.Text("Launch.Account.Microsoft.Settings.Title"),
                Lang.Text("Launch.Account.Microsoft.Settings.Description"),
                defaultInput: existingId,
                hintText: "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx");
            if (string.IsNullOrWhiteSpace(input)) return false;
            MicrosoftOAuthConfiguration.Save(MicrosoftOAuthConfiguration.FilePath, input);
            return true;
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, Lang.Text("Launch.Account.Microsoft.Settings.SaveFailed"), ModBase.LogLevel.Msgbox);
            return false;
        }
    }
}
