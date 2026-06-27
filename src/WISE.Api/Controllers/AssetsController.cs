using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Threading.Tasks;
using WISE.Infrastructure.Data;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AssetsController : ControllerBase
    {
        private readonly WiseDbContext _dbContext;

        public AssetsController(WiseDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("{id}/content")]
        public async Task<IActionResult> GetContent(string id)
        {
            if (!System.Guid.TryParse(id, out var assetId))
            {
                return BadRequest("Invalid Asset ID format.");
            }

            var asset = await _dbContext.Assets
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == assetId);

            if (asset == null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(asset.FilePath) || !System.IO.File.Exists(asset.FilePath))
            {
                // Fallback to dummy image for UI development
                return Redirect($"https://picsum.photos/seed/{asset.Id}/400/600");
            }

            var contentType = GetContentType(asset.FilePath);
            var fileStream = new FileStream(asset.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return File(fileStream, contentType, enableRangeProcessing: true);
        }

        private string GetContentType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".mp4" => "video/mp4",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "application/octet-stream",
            };
        }
    }
}
