using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Archive;

public class RarArchiveReader : IArchiveReader
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif" };

    public bool CanRead(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".rar" or ".cbr";
    }

    public Task<IReadOnlyList<ArchivePage>> GetPagesAsync(string filePath, CancellationToken ct = default)
    {
        using var archive = ArchiveFactory.Open(filePath);
        var pages = archive.Entries
            .Where(e => !e.IsDirectory && ImageExtensions.Contains(Path.GetExtension(e.Key ?? "")))
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .Select((e, i) => new ArchivePage(i, e.Key!, GetContentType(e.Key!)))
            .ToList();

        return Task.FromResult<IReadOnlyList<ArchivePage>>(pages);
    }

    public Task<Stream> OpenPageAsync(string filePath, int pageIndex, CancellationToken ct = default)
    {
        using var archive = ArchiveFactory.Open(filePath);
        var entries = archive.Entries
            .Where(e => !e.IsDirectory && ImageExtensions.Contains(Path.GetExtension(e.Key ?? "")))
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pageIndex < 0 || pageIndex >= entries.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        var ms = new MemoryStream();
        using var entryStream = entries[pageIndex].OpenEntryStream();
        entryStream.CopyTo(ms);
        ms.Position = 0;

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
