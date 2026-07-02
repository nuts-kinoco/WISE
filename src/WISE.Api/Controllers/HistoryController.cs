using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;
using WISE.Application.Queries;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HistoryController : ControllerBase
    {
        private readonly IHistoryQueryService _historyQuery;

        public HistoryController(IHistoryQueryService historyQuery)
        {
            _historyQuery = historyQuery;
        }

        [HttpGet]
        public async Task<IActionResult> GetHistory(CancellationToken ct)
        {
            var history = await _historyQuery.GetRecentHistoryAsync(100, ct);
            return Ok(history);
        }
    }
}
