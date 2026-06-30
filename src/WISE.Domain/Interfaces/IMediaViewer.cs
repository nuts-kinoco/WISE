using WISE.Domain.Entities;
using WISE.Domain.Enums;

namespace WISE.Domain.Interfaces;

public record ViewerCapabilities(
    bool SupportsPageNavigation,
    bool SupportsDoublePage,
    bool SupportsPrefetch,
    bool SupportsTimeSeek,
    bool SupportsResume);

public record ViewerRoute(
    string RouteTemplate,
    string ViewerType,
    ViewerCapabilities Capabilities);

public interface IMediaViewer
{
    MediaType MediaType { get; }
    ViewerRoute GetViewerRoute(Work work);
}
