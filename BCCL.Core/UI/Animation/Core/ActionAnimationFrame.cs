using System;

namespace BCCL.Core.UI.Animation.Core;

public struct ActionAnimationFrame(Action action) : IAnimationFrame
{
    public Action Action { get; set; } = action;

    public Action GetAction() => Action;
}