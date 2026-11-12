using System;
using BCCL.Core.UI.Animation.Animatable;
using BCCL.Core.UI.Animation.ValueProcessor;

namespace BCCL.Core.UI.Animation.Core;

public readonly struct FromToAnimationFrame<T>(IAnimatable target, T value, T startValue) : IAnimationFrame
{
    public IAnimatable Target { get; init; } = target;
    public T Value { get; init; } = value;
    public T StartValue { get; init; } = startValue;
    public Action GetAction()
    {
        var target = Target;
        var absolute = ValueProcessorManager.Add(StartValue, Value);
        return () => target.SetValue(absolute!);
    }
}