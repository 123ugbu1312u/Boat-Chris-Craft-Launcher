namespace BCCL;

public enum McInstanceCardType
{
    Retired = -1, // Preserve the old stored numeric value when reading legacy data.
    Auto = 0, // 仅用于强制实例分类的自动
    Hidden = 1,
    API = 2,
    OriginalLike = 3,
    Rubbish = 4,
    Fool = 5,
    Error = 6
}
