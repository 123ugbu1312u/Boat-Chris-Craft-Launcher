using System;
using System.Windows;
using BCCL.Core.UI.Animation.Animatable;

namespace BCCL.Core.UI.Animation.Core;

public interface IAnimationFrame
{
    Action GetAction();
}