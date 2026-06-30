using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;
using WISE.Infrastructure.Data;

namespace WISE.Infrastructure.Providers;

/// <summary>
/// Reads metadata embedded in ComicInfo.xml inside ZIP/CBZ archives.
/// No network calls. Priority=100 (highest) — embedded metadata is authoritative.
/// </summary>
public class ComicInfoXmlMetadataProvider : IMetadataProvider
{
    private readonly WiseDbContext _db;
    private readonly ILogger<ComicInfoXmlMetadataProvider> _logger;

    public string ProviderId => "ComicInfoXml";
    public int Priority => 100;

    public ComicInfoXmlMetadataProvider(WiseDbContext db, ILogger<ComicInfoXmlMetadataProvider> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        var sw = Stopwatch.StartNew();
        var candidates = new List<MetadataCandidate>();

        // Find an archive asset for this work
        var work = await _db.Works
            .Include(w => w.Assets)
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == context.WorkId, context.CancellationToken);

        if (work == null)
        {
            sw.Stop();
            return MetadataResult.Failed(ProviderId, FailureReason.NotFound, "Work not found", sw.Elapsed, "Local");
        }

        var archiveAsset = work.Assets.FirstOrDefault(a =>
        {
            var ext = Path.GetExtension(a.FilePath).ToLowerInvariant();
            return ext is ".zip" or ".cbz" or ".cbr";
        });

        if (archiveAsset == null || !File.Exists(archiveAsset.FilePath))
        {
            sw.Stop();
            return MetadataResult.Failed(ProviderId, FailureReason.NotFound, "No archive asset found", sw.Elapsed, "Local");
        }

        try
        {
            using var archive = ZipFile.OpenRead(archiveAsset.FilePath);
            var entry = archive.GetEntry("ComicInfo.xml") ?? archive.GetEntry("comicinfo.xml");

            if (entry == null)
            {
                sw.Stop();
                return MetadataResult.Failed(ProviderId, FailureReason.NotFound, "ComicInfo.xml not found in archive", sw.Elapsed, "Local");
            }

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            var root = doc.Root;

            if (root == null)
            {
                sw.Stop();
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "ComicInfo.xml has no root element", sw.Elapsed, "Local");
            }

            AddCandidate(candidates, "Title",       root.Element("Title")?.Value);
            AddCandidate(candidates, "author",      root.Element("Writer")?.Value);
            AddCandidate(candidates, "author",      root.Element("Penciller")?.Value);
            AddCandidate(candidates, "circle",      root.Element("Publisher")?.Value);
            AddCandidate(candidates, "series",      root.Element("Series")?.Value);
            AddCandidate(candidates, "page_count",  root.Element("PageCount")?.Value);
            AddCandidate(candidates, "language",    root.Element("LanguageISO")?.Value);
            AddCandidate(candidates, "summary",     root.Element("Summary")?.Value);

            var tags = root.Element("Tags")?.Value;
            if (!string.IsNullOrWhiteSpace(tags))
            {
                foreach (var tag in tags.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    AddCandidate(candidates, "Tags", tag.Trim());
            }

            var genre = root.Element("Genre")?.Value;
            if (!string.IsNullOrWhiteSpace(genre))
            {
                foreach (var g in genre.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    AddCandidate(candidates, "Genre", g.Trim());
            }

            var web = root.Element("Web")?.Value;
            if (!string.IsNullOrWhiteSpace(web)) AddCandidate(candidates, "source_url", web);

            sw.Stop();

            if (!candidates.Any())
                return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "No fields extracted from ComicInfo.xml", sw.Elapsed, "Local");

            _logger.LogInformation("[ComicInfoXml] OK | Fields={Fields} | Archive={Path}",
                string.Join(",", candidates.Select(c => c.FieldName).Distinct()), archiveAsset.FilePath);

            return MetadataResult.Succeeded(ProviderId, candidates, sw.Elapsed, "Local");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[ComicInfoXml] Failed to parse ComicInfo.xml from {Path}", archiveAsset.FilePath);
            return MetadataResult.Failed(ProviderId, FailureReason.ParserError, ex.Message, sw.Elapsed, "Local");
        }
    }

    private void AddCandidate(List<MetadataCandidate> list, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        list.Add(new MetadataCandidate(ProviderId, field, value.Trim(), 90, Priority, "ComicInfo.xml"));
    }
}
