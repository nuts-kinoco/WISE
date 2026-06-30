using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WISE.Application.DTOs;
using WISE.Application.Services;
using WISE.Api.UseCases;
using Microsoft.EntityFrameworkCore;
using WISE.Infrastructure.Data;
using WISE.Domain.Interfaces;
using System.Linq;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class JobsController : ControllerBase
    {
        private readonly CreateImportJobUseCase _createImportJobUseCase;
        private readonly WiseDbContext _dbContext;
        private readonly IJobCancellationService _cancellationService;
        private readonly IEnumerable<IMetadataProvider> _providers;

        public JobsController(CreateImportJobUseCase createImportJobUseCase, WiseDbContext dbContext, IJobCancellationService cancellationService, IEnumerable<IMetadataProvider> providers)
        {
            _createImportJobUseCase = createImportJobUseCase;
            _dbContext = dbContext;
            _cancellationService = cancellationService;
            _providers = providers;
        }

        [HttpGet]
        public async Task<IActionResult> GetJobs()
        {
            var jobs = await _dbContext.Jobs
                .OrderByDescending(j => j.CreatedAt)
                .Take(100)
                .ToListAsync();

            return Ok(jobs);
        }

        /// <summary>
        /// 実行中・待機中のジョブを返す。Work識別子付き。
        /// </summary>
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveJobs()
        {
            var activeStatuses = new[]
            {
                WISE.Domain.Enums.JobStatus.Created,
                WISE.Domain.Enums.JobStatus.Queued,
                WISE.Domain.Enums.JobStatus.Running,
            };

            var jobs = await _dbContext.Jobs
                .Where(j => activeStatuses.Contains(j.Status))
                .OrderBy(j => j.CreatedAt)
                .ToListAsync();

            // Target = "Work_{guid}" → Work の PrimaryIdentifier を解決
            var workIds = jobs
                .Where(j => j.Target != null && j.Target.StartsWith("Work_"))
                .Select(j => {
                    Guid.TryParse(j.Target!["Work_".Length..], out var gid);
                    return gid;
                })
                .Where(g => g != Guid.Empty)
                .Distinct()
                .ToList();

            var identifiers = await _dbContext.Works
                .Where(w => workIds.Contains(w.Id))
                .Select(w => new { w.Id, w.PrimaryIdentifier })
                .ToDictionaryAsync(w => w.Id, w => w.PrimaryIdentifier);

            var result = jobs.Select(j => {
                string? identifier = null;
                if (j.Target != null && j.Target.StartsWith("Work_") &&
                    Guid.TryParse(j.Target["Work_".Length..], out var gid))
                    identifiers.TryGetValue(gid, out identifier);

                return new
                {
                    j.Id,
                    j.JobType,
                    Status = j.Status.ToString(),
                    j.Target,
                    Identifier = identifier,
                    j.CreatedAt,
                    j.StartedAt,
                    j.ErrorMessage,
                };
            });

            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetJob(Guid id)
        {
            var job = await _dbContext.Jobs.FindAsync(id);
            if (job == null) return NotFound();
            return Ok(job);
        }

        [HttpPost("import")]
        public async Task<IActionResult> EnqueueJob([FromBody] JobRequest request)
        {
            try
            {
                if (request.JobType != "Import")
                {
                    return BadRequest(new { Error = "Invalid JobType. Only 'Import' is supported in this endpoint." });
                }

                // Parse the generic JSON payload back to the specific ImportJobRequest
                var importRequest = request.Payload.HasValue
                    ? System.Text.Json.JsonSerializer.Deserialize<ImportJobRequest>(request.Payload.Value.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    : null;

                if (importRequest == null)
                {
                    return BadRequest(new { Error = "Invalid or missing Payload for Import job." });
                }

                var result = await _createImportJobUseCase.ExecuteAsync(importRequest);
                return Ok(result);
            }
            catch (System.Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// 指定したWorkIdのFetchMetadataジョブをキューに追加します。
        /// WorkId は GUID 文字列で指定してください。
        /// </summary>
        [HttpPost("fetchmetadata")]
        public async Task<IActionResult> EnqueueFetchMetadata([FromBody] System.Text.Json.JsonElement body)
        {
            try
            {
                string? workIdStr = null;
                if (body.TryGetProperty("WorkId", out var widEl) || body.TryGetProperty("workId", out widEl))
                    workIdStr = widEl.GetString();

                if (!Guid.TryParse(workIdStr, out var workId))
                    return BadRequest(new { Error = "WorkId (GUID) is required." });

                var work = await _dbContext.Works.FindAsync(workId);
                if (work == null) return NotFound(new { Error = $"Work {workId} not found." });

                var payload = System.Text.Json.JsonSerializer.Serialize(new { WorkId = workId });
                var job = new WISE.Domain.Entities.Job("FetchMetadata", $"Work_{workId}", payload);
                job.MarkAsQueued();
                _dbContext.Jobs.Add(job);
                await _dbContext.SaveChangesAsync();

                return Ok(new { JobId = job.Id, WorkId = workId, Message = "FetchMetadata job queued." });
            }
            catch (System.Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelJob(Guid id)
        {
            var job = await _dbContext.Jobs.FindAsync(id);
            if (job == null) return NotFound();

            if (job.Status == WISE.Domain.Enums.JobStatus.Running)
            {
                var canceled = _cancellationService.CancelJob(id);
                if (canceled)
                {
                    return Ok(new { Message = "Cancellation requested." });
                }
            }
            else if (job.Status == WISE.Domain.Enums.JobStatus.Queued || job.Status == WISE.Domain.Enums.JobStatus.Created)
            {
                job.MarkAsCanceled();
                await _dbContext.SaveChangesAsync();
                return Ok(new { Message = "Job canceled before running." });
            }

            return BadRequest(new { Error = "Job cannot be canceled in its current state." });
        }

        [HttpPost("{id}/retry")]
        public async Task<IActionResult> RetryJob(Guid id)
        {
            var job = await _dbContext.Jobs.FindAsync(id);
            if (job == null) return NotFound();

            if (job.Status == WISE.Domain.Enums.JobStatus.Failed || job.Status == WISE.Domain.Enums.JobStatus.Canceled)
            {
                // Re-queue the job
                job.MarkAsQueued();
                await _dbContext.SaveChangesAsync();
                return Ok(job);
            }

            return BadRequest(new { Error = "Only failed or canceled jobs can be retried." });
        }

        /// <summary>完了・失敗・キャンセル済みのジョブを一括削除する。</summary>
        [HttpDelete]
        public async Task<IActionResult> ClearFinishedJobs()
        {
            var finished = new[]
            {
                WISE.Domain.Enums.JobStatus.Completed,
                WISE.Domain.Enums.JobStatus.Failed,
                WISE.Domain.Enums.JobStatus.Canceled,
            };
            var count = await _dbContext.Jobs.Where(j => finished.Contains(j.Status)).CountAsync();
            await _dbContext.Jobs.Where(j => finished.Contains(j.Status)).ExecuteDeleteAsync();
            return Ok(new { deleted = count });
        }

        /// <summary>登録済みメタデータプロバイダー一覧を返す。</summary>
        [HttpGet("providers")]
        public IActionResult GetProviders()
        {
            var list = _providers
                .OrderBy(p => p.Priority)
                .Select(p => new { id = p.ProviderId, priority = p.Priority })
                .ToList();
            return Ok(list);
        }

        /// <summary>複数WorkのFetchMetadataジョブを一括キューに追加する。</summary>
        [HttpPost("fetchmetadata/batch")]
        public async Task<IActionResult> EnqueueFetchMetadataBatch([FromBody] System.Text.Json.JsonElement body)
        {
            var workIds = new List<Guid>();
            if (body.TryGetProperty("workIds", out var idsEl) || body.TryGetProperty("WorkIds", out idsEl))
            {
                foreach (var el in idsEl.EnumerateArray())
                    if (Guid.TryParse(el.GetString(), out var g)) workIds.Add(g);
            }
            if (workIds.Count == 0) return BadRequest(new { Error = "workIds is required." });

            int queued = 0;
            foreach (var workId in workIds)
            {
                var work = await _dbContext.Works.FindAsync(workId);
                if (work == null) continue;
                var payload = System.Text.Json.JsonSerializer.Serialize(new { WorkId = workId });
                var job = new WISE.Domain.Entities.Job("FetchMetadata", $"Work_{workId}", payload);
                job.MarkAsQueued();
                _dbContext.Jobs.Add(job);
                queued++;
            }
            await _dbContext.SaveChangesAsync();
            return Ok(new { Queued = queued });
        }
    }
}
