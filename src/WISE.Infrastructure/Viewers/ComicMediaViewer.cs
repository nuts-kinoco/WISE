using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Viewers;

public class ComicMediaViewer : IMediaViewer
{
    public MediaType MediaType => MediaType.Comic;

    private static readonly ViewerCapabilities Caps = new(
        SupportsPageNavigation: true,
        SupportsDoublePage: true,
        SupportsPrefetch: true,
        SupportsTimeSeek: false,
        SupportsResume: true);

    public ViewerRoute GetViewerRoute(Work work)
        => new ViewerRoute($"/works/{work.Id}/reader", "ComicReader", Caps);
}
