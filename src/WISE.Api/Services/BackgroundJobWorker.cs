using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using WISE.Application.DTOs;
using WISE.Application.Services;
using WISE.Api.UseCases; // Using this namespace to access ExecuteImportJobUseCase directly
using WISE.Domain.Enums;
using WISE.Infrastructure.Data;

namespace WISE.Api.Services;

public class BackgroundJobWorker : BackgroundService
{
    private readonly ILogger<BackgroundJobWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobCancellationService _cancellationService;

    public BackgroundJobWorker(ILogger<BackgroundJobWorker> logger, IServiceScopeFactory scopeFactory, IJobCancellationService cancellationService)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _cancellationService = cancellationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BackgroundJobWorker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in BackgroundJobWorker.");
            }

            await Task.Delay(2000, stoppingToken);
        }

        _logger.LogInformation("BackgroundJobWorker is stopping.");
    }

    private async Task ProcessNextJobAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WiseDbContext>();

        // Find the oldest queued job
        var job = await dbContext.Jobs
            .FirstOrDefaultAsync(j => j.Status == JobStatus.Queued, stoppingToken);

        if (job == null) return;

        // Mark as running
        job.MarkAsRunning();
        await dbContext.SaveChangesAsync(stoppingToken);

        _logger.LogInformation($"Starting Job {job.Id} of type {job.JobType}");

        // Create a linked cancellation token that can be triggered by either the service stopping or a user cancellation request
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _cancellationService.RegisterJob(job.Id, cts);

        try
        {
            if (job.JobType == "Import")
            {
                using var executionScope = _scopeFactory.CreateScope();
                var executeUseCase = executionScope.ServiceProvider.GetRequiredService<ExecuteImportJobUseCase>();
                var request = JsonSerializer.Deserialize<ImportJobRequest>(job.Payload ?? "{}");

                if (request != null)
                {
                    DateTime lastUpdate = DateTime.MinValue;

                    var result = await executeUseCase.ExecuteAsync(request, (processed, total) => 
                    {
                        // Throttle DB updates to avoid locking SQLite too much
                        if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds > 500 || processed == total)
                        {
                            job.UpdateProgress(processed, total);
                            dbContext.SaveChanges();
                            lastUpdate = DateTime.UtcNow;
                        }
                    }, cts.Token);

                    var resultJson = JsonSerializer.Serialize(result);
                    job.MarkAsCompleted(resultJson);
                }
                else
                {
                    job.MarkAsFailed("Payload is invalid");
                }
            }
            else if (job.JobType == "FetchMetadata")
            {
                var payloadNode = JsonDocument.Parse(job.Payload ?? "{}").RootElement;
                if (payloadNode.TryGetProperty("WorkId", out var workIdElement) && Guid.TryParse(workIdElement.GetString(), out var workId))
                {
                    job.UpdateProgress(0, 1);
                    dbContext.SaveChanges();

                    int maxRetries = 3;
                    int attempts = 0;
                    string? resultJson = null;
                    Exception? lastException = null;

                    while (attempts < maxRetries)
                    {
                        try
                        {
                            using var executionScope = _scopeFactory.CreateScope();
                            var fetchMetadataUseCase = executionScope.ServiceProvider.GetRequiredService<FetchMetadataJobUseCase>();
                            
                            attempts++;
                            resultJson = await fetchMetadataUseCase.ExecuteAsync(workId, cts.Token);
                            break; // Success
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            if (attempts >= maxRetries) break;
                            await Task.Delay(2000, cts.Token); // Wait before retry
                        }
                    }

                    if (resultJson != null)
                    {
                        job.UpdateProgress(1, 1);
                        job.MarkAsCompleted(resultJson);
                    }
                    else
                    {
                        job.MarkAsFailed($"Failed after {maxRetries} attempts. Last error: {lastException?.Message}");
                    }
                }
                else
                {
                    job.MarkAsFailed("Payload does not contain valid WorkId");
                }
            }
            else if (job.JobType == "RebuildFts")
            {
                // 同一 JobType が後続にキューされている場合、このジョブは重複とみなしてスキップ。
                // 並行 FetchMetadata 完了で複数 RebuildFts がキューされる Race Condition への対処。
                // RebuildFts 自体は冪等なので、最後の 1 件だけ実行すれば十分。
                var hasDuplicate = await dbContext.Jobs
                    .AnyAsync(j => j.JobType == "RebuildFts" && j.Status == JobStatus.Queued && j.Id != job.Id,
                              stoppingToken);
                if (hasDuplicate)
                {
                    job.MarkAsCompleted("Deduplicated: a later RebuildFts job will run.");
                }
                else
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "INSERT INTO METADATA_FIELD_FTS(METADATA_FIELD_FTS) VALUES('rebuild')",
                        stoppingToken);
                    job.MarkAsCompleted("FTS5 rebuild complete.");
                }
            }
            else
            {
                job.MarkAsFailed($"Unknown job type: {job.JobType}");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning($"Job {job.Id} was canceled.");
            job.MarkAsCanceled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Job {job.Id} failed.");
            job.MarkAsFailed(ex.Message);
        }
        finally
        {
            _cancellationService.UnregisterJob(job.Id);
            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}
