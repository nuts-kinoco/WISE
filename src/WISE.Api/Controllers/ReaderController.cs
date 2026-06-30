using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WISE.Domain.Enums;
using WISE.Infrastructure.Archive;
using WISE.Infrastructure.Data;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/works/{id}/reader")]
    public class ReaderController : ControllerBase
    {
        private readonly WiseDbContext _db;
        private readonly ArchiveReaderSelector _selector;
        private readonly ILogger<ReaderController> _logger;

        public ReaderController(WiseDbContext db, ArchiveReaderSelector selector, ILogger<ReaderController> logger)
        {
            _db = db;
            _selector = selector;
            _logger = logger;
        }

        [HttpGet("pages")]
        public async Task<IActionResult> GetPages(string id)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID.");

            var work = await _db.Works
                .Include(w => w.Assets)
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == workId);

            if (work == null) return NotFound();

            var archiveAsset = SelectArchiveAsset(work.Assets);
            if (archiveAsset == null)
                return NotFound(new { error = "No readable archive asset found for this work." });

            var reader = _selector.Select(archiveAsset.FilePath);
            if (reader == null)
                return UnprocessableEntity(new { error = "No archive reader available for this file type." });

            var pages = await reader.GetPagesAsync(archiveAsset.FilePath, HttpContext.RequestAborted);
            return Ok(new
            {
                workId = work.Id,
                assetId = archiveAsset.Id,
                storageFormat = archiveAsset.StorageFormat.ToString(),
                totalPages = pages.Count,
                pages = pages.Select(p => new { p.Index, p.FileName, p.ContentType })
            });
        }

        [HttpGet("pages/{pageIndex:int}")]
        public async Task<IActionResult> GetPage(string id, int pageIndex)
        {
            if (!Guid.TryParse(id, out var workId)) return BadRequest("Invalid Work ID.");

            var work = await _db.Works
                .Include(w => w.Assets)
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == workId);

            if (work == null) return NotFound();

            var archiveAsset = SelectArchiveAsset(work.Assets);
            if (archiveAsset == null) return NotFound();

            var reader = _selector.Select(archiveAsset.FilePath);
            if (reader == null) return UnprocessableEntity();

            try
            {
                var pages = await reader.GetPagesAsync(archiveAsset.FilePath, HttpContext.RequestAborted);
                if (pageIndex < 0 || pageIndex >= pages.Count)
                    return NotFound(new { error = $"Page {pageIndex} not found. Total: {pages.Count}" });

                var stream = await reader.OpenPageAsync(archiveAsset.FilePath, pageIndex, HttpContext.RequestAborted);
                var contentType = pages[pageIndex].ContentType;

                return File(stream, contentType, enableRangeProcessing: false);
            }
            catch (ArgumentOutOfRangeException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Reader] Failed to open page {Page} for work {WorkId}", pageIndex, workId);
                return StatusCode(500, new { error = "Failed to read page." });
            }
        }

        private static WISE.Domain.Entities.Asset? SelectArchiveAsset(
            System.Collections.Generic.IReadOnlyCollection<WISE.Domain.Entities.Asset> assets)
            => assets.FirstOrDefault(a =>
                a.StorageFormat == StorageFormat.Archive
                || a.StorageFormat == StorageFormat.Folder
                || a.StorageFormat == StorageFormat.Pdf);
    }
}
