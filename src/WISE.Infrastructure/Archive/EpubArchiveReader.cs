using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Archive;

public class EpubArchiveReader : IArchiveReader
{
    private static readonly XNamespace OpfNs = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";

    public bool CanRead(string filePath)
        => Path.GetExtension(filePath).Equals(".epub", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ArchivePage>> GetPagesAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            var spineItems = GetSpineItems(zip);
            var pages = spineItems
                .Select((item, i) => new ArchivePage(i, item, "application/xhtml+xml"))
                .ToList();

            if (pages.Count == 0)
                pages.Add(new ArchivePage(0, Path.GetFileName(filePath), "application/epub+zip"));

            return Task.FromResult<IReadOnlyList<ArchivePage>>(pages);
        }
        catch
        {
            IReadOnlyList<ArchivePage> fallback = new[]
            {
                new ArchivePage(0, Path.GetFileName(filePath), "application/epub+zip")
            };
            return Task.FromResult(fallback);
        }
    }

    // OpenPageAsync(0) returns the full EPUB bytes — epub.js renders it client-side
    public Task<Stream> OpenPageAsync(string filePath, int pageIndex, CancellationToken ct = default)
    {
        var ms = new MemoryStream(File.ReadAllBytes(filePath));
        return Task.FromResult<Stream>(ms);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static List<string> GetSpineItems(ZipArchive zip)
    {
        var opfPath = GetOpfPath(zip);
        if (opfPath == null) return new List<string>();

        var opfEntry = zip.GetEntry(opfPath);
        if (opfEntry == null) return new List<string>();

        using var stream = opfEntry.Open();
        var opf = XDocument.Load(stream);

        var manifest = opf.Root?
            .Element(OpfNs + "manifest")?
            .Elements(OpfNs + "item")
            .ToDictionary(e => e.Attribute("id")?.Value ?? "", e => e.Attribute("href")?.Value ?? "")
            ?? new Dictionary<string, string>();

        var spine = opf.Root?
            .Element(OpfNs + "spine")?
            .Elements(OpfNs + "itemref")
            .Select(e => e.Attribute("idref")?.Value ?? "")
            .Where(id => manifest.ContainsKey(id))
            .Select(id => manifest[id])
            .ToList() ?? new List<string>();

        return spine;
    }

    internal static string? GetOpfPath(ZipArchive zip)
    {
        var containerEntry = zip.GetEntry("META-INF/container.xml");
        if (containerEntry == null) return null;

        using var stream = containerEntry.Open();
        var container = XDocument.Load(stream);

        return container.Root?
            .Element(ContainerNs + "rootfiles")?
            .Element(ContainerNs + "rootfile")?
            .Attribute("full-path")?.Value;
    }

    internal static string? GetCoverImagePath(ZipArchive zip)
    {
        var opfPath = GetOpfPath(zip);
        if (opfPath == null) return null;

        var opfEntry = zip.GetEntry(opfPath);
        if (opfEntry == null) return null;

        using var stream = opfEntry.Open();
        var opf = XDocument.Load(stream);

        var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? "";

        var items = opf.Root?
            .Element(OpfNs + "manifest")?
            .Elements(OpfNs + "item")
            .ToList() ?? new List<XElement>();

        // prefer item with properties="cover-image"
        var coverItem = items.FirstOrDefault(e =>
            e.Attribute("properties")?.Value?.Contains("cover-image") == true);

        // fallback: item whose id or href contains "cover"
        coverItem ??= items.FirstOrDefault(e =>
            (e.Attribute("id")?.Value?.Contains("cover", StringComparison.OrdinalIgnoreCase) == true
             || e.Attribute("href")?.Value?.Contains("cover", StringComparison.OrdinalIgnoreCase) == true)
            && e.Attribute("media-type")?.Value?.StartsWith("image/") == true);

        // fallback: first image in manifest
        coverItem ??= items.FirstOrDefault(e =>
            e.Attribute("media-type")?.Value?.StartsWith("image/") == true);

        var href = coverItem?.Attribute("href")?.Value;
        if (href == null) return null;

        return string.IsNullOrEmpty(opfDir) ? href : $"{opfDir}/{href}";
    }
}
