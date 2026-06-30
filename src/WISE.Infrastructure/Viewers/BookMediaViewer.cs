using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Viewers;

public class BookMediaViewer : IMediaViewer
{
    public MediaType MediaType => MediaType.Book;

    private static readonly ViewerCapabilities Caps = new(
        SupportsPageNavigation: true,
        SupportsDoublePage: false,
        SupportsPrefetch: false,
        SupportsTimeSeek: false,
        SupportsResume: true);

    public ViewerRoute GetViewerRoute(Work work)
        => new($"/works/{work.Id}/epub-reader", "EpubReader", Caps);
}
