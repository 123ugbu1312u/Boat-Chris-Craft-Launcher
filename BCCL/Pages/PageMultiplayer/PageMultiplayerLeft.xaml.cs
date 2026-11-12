namespace BCCL;

public partial class PageMultiplayerLeft
{
    public bool ExtrasSelected => ItemExtras.Checked;
    public PageMultiplayerLeft()
    {
        InitializeComponent();
        AnimatedControl = PanItem;
        ItemLobby.SetChecked(true, false, false);
    }
    private void PageCheck(object sender, ModBase.RouteEventArgs e)
    {
        if (ModMain.frmMain is null) return;
        ModMain.frmMain.ShowMultiplayerExtras(ReferenceEquals(sender, ItemExtras));
    }
}
