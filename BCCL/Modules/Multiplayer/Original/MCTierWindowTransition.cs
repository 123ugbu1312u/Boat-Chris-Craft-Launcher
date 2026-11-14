using static BCCL.ModAnimation;

namespace BCCL.Multiplayer.Original;

/// <summary>Animate the native child position: WPF opacity cannot animate an HwndHost surface.</summary>
internal static class MCTierWindowTransition
{
    public const int DurationMilliseconds = 240;
    public const double DistanceDip = 18;

    public static AniData[] Create(Action<double> update)
    {
        var progress = 0d;
        update(0);
        return [
            AaDouble(delta =>
            {
                progress = Math.Clamp(progress + Convert.ToDouble(delta), 0, 1);
                update(progress);
            }, 1, DurationMilliseconds, ease: new AniEaseOutFluent(AniEasePower.Weak)),
            AaCode(() => update(1), after: true)
        ];
    }
}
