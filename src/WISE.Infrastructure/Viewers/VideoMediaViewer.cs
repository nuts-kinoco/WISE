using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Viewers;

public class VideoMediaViewer : IMediaViewer
{
    public MediaType MediaType => MediaType.Video;

    private static readonly ViewerCapabilities Caps = new(
        SupportsPageNavigation: false,
        SupportsDoublePage: false,
        SupportsPrefetch: false,
        SupportsTimeSeek: true,
        SupportsResume: true);

    public ViewerRoute GetViewerRoute(Work work)
        => new ViewerRoute($"/works/{work.Id}/video", "VideoPlayer", Caps);
}
