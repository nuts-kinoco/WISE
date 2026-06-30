using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WISE.Domain.Enums;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;
using WISE.Infrastructure.Data;

namespace WISE.Infrastructure.Providers;

/// <summary>
/// Parses metadata from standard doujinshi filenames.
/// Pattern: (Category) [Circle (Author)] Title (Genre).zip
/// No network calls. Priority=50 — lower than scrapers so they win on conflicts.
/// </summary>
public class DoujinishiFilenameMetadataProvider : IMetadataProvider
{
    private static readonly Regex DoujinPattern = new(
        @"^\s*\((?<category>[^)]+)\)\s*\[(?<circle>[^\[(]+?)(?:\s*\((?<author>[^)]+)\))?\s*\]\s*(?<title>.+?)(?:\s*\((?<genre>[^)]+)\))?(?:\s*\[[^\]]*\])*\s*$",
        RegexOptions.Compiled);

    private readonly WiseDbContext _db;
    private readonly ILogger<DoujinishiFilenameMetadataProvider> _logger;

    public string ProviderId => "DoujinishiFilename";
    public int Priority => 50;

    public DoujinishiFilenameMetadataProvider(WiseDbContext db, ILogger<DoujinishiFilenameMetadataProvider> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<MetadataResult> FetchAsync(MetadataProviderContext context)
    {
        var sw = Stopwatch.StartNew();

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
            if (string.IsNullOrEmpty(a.FilePath)) return false;
            var ext = Path.GetExtension(a.FilePath).ToLowerInvariant();
            return ext is ".zip" or ".cbz" or ".rar" or ".cbr" or ".7z";
        });

        if (archiveAsset == null)
        {
            sw.Stop();
            return MetadataResult.Failed(ProviderId, FailureReason.NotFound, "No archive asset", sw.Elapsed, "Local");
        }

        var nameWithoutExt = Path.GetFileNameWithoutExtension(archiveAsset.OriginalFilename ?? archiveAsset.FilePath ?? "");
        var m = DoujinPattern.Match(nameWithoutExt);

        if (!m.Success)
        {
            sw.Stop();
            return MetadataResult.Failed(ProviderId, FailureReason.NotFound, "Filename does not match doujinshi pattern", sw.Elapsed, "Local");
        }

        var candidates = new List<MetadataCandidate>();
        void Add(string field, string groupName)
        {
            var val = m.Groups[groupName].Value.Trim();
            if (!string.IsNullOrWhiteSpace(val))
                candidates.Add(new MetadataCandidate(ProviderId, field, val, 75, Priority, "filename"));
        }

        Add("Category", "category");
        Add("circle", "circle");
        Add("author", "author");
        Add("Title", "title");
        Add("Genre", "genre");

        sw.Stop();

        if (candidates.Count == 0)
            return MetadataResult.Failed(ProviderId, FailureReason.ParserError, "No fields extracted", sw.Elapsed, "Local");

        _logger.LogInformation("[DoujinFilename] OK | {Fields} | {Name}",
            string.Join(",", candidates.Select(c => c.FieldName).Distinct()), nameWithoutExt);

        return MetadataResult.Succeeded(ProviderId, candidates, sw.Elapsed, "Local");
    }
}
