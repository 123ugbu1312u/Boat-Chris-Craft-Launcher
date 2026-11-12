using System;
using BCCL.Core.Utils;

namespace BCCL.Core.App.Configuration;

public class ArgConfig<TValue> : ParameterizedProperty<object, TValue>
{
    public ArgConfig(Func<object?, TValue> getter, Action<object?, TValue> setter)
    {
        GetValue = getter;
        SetValue = setter;
    }
}
