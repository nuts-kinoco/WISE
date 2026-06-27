using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using WISE.Application.DTOs;
using WISE.Application.Services;
using WISE.Api.UseCases;
using Microsoft.EntityFrameworkCore;
using WISE.Infrastructure.Data;
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

        public JobsController(CreateImportJobUseCase createImportJobUseCase, WiseDbContext dbContext, IJobCancellationService cancellationService)
        {
            _createImportJobUseCase = createImportJobUseCase;
            _dbContext = dbContext;
            _cancellationService = cancellationService;
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
    }
}
