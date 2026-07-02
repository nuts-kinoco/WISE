using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WISE.Application.DTOs;
using WISE.Application.Queries;
using WISE.Api.UseCases;
using WISE.Domain.Interfaces;
using System.Linq;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class JobsController : ControllerBase
    {
        private readonly CreateImportJobUseCase _createImportJobUseCase;
        private readonly IJobsQueryService _query;
        private readonly JobUseCase _jobUseCase;
        private readonly IEnumerable<IMetadataProvider> _providers;

        public JobsController(
            CreateImportJobUseCase createImportJobUseCase,
            IJobsQueryService query,
            JobUseCase jobUseCase,
            IEnumerable<IMetadataProvider> providers)
        {
            _createImportJobUseCase = createImportJobUseCase;
            _query = query;
            _jobUseCase = jobUseCase;
            _providers = providers;
        }

        [HttpGet]
        public async Task<IActionResult> GetJobs(CancellationToken ct)
        {
            var jobs = await _query.GetRecentAsync(100, ct);
            return Ok(jobs);
        }

        /// <summary>
        /// 実行中・待機中のジョブを返す。Work識別子付き。
        /// </summary>
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveJobs(CancellationToken ct)
        {
            var jobs = await _query.GetActiveAsync(ct);
            return Ok(jobs);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetJob(Guid id, CancellationToken ct)
        {
            var job = await _query.GetByIdAsync(id, ct);
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
        public async Task<IActionResult> EnqueueFetchMetadata([FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
        {
            try
            {
                string? workIdStr = null;
                if (body.TryGetProperty("WorkId", out var widEl) || body.TryGetProperty("workId", out widEl))
                    workIdStr = widEl.GetString();

                if (!Guid.TryParse(workIdStr, out var workId))
                    return BadRequest(new { Error = "WorkId (GUID) is required." });

                var (found, jobId) = await _jobUseCase.EnqueueFetchMetadataAsync(workId, ct);
                if (!found) return NotFound(new { Error = $"Work {workId} not found." });

                return Ok(new { JobId = jobId, WorkId = workId, Message = "FetchMetadata job queued." });
            }
            catch (System.Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelJob(Guid id, CancellationToken ct)
        {
            var result = await _jobUseCase.CancelAsync(id, ct);
            return result switch
            {
                JobUseCase.CancelResult.NotFound => NotFound(),
                JobUseCase.CancelResult.CancellationRequested => Ok(new { Message = "Cancellation requested." }),
                JobUseCase.CancelResult.CanceledBeforeRunning => Ok(new { Message = "Job canceled before running." }),
                _ => BadRequest(new { Error = "Job cannot be canceled in its current state." }),
            };
        }

        [HttpPost("{id}/retry")]
        public async Task<IActionResult> RetryJob(Guid id, CancellationToken ct)
        {
            var (result, job) = await _jobUseCase.RetryAsync(id, ct);
            return result switch
            {
                JobUseCase.RetryResult.NotFound => NotFound(),
                JobUseCase.RetryResult.Ok => Ok(job),
                _ => BadRequest(new { Error = "Only failed or canceled jobs can be retried." }),
            };
        }

        /// <summary>完了・失敗・キャンセル済みのジョブを一括削除する。</summary>
        [HttpDelete]
        public async Task<IActionResult> ClearFinishedJobs(CancellationToken ct)
        {
            var count = await _jobUseCase.ClearFinishedAsync(ct);
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
        public async Task<IActionResult> EnqueueFetchMetadataBatch([FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
        {
            var workIds = new List<Guid>();
            if (body.TryGetProperty("workIds", out var idsEl) || body.TryGetProperty("WorkIds", out idsEl))
            {
                foreach (var el in idsEl.EnumerateArray())
                    if (Guid.TryParse(el.GetString(), out var g)) workIds.Add(g);
            }
            if (workIds.Count == 0) return BadRequest(new { Error = "workIds is required." });

            var queued = await _jobUseCase.EnqueueFetchMetadataBatchAsync(workIds, ct);
            return Ok(new { Queued = queued });
        }
    }
}
