using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using WISE.Infrastructure.Data;
using System.Collections.Generic;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HistoryController : ControllerBase
    {
        private readonly WiseDbContext _dbContext;

        public HistoryController(WiseDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public class HistoryDto
        {
            public Guid Id { get; set; }
            public string EventType { get; set; } = string.Empty;
            public string Actor { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
            public string? Payload { get; set; }
            public Guid? TargetId { get; set; }
            public string? TargetIdentifier { get; set; } // E.g., Work identifier
            public DateTime OccurredAt { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> GetHistory()
        {
            var eventLogs = await _dbContext.EventLogs
                .AsNoTracking()
                .Include(e => e.TargetWork)
                .OrderByDescending(e => e.OccurredAt)
                .Take(100)
                .ToListAsync();

            var history = eventLogs.Select(e => new HistoryDto
            {
                Id = e.Id,
                EventType = e.EventType,
                Actor = e.Actor,
                Source = e.Source,
                Payload = e.Payload,
                TargetId = e.TargetId,
                TargetIdentifier = e.TargetWork?.PrimaryIdentifier,
                OccurredAt = e.OccurredAt
            }).ToList();

            return Ok(history);
        }
    }
}
