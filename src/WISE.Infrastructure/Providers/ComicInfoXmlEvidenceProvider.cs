using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;
using WISE.Domain.Services;
using WISE.Domain.ValueObjects;

namespace WISE.Infrastructure.Providers;

/// <summary>
/// Reads ComicInfo.xml embedded in ZIP/CBZ archives and extracts identifier evidence.
/// ComicInfo.xml is a de-facto standard metadata format for comic readers (ComicRack, Kavita, etc.)
/// Identifier is searched in: <Notes>, <Tags>, <Series> + <Number> fields.
/// Score=80 — more reliable than path (60) but less than exact filename match (90).
/// </summary>
public class ComicInfoXmlEvidenceProvider : IEvidenceProvider
{
    public string ProviderId => "Comic.ComicInfoXml";

    private const int ComicInfoScore = 80;

    public Task<IEnumerable<Evidence>> CollectEvidencesAsync(
        Asset asset,
        CancellationToken cancellationToken = default)
    {
        var evidences = new List<Evidence>();

        if (string.IsNullOrWhiteSpace(asset.FilePath)) return Done(evidences);

        var ext = Path.GetExtension(asset.FilePath).ToLowerInvariant();
        if (ext is not (".zip" or ".cbz" or ".cbr")) return Done(evidences);
        if (!File.Exists(asset.FilePath)) return Done(evidences);

        try
        {
            using var archive = ZipFile.OpenRead(asset.FilePath);
            var comicInfoEntry = archive.GetEntry("ComicInfo.xml")
                ?? archive.GetEntry("comicinfo.xml");

            if (comicInfoEntry == null) return Done(evidences);

            using var stream = comicInfoEntry.Open();
            var doc = XDocument.Load(stream);
            var root = doc.Root;
            if (root == null) return Done(evidences);

            // Search Notes field (often contains full identifier like "RJ123456")
            TryExtract(root.Element("Notes")?.Value, "Notes", evidences);

            // Search Tags field (comma-separated, may contain identifiers)
            var tags = root.Element("Tags")?.Value;
            if (!string.IsNullOrWhiteSpace(tags))
            {
                foreach (var tag in tags.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    TryExtract(tag.Trim(), "Tags", evidences);
            }

            // Series + Number combination (e.g. Series="RJ123456", Number="1")
            TryExtract(root.Element("Series")?.Value, "Series", evidences);

            // Web (source URL can contain DLsite/FANZA product ID)
            TryExtract(root.Element("Web")?.Value, "Web", evidences);
        }
        catch (Exception)
        {
            // Silently ignore unreadable archives or malformed XML
        }

        return Done(evidences);
    }

    private static void TryExtract(string? text, string field, List<Evidence> evidences)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var candidates = IdentifierParser.ExtractCandidates(text);
        foreach (var candidate in candidates)
        {
            evidences.Add(new Evidence(
                type: $"ComicInfo.{field}.{candidate.PatternName}",
                value: candidate.ExtractedValue,
                score: new ConfidenceScore(ComicInfoScore),
                providerId: "Comic.ComicInfoXml"));
        }
    }

    private static Task<IEnumerable<Evidence>> Done(List<Evidence> list)
        => Task.FromResult<IEnumerable<Evidence>>(list);
}
