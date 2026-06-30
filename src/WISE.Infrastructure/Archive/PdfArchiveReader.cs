using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Archive;

/// <summary>
/// PDF page listing only. Rendering is deferred to the client (PDF.js).
/// OpenPageAsync streams the raw PDF bytes for single-page range extraction by client.
/// </summary>
public class PdfArchiveReader : IArchiveReader
{
    public bool CanRead(string filePath)
        => Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ArchivePage>> GetPagesAsync(string filePath, CancellationToken ct = default)
    {
        // Return a single "page" entry representing the whole PDF; actual page count
        // is resolved by the client PDF.js viewer which receives the raw file.
        IReadOnlyList<ArchivePage> pages = [new ArchivePage(0, Path.GetFileName(filePath), "application/pdf")];
        return Task.FromResult(pages);
    }

    public Task<Stream> OpenPageAsync(string filePath, int pageIndex, CancellationToken ct = default)
    {
        if (pageIndex != 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        Stream stream = File.OpenRead(filePath);
        return Task.FromResult(stream);
    }
}
