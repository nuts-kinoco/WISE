namespace WISE.Infrastructure.Data.Models;

public class HttpCache
{
    public int Id { get; set; }
    public string Url { get; set; } = "";
    public string Body { get; set; } = "";
    public string? ContentType { get; set; }
    public DateTime CachedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
