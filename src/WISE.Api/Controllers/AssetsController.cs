using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using WISE.Application.Queries;
using WISE.Infrastructure.Services;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AssetsController : ControllerBase
    {
        private readonly IAssetsQueryService _query;
        private readonly VideoStreamCache _videoCache;

        public AssetsController(IAssetsQueryService query, VideoStreamCache videoCache)
        {
            _query = query;
            _videoCache = videoCache;
        }

        [HttpGet("{id}/content")]
        public async Task<IActionResult> GetContent(string id)
        {
            if (!System.Guid.TryParse(id, out var assetId))
                return BadRequest("Invalid Asset ID format.");

            var asset = await _query.GetByIdAsync(assetId, HttpContext.RequestAborted);

            if (asset == null)
                return NotFound();

            if (string.IsNullOrEmpty(asset.FilePath) || !System.IO.File.Exists(asset.FilePath))
                return NotFound();

            var contentType = GetContentType(asset.FilePath);

            // ビデオはバックグラウンドでキャッシュをウォームアップ
            if (contentType.StartsWith("video/"))
                _ = _videoCache.WarmAsync(asset.FilePath, HttpContext.RequestAborted);

            // Range Request でキャッシュにヒットする場合はメモリから返す
            if (contentType.StartsWith("video/")
                && HttpContext.Request.Headers.TryGetValue("Range", out var rangeHeader)
                && TryParseFirstRange(rangeHeader.ToString(), new FileInfo(asset.FilePath).Length, out var offset, out var count))
            {
                var cachedData = _videoCache.TryGetRange(asset.FilePath, offset, count);
                if (cachedData != null)
                {
                    var fileLen = new FileInfo(asset.FilePath).Length;
                    Response.StatusCode = 206;
                    Response.ContentType = contentType;
                    Response.Headers.ContentLength = cachedData.Length;
                    Response.Headers.ContentRange = $"bytes {offset}-{offset + cachedData.Length - 1}/{fileLen}";
                    await Response.Body.WriteAsync(cachedData);
                    return new EmptyResult();
                }
            }

            // キャッシュミス / 非ビデオ → PhysicalFile でOSのゼロコピー送信
            Response.Headers["Cache-Control"] = "no-store";
            return PhysicalFile(asset.FilePath, contentType, enableRangeProcessing: true);
        }

        /// <summary>
        /// Range ヘッダーの最初の範囲を解析する（キャッシュ可能サイズのみ受け付ける）
        /// </summary>
        private static bool TryParseFirstRange(string header, long fileLen, out long offset, out int count)
        {
            offset = 0;
            count = 0;

            if (!header.StartsWith("bytes=")) return false;
            var first = header[6..].Split(',')[0].Trim();
            var dash = first.IndexOf('-');
            if (dash < 0) return false;

            var startStr = first[..dash];
            var endStr = first[(dash + 1)..];

            if (!long.TryParse(startStr, out offset)) return false;

            long end;
            if (string.IsNullOrEmpty(endStr))
                end = fileLen - 1;
            else if (!long.TryParse(endStr, out end))
                return false;

            var len = end - offset + 1;
            if (len <= 0) return false;

            count = (int)System.Math.Min(len, 32 * 1024 * 1024);
            return offset >= 0 && offset < fileLen;
        }

        private static string GetContentType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".mp4"            => "video/mp4",
                ".mkv"            => "video/x-matroska",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png"            => "image/png",
                ".gif"            => "image/gif",
                ".webp"           => "image/webp",
                _                 => "application/octet-stream",
            };
        }
    }
}
