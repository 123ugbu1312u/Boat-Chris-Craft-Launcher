using System.Windows;
using BCCL.Core.UI.Animation.Animatable;
using BCCL.Core.UI.Animation.Core;
using BCCL.Core.UI.Animation.ValueProcessor;

namespace BCCL.Core.UI.Animation;

public class PointFromToAnimation : FromToAnimationBase<Point>
{
    public override IAnimationFrame? ComputeNextFrame(IAnimatable target)
    {
        // 应用缓动函数
        var easedProgress = Easing.Ease(CurrentFrame, TotalFrames);

        // 计算当前值
        CurrentValue = ValueType == AnimationValueType.Relative
            ? ValueProcessorManager.Add(From, ValueProcessorManager.Scale(To, easedProgress))
            : ValueProcessorManager.Add(From,
                ValueProcessorManager.Scale(ValueProcessorManager.Subtract(To, From), easedProgress));

        return base.ComputeNextFrame(target);
    }
}