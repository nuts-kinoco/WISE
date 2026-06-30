using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Archive;

public class ZipArchiveReader : IArchiveReader
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif" };

    public bool CanRead(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".zip" or ".cbz";
    }

    public Task<IReadOnlyList<ArchivePage>> GetPagesAsync(string filePath, CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var pages = archive.Entries
            .Where(e => ImageExtensions.Contains(Path.GetExtension(e.Name)))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .Select((e, i) => new ArchivePage(i, e.FullName, GetContentType(e.Name)))
            .ToList();

        return Task.FromResult<IReadOnlyList<ArchivePage>>(pages);
    }

    public Task<Stream> OpenPageAsync(string filePath, int pageIndex, CancellationToken ct = default)
    {
        var archive = ZipFile.OpenRead(filePath);
        var entries = archive.Entries
            .Where(e => ImageExtensions.Contains(Path.GetExtension(e.Name)))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pageIndex < 0 || pageIndex >= entries.Count)
        {
            archive.Dispose();
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        var entry = entries[pageIndex];
        var ms = new MemoryStream();
        using var entryStream = entry.Open();
        entryStream.CopyTo(ms);
        ms.Position = 0;
        archive.Dispose();

        return Task.FromResult<Stream>(ms);
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
