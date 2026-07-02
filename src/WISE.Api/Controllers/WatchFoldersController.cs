using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;
using WISE.Api.UseCases;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WatchFoldersController : ControllerBase
    {
        private readonly WatchFolderUseCase _useCase;

        public WatchFoldersController(WatchFolderUseCase useCase)
        {
            _useCase = useCase;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            var folders = await _useCase.GetAllAsync(ct);
            return Ok(folders);
        }

        public class CreateWatchFolderRequest
        {
            public string Path { get; set; } = string.Empty;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateWatchFolderRequest request, CancellationToken ct)
        {
            var (success, error, created) = await _useCase.CreateAsync(request.Path, ct);
            if (!success)
                return error == "Watch folder already exists." ? Conflict(error) : BadRequest(error);

            return CreatedAtAction(nameof(GetAll), new { id = created!.Id }, created);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(System.Guid id, CancellationToken ct)
        {
            var deleted = await _useCase.DeleteAsync(id, ct);
            return deleted ? NoContent() : NotFound();
        }

        [HttpPatch("{id}/toggle")]
        public async Task<IActionResult> Toggle(System.Guid id, CancellationToken ct)
        {
            var folder = await _useCase.ToggleAsync(id, ct);
            return folder == null ? NotFound() : Ok(folder);
        }
    }
}
