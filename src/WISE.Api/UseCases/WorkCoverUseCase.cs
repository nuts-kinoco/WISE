using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.Enums;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

/// <summary>
/// P1 リファクタリング Phase5: WorksController から WiseDbContext 直接参照を排除するための UseCase。
/// カバー画像の手動設定（既存アセットから選択／新規アップロード）を担う。
/// </summary>
public class WorkCoverUseCase
{
    private readonly WiseDbContext _dbContext;

    public WorkCoverUseCase(WiseDbContext dbContext) => _dbContext = dbContext;

    public enum SetCoverResult { WorkNotFound, AssetNotFound, Ok }

    public async Task<(SetCoverResult Result, string? CoverUrl)> SetCoverAsync(Guid workId, Guid assetId, CancellationToken ct = default)
    {
        var work = await _dbContext.Works.Include(w => w.Assets).Include(w => w.MetadataFields)
            .FirstOrDefaultAsync(w => w.Id == workId, ct);
        if (work == null) return (SetCoverResult.WorkNotFound, null);

        var asset = work.Assets.FirstOrDefault(a => a.Id == assetId);
        if (asset == null) return (SetCoverResult.AssetNotFound, null);

        var assetApiUrl = $"/api/assets/{assetId}/content";

        // Demote all PortraitCover primary fields
        foreach (var f in work.MetadataFields.Where(m => m.FieldName == "PortraitCover" && m.IsPrimary))
            f.SetPrimary(false);

        // Check if the selected asset is a PortraitCover (= revert to original provider)
        var isOriginalCover = asset.AssetType == AssetType.PortraitCover;
        if (isOriginalCover)
        {
            // Remove any Manual PortraitCover field and promote the field whose value points to this asset
            var manualField = work.MetadataFields.FirstOrDefault(m => m.FieldName == "PortraitCover" && m.ProviderId == "Manual");
            if (manualField != null) _dbContext.MetadataFields.Remove(manualField);

            var originalField = work.MetadataFields.FirstOrDefault(m =>
                m.FieldName == "PortraitCover" && m.ProviderId != "Manual" && m.Value == assetApiUrl);
            if (originalField != null)
                originalField.SetPrimary(true);
            else
            {
                var best = work.MetadataFields
                    .Where(m => m.FieldName == "PortraitCover" && m.ProviderId != "Manual")
                    .OrderByDescending(m => m.ConfidenceScore).FirstOrDefault();
                if (best != null) best.SetPrimary(true);
            }
        }
        else
        {
            var existing = work.MetadataFields.FirstOrDefault(m => m.FieldName == "PortraitCover" && m.ProviderId == "Manual");
            if (existing != null)
            {
                existing.UpdateValue(assetApiUrl, 999, "Manual");
                existing.SetPrimary(true);
            }
            else
            {
                var newField = new MetadataField("PortraitCover", assetApiUrl, "Manual", true, 999);
                newField.SetWorkId(workId);
                _dbContext.MetadataFields.Add(newField);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        return (SetCoverResult.Ok, assetApiUrl);
    }

    public enum UploadCoverResult { WorkNotFound, VideoAssetNotFound, Ok }

    public async Task<(UploadCoverResult Result, Guid AssetId, string? Url)> UploadCoverAsync(
        Guid workId, IFormFile file, CancellationToken ct = default)
    {
        var work = await _dbContext.Works.Include(w => w.Assets).Include(w => w.MetadataFields)
            .FirstOrDefaultAsync(w => w.Id == workId, ct);
        if (work == null) return (UploadCoverResult.WorkNotFound, Guid.Empty, null);

        // .thumbnails/ フォルダをビデオアセット横に確保する
        var videoAsset = work.Assets.FirstOrDefault(a =>
            a.FilePath != null && (a.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                || a.FilePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)));
        if (videoAsset?.FilePath == null)
            return (UploadCoverResult.VideoAssetNotFound, Guid.Empty, null);

        var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        var coverDir = System.IO.Path.GetDirectoryName(videoAsset.FilePath) ?? string.Empty;
        var thumbDir = System.IO.Path.Combine(coverDir, ".thumbnails");
        System.IO.Directory.CreateDirectory(thumbDir);

        var safeBase = System.Text.RegularExpressions.Regex.Replace(
            System.IO.Path.GetFileNameWithoutExtension(file.FileName), @"[^\w\-]", "_");
        var newFileName = $"upload_{DateTime.UtcNow:yyyyMMddHHmmss}_{safeBase}{ext}";
        var destPath = System.IO.Path.Combine(thumbDir, newFileName);

        await using (var stream = System.IO.File.Create(destPath))
            await file.CopyToAsync(stream, ct);

        var assetId = Guid.NewGuid();
        var newAsset = new Asset(
            destPath, newFileName, new System.IO.FileInfo(destPath).Length, null, assetId,
            AssetType.PortraitCover);
        work.AddAsset(newAsset);

        var assetApiUrl = $"/api/assets/{assetId}/content";

        foreach (var f in work.MetadataFields.Where(m => m.FieldName == "PortraitCover" && m.IsPrimary))
            f.SetPrimary(false);

        var existingManual = work.MetadataFields.FirstOrDefault(m => m.FieldName == "PortraitCover" && m.ProviderId == "Manual");
        if (existingManual != null)
        {
            existingManual.UpdateValue(assetApiUrl, 999, "Manual");
            existingManual.SetPrimary(true);
        }
        else
        {
            var newField = new MetadataField("PortraitCover", assetApiUrl, "Manual", true, 999);
            newField.SetWorkId(workId);
            _dbContext.MetadataFields.Add(newField);
        }

        await _dbContext.SaveChangesAsync(ct);
        return (UploadCoverResult.Ok, assetId, assetApiUrl);
    }
}
