using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Archive;

public class FolderArchiveReader : IArchiveReader
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif" };

    public bool CanRead(string filePath) => Directory.Exists(filePath);

    public Task<IReadOnlyList<ArchivePage>> GetPagesAsync(string filePath, CancellationToken ct = default)
    {
        var pages = Directory.EnumerateFiles(filePath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select((f, i) => new ArchivePage(i, Path.GetFileName(f), GetContentType(f)))
            .ToList();

        return Task.FromResult<IReadOnlyList<ArchivePage>>(pages);
    }

    public Task<Stream> OpenPageAsync(string filePath, int pageIndex, CancellationToken ct = default)
    {
        var files = Directory.EnumerateFiles(filePath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pageIndex < 0 || pageIndex >= files.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        Stream stream = File.OpenRead(files[pageIndex]);
        return Task.FromResult(stream);
    }

    private static string GetContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            _ => "image/jpeg"
        };
}
