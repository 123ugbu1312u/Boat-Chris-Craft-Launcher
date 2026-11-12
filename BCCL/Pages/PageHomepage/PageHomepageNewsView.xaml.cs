using BCCL.Core.ViewModel.Homepage;

namespace BCCL;

public partial class PageHomepageNewsView : MyPageRight
{
    public PageHomepageNewsView()
    {
        InitializeComponent();
        DataContext = new NewsViewModel();
    }
}