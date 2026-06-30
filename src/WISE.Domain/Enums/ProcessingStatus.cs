namespace WISE.Domain.Enums;

public enum ProcessingStatus
{
    ScanPending,
    Scanning,
    MetadataPending,
    MetadataFetching,
    NotFound,
    NetworkError,
    Organizing,
    Organized,
    Failed
}
