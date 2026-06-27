using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using WISE.Api.UseCases;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImportController : ControllerBase
    {
        private readonly ImportUseCase _importUseCase;

        public ImportController(ImportUseCase importUseCase)
        {
            _importUseCase = importUseCase;
        }

        public class AnalyzeRequest
        {
            public string DirectoryPath { get; set; } = string.Empty;
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest request)
        {
            try
            {
                var result = await _importUseCase.AnalyzeDirectoryAsync(request.DirectoryPath);
                return Ok(result);
            }
            catch (System.Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


    }
}
