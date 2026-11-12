using System;
using System.Numerics;
using BCCL.Core.UI.Animation.Animatable;
using BCCL.Core.UI.Animation.ValueProcessor;

namespace BCCL.Core.UI.Animation.Core;

// public readonly struct AnimationFrame<T>(IAnimatable target, T value, T startValue) : IAnimationFrame
// {
//     public IAnimatable Target { get; init; } = target;
//     public T Value { get; init; } = value;
//     public T StartValue { get; init; } = startValue;
//     public T GetAbsoluteValue() => ValueProcessorManager.Add(StartValue, Value);
//     object IAnimationFrame.StartValue => StartValue!;
//     object IAnimationFrame.Value => Value!;
//     object IAnimationFrame.GetAbsoluteValue() => GetAbsoluteValue()!;
// }