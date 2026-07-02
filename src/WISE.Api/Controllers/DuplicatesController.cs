using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;
using WISE.Api.UseCases;
using WISE.Application.Queries;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/duplicates")]
    public class DuplicatesController : ControllerBase
    {
        private readonly IDuplicatesQueryService _query;
        private readonly DuplicateResolveUseCase _resolveUseCase;

        public DuplicatesController(IDuplicatesQueryService query, DuplicateResolveUseCase resolveUseCase)
        {
            _query = query;
            _resolveUseCase = resolveUseCase;
        }

        /// <summary>
        /// 重複作品グループを返す。
        /// detectionType: "identifier" = PrimaryIdentifier完全一致, "title" = タイトル正規化一致
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetDuplicates(CancellationToken ct)
        {
            var result = await _query.GetDuplicateGroupsAsync(ct);
            return Ok(result);
        }

        public record ResolveRequest(
            System.Guid KeepWorkId,
            System.Guid[] DeleteWorkIds,
            bool DeleteFiles,
            bool MergeRating,
            bool MergeMemo,
            bool MergeUserTags,
            bool MergeFavorite
        );

        /// <summary>
        /// 重複を解決する: keepWork を保持し、DeleteWorkIds を全て削除する。
        /// 3件以上の重複グループにも対応。
        /// </summary>
        [HttpPost("resolve")]
        public async Task<IActionResult> Resolve([FromBody] ResolveRequest req, CancellationToken ct)
        {
            if (req.DeleteWorkIds == null || req.DeleteWorkIds.Length == 0)
                return BadRequest(new { Error = "DeleteWorkIds must not be empty." });

            var (outcome, filesDeleted, filesFailed) = await _resolveUseCase.ResolveAsync(
                new DuplicateResolveUseCase.ResolveRequest(
                    req.KeepWorkId, req.DeleteWorkIds, req.DeleteFiles,
                    req.MergeRating, req.MergeMemo, req.MergeUserTags, req.MergeFavorite),
                ct);

            if (outcome == DuplicateResolveUseCase.ResolveOutcome.KeepWorkNotFound)
                return NotFound(new { Error = "keepWork not found." });

            return Ok(new { resolved = true, filesDeleted, filesFailed });
        }
    }
}
