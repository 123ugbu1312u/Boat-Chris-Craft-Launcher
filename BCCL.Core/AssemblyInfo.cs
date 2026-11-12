using System.Runtime.CompilerServices;
using System.Windows.Markup;

[assembly: XmlnsDefinition("https://ce.pclc.cc/core/ui/animation", "BCCL.Core.UI.Animation")]
[assembly: XmlnsDefinition("https://ce.pclc.cc/core/ui/animation", "BCCL.Core.UI.Animation.Core")]
[assembly: XmlnsDefinition("https://ce.pclc.cc/core/ui/animation", "BCCL.Core.UI.Animation.Easings")]
[assembly: XmlnsPrefix("https://ce.pclc.cc/core/ui/animation", "ani")]

[assembly:XmlnsDefinition("https://ce.pclc.cc/core/utils/validate", "BCCL.Core.Utils.Validate")]
[assembly:XmlnsPrefix("https://ce.pclc.cc/core/utils/validate", "val")]

[assembly: DisableRuntimeMarshalling]
[assembly: InternalsVisibleTo("BCCL.Core.Test")]
