using System.Threading.Tasks;
using System.Text.Json;
using WISE.Application.DTOs;
using WISE.Infrastructure.Data;
using WISE.Domain.Entities;

namespace WISE.Api.UseCases;

public class CreateImportJobUseCase
{
    private readonly WiseDbContext _dbContext;
    private readonly ExecuteImportJobUseCase _executeImportJobUseCase;

    public CreateImportJobUseCase(WiseDbContext dbContext, ExecuteImportJobUseCase executeImportJobUseCase)
    {
        _dbContext = dbContext;
        _executeImportJobUseCase = executeImportJobUseCase;
    }

    public async Task<object> ExecuteAsync(ImportJobRequest request)
    {
        var payloadJson = JsonSerializer.Serialize(request);
        var job = new Job("Import", "System", payloadJson);
        
        job.MarkAsQueued(); // Just queue it. BackgroundWorker will pick it up.
        _dbContext.Jobs.Add(job);
        await _dbContext.SaveChangesAsync();

        return new { JobId = job.Id };
    }
}
