using System;
using System.Collections.Frozen;

namespace BCCL.Core.UI.Animation.Core;

public record AnimationData<T> : IAnimationData
{
    public IAnimation Animation { get; init; } = null!;
    public FrozenDictionary<int, T> Values { get; init; } = null!;
}