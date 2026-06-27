using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WISE.Infrastructure.Data;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemController : ControllerBase
    {
        private readonly WiseDbContext _dbContext;

        public SystemController(WiseDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            // Real DB query
            var logs = await _dbContext.EventLogs
                .Include(e => e.TargetWork)
                .OrderByDescending(e => e.OccurredAt)
                .Take(50)
                .Select(e => new
                {
                    Id = e.Id,
                    Timestamp = e.OccurredAt,
                    EventType = e.EventType,
                    TargetWorkId = e.TargetId,
                    TargetWorkName = e.TargetWork != null ? e.TargetWork.PrimaryIdentifier : null,
                    Summary = e.Payload
                })
                .ToListAsync();

            return Ok(logs);
        }

        // v1.0 In-Memory Job Management
        private static readonly List<object> _mockJobs = new List<object>
        {
            new { Id = Guid.NewGuid(), Name = "Full Library Scan", Status = "Running", StartTime = DateTime.UtcNow.AddMinutes(-12), Duration = "12m 30s", Progress = 45, Error = (string?)null, TargetWorkId = (string?)null },
            new { Id = Guid.NewGuid(), Name = "Scheduled Backup", Status = "Pending", StartTime = (DateTime?)null, Duration = "-", Progress = 0, Error = (string?)null, TargetWorkId = (string?)null }
        };

        [HttpGet("jobs")]
        public IActionResult GetJobs()
        {
            // Future feature: Actually link this to a background job orchestrator.
            // For v1.0, user allowed in-memory.
            return Ok(_mockJobs);
        }
    }
}
