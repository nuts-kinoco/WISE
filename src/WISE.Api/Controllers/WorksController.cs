using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using WISE.Infrastructure.Data;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorksController : ControllerBase
    {
        private readonly WiseDbContext _dbContext;

        public WorksController(WiseDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetWorks([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null)
        {
            var query = _dbContext.Works
                .AsNoTracking()
                .Include(w => w.MetadataFields)
                .Include(w => w.Assets)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var lowerQ = q.ToLower();
                query = query.Where(w => 
                    (w.PrimaryIdentifier != null && w.PrimaryIdentifier.ToLower().Contains(lowerQ)) ||
                    w.MetadataFields.Any(m => m.FieldName == "Title" && m.Value != null && m.Value.ToLower().Contains(lowerQ)) ||
                    w.MetadataFields.Any(m => m.FieldName == "Maker" && m.Value != null && m.Value.ToLower().Contains(lowerQ))
                );
            }

            var totalCount = await query.CountAsync();
            var works = await query
                .OrderBy(w => w.PrimaryIdentifier)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(w => new
                {
                    w.Id,
                    w.PrimaryIdentifier,
                    Title = w.MetadataFields.FirstOrDefault(m => m.FieldName == "Title" && m.IsPrimary) != null 
                            ? w.MetadataFields.First(m => m.FieldName == "Title" && m.IsPrimary).Value 
                            : null,
                    CoverAssetId = w.Assets.FirstOrDefault(a => a.FilePath != null && (a.FilePath.EndsWith(".jpg") || a.FilePath.EndsWith(".png"))) != null
                                    ? (System.Guid?)w.Assets.FirstOrDefault(a => a.FilePath != null && (a.FilePath.EndsWith(".jpg") || a.FilePath.EndsWith(".png")))!.Id
                                    : null
                })
                .ToListAsync();

            return Ok(new
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                Items = works
            });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetWorkDetail(string id)
        {
            if (!System.Guid.TryParse(id, out var workId))
            {
                return BadRequest("Invalid Work ID format.");
            }

            var work = await _dbContext.Works
                .AsNoTracking()
                .Include(w => w.MetadataFields)
                .Include(w => w.Assets)
                .FirstOrDefaultAsync(w => w.Id == workId);

            if (work == null)
            {
                return NotFound();
            }

            var history = await _dbContext.EventLogs
                .Where(e => e.TargetId == workId)
                .OrderByDescending(e => e.OccurredAt)
                .Select(e => new { e.EventType, e.OccurredAt, e.Actor, e.Payload })
                .ToListAsync();

            return Ok(new
            {
                work.Id,
                work.PrimaryIdentifier,
                Metadata = work.MetadataFields.Select(m => new { m.FieldName, m.Value, m.IsPrimary, m.ProviderId, m.ConfidenceScore }),
                Assets = work.Assets.Select(a => new { a.Id, a.OriginalFilename, a.FileSize, a.Sha256 }),
                History = history,
                Diagnostic = new {
                    IdentifierConfidence = (int?)null,
                    Evidences = System.Array.Empty<object>(),
                    Note = "Available in v1.1. Identifier diagnostics will be available after the Rule Engine is implemented."
                }
            });
        }
    }
}
