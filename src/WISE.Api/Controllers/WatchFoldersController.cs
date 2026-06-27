using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Infrastructure.Data;
using System.Linq;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WatchFoldersController : ControllerBase
    {
        private readonly WiseDbContext _dbContext;

        public WatchFoldersController(WiseDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var folders = await _dbContext.WatchFolders.OrderByDescending(w => w.CreatedAt).ToListAsync();
            return Ok(folders);
        }

        public class CreateWatchFolderRequest
        {
            public string Path { get; set; } = string.Empty;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateWatchFolderRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Path))
                return BadRequest("Path is required.");

            if (await _dbContext.WatchFolders.AnyAsync(w => w.Path == request.Path))
                return Conflict("Watch folder already exists.");

            var watchFolder = new WatchFolder(request.Path);
            _dbContext.WatchFolders.Add(watchFolder);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAll), new { id = watchFolder.Id }, watchFolder);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(System.Guid id)
        {
            var folder = await _dbContext.WatchFolders.FindAsync(id);
            if (folder == null)
                return NotFound();

            _dbContext.WatchFolders.Remove(folder);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        [HttpPatch("{id}/toggle")]
        public async Task<IActionResult> Toggle(System.Guid id)
        {
            var folder = await _dbContext.WatchFolders.FindAsync(id);
            if (folder == null)
                return NotFound();

            if (folder.IsEnabled)
                folder.Disable();
            else
                folder.Enable();

            await _dbContext.SaveChangesAsync();

            return Ok(folder);
        }
    }
}
