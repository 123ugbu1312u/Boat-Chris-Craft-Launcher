using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BCCL;

public class MyPageLeft : Grid
{
    public static DependencyProperty AnimatedControlProperty =
        DependencyProperty.Register("AnimatedControl", typeof(FrameworkElement), typeof(MyPageLeft));

    private readonly int uuid = ModBase.GetUuid();

    private bool _animatedControlNullWarned;

    // 执行逐个进入动画的控件
    public FrameworkElement AnimatedControl
    {
        get
        {
            var res = GetValue(AnimatedControlProperty);
            if (res is null && !_animatedControlNullWarned)
            {
                _animatedControlNullWarned = true;
                ModBase.Log($"[MyPageLeft] 获取到 AnimatedControl(来自 {Name}) 的值为 null", ModBase.LogLevel.Debug);
            }

            return (FrameworkElement)res;
        }
        set => SetValue(AnimatedControlProperty, value);
    }

    public void TriggerShowAnimation()
    {
        if (AnimatedControl is null)
        {
            if (RenderTransform is not TranslateTransform)
            {
                RenderTransform = new TranslateTransform();
            }

            Opacity = 0d;
            ((TranslateTransform)RenderTransform).Y = 8d;
            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaTranslateY(this, -8d, 220,
                        ease: new ModAnimation.AniEaseOutFluent()),
                    ModAnimation.AaOpacity(this, 1d, 170)
                }, "PageLeft PageChange " + uuid);
        }
        else
        {
            // 逐个进入动画
            var aniList = new List<ModAnimation.AniData>();
            var id = 0;
            var delay = 0;
            foreach (var ElementRaw in GetAllAnimControls(true))
            {
                var element = MyVirtualizingElement.TryInit(ElementRaw);
                if (element.Visibility == Visibility.Collapsed)
                {
                    // 还原之前的隐藏动画可能导致的改变（#2436）
                    element.Opacity = 1d;
                    element.RenderTransform = new TranslateTransform(0d, 0d);
                    if (element is MyListItem)
                        ((MyListItem)element).isMouseOverAnimationEnabled = true;
                }
                else
                {
                    element.Opacity = 0d;
                    element.RenderTransform = new TranslateTransform(0d, 8d);
                    if (element is MyListItem)
                        ((MyListItem)element).isMouseOverAnimationEnabled = false;
                    aniList.Add(ModAnimation.AaOpacity(element, element is TextBlock ? 0.6d : 1d, 170, delay,
                        new ModAnimation.AniEaseOutFluent(ModAnimation.AniEasePower.Weak)));
                    aniList.Add(ModAnimation.AaTranslateY(element, -8d, 220, delay,
                        new ModAnimation.AniEaseOutFluent()));
                    if (element is MyListItem)
                        aniList.Add(ModAnimation.AaCode(() =>
                        {
                            ((MyListItem)element).isMouseOverAnimationEnabled = true;
                            ((MyListItem)element).RefreshColor(this, new EventArgs());
                        }, delay + 180));
                    delay = Math.Min(60, delay + Math.Max(10 - id, 5));
                    id += 1;
                }
            }

            ModAnimation.AniStart(aniList, "PageLeft PageChange " + uuid);
        }
    }

    public void TriggerHideAnimation()
    {
        if (AnimatedControl is null)
        {
            if (RenderTransform is not TranslateTransform)
            {
                RenderTransform = new TranslateTransform();
            }

            ModAnimation.AniStart(
                new[]
                {
                    ModAnimation.AaTranslateY(this, -6d - ((TranslateTransform)RenderTransform).Y, 140,
                        ease: new ModAnimation.AniEaseInFluent(ModAnimation.AniEasePower.Weak)),
                    ModAnimation.AaOpacity(this, -Opacity, 120)
                }, "PageLeft PageChange " + uuid);
        }
        else
        {
            // 逐个退出动画
            var aniList = new List<ModAnimation.AniData>();
            var id = 0;
            var controls = GetAllAnimControls();
            foreach (var Element in controls)
            {
                aniList.Add(ModAnimation.AaOpacity(Element, -Element.Opacity, 110,
                    (int)Math.Round(70d / controls.Count * id)));
                aniList.Add(ModAnimation.AaTranslateY(Element, -5, 110, (int)Math.Round(70d / controls.Count * id)));
                id += 1;
            }

            ModAnimation.AniStart(aniList, "PageLeft PageChange " + uuid);
        }
    }

    // 遍历获取所有需要生成动画的控件
    private List<FrameworkElement> GetAllAnimControls(bool ignoreInvisibility = false)
    {
        var allControls = new List<FrameworkElement>();
        GetAllAnimControls(AnimatedControl, ref allControls, ignoreInvisibility);
        return allControls;
    }

    private void GetAllAnimControls(FrameworkElement element, ref List<FrameworkElement> allControls,
        bool ignoreInvisibility)
    {
        if (!ignoreInvisibility && element.Visibility == Visibility.Collapsed)
            return;
        if (element is MyTextButton)
            allControls.Add(element);
        else if (element is MyListItem)
            allControls.Add(element);
        else if (element is ContentControl)
            GetAllAnimControls((FrameworkElement)((ContentControl)element).Content, ref allControls,
                ignoreInvisibility);
        else if (element is Panel)
            foreach (FrameworkElement Element2 in ((Panel)element).Children)
                GetAllAnimControls(Element2, ref allControls, ignoreInvisibility);
        else
            allControls.Add(element);
    }
}

public interface IRefreshable
{
    void Refresh();
}
