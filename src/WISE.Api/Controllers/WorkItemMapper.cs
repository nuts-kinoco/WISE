using System;
using System.Collections.Generic;
using System.Linq;
using WISE.Domain.Entities;

namespace WISE.Api.Controllers;

internal static class WorkItemMapper
{
    internal static string? ResolveMediaUrl(string? value, IEnumerable<Asset> assets)
    {
        if (value == null) return null;
        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return value;
        if (value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return value;
        var match = assets.FirstOrDefault(a => a.FilePath == value);
        return match != null ? $"/api/assets/{match.Id}/content" : null;
    }

    internal static object Map(Work w)
    {
        string? MetaFirst(params string[] names)
        {
            foreach (var name in names)
            {
                var v = w.MetadataFields.FirstOrDefault(m => m.FieldName == name && m.IsPrimary)?.Value
                     ?? w.MetadataFields.FirstOrDefault(m => m.FieldName == name)?.Value;
                if (v != null) return v;
            }
            return null;
        }

        return new
        {
            w.Id,
            w.PrimaryIdentifier,
            MediaType       = w.MediaType.ToString(),
            Title           = MetaFirst("Title"),
            Actress         = MetaFirst("Actress", "actress"),
            Maker           = MetaFirst("Maker", "maker"),
            Label           = MetaFirst("Label", "label"),
            ReleaseDate     = MetaFirst("ReleaseDate", "release_date"),
            Author          = MetaFirst("author", "Author", "Writer"),
            Circle          = MetaFirst("circle", "Circle"),
            PageCount       = MetaFirst("page_count", "PageCount"),
            Language        = MetaFirst("language", "Language", "LanguageISO"),
            CoverUrl        = ResolveMediaUrl(
                                MetaFirst("PortraitCover", "Cover") ?? $"/api/works/{w.Id}/cover",
                                w.Assets),
            CoverLandscapeUrl = ResolveMediaUrl(
                                  MetaFirst("LandscapeCover", "CoverLandscape"),
                                  w.Assets),
            MetadataStatus  = w.Status.ToString(),
            w.Favorite,
            w.Rating,
        };
    }
}
