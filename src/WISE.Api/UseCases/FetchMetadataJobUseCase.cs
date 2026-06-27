using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;
using WISE.Domain.Models;
using WISE.Infrastructure.Data;

namespace WISE.Api.UseCases;

public class FetchMetadataJobUseCase
{
    private readonly WiseDbContext _dbContext;
    private readonly IEnumerable<IMetadataProvider> _providers;

    public FetchMetadataJobUseCase(WiseDbContext dbContext, IEnumerable<IMetadataProvider> providers)
    {
        _dbContext = dbContext;
        _providers = providers.OrderByDescending(p => p.Priority).ToList();
    }

    public async Task<string> ExecuteAsync(Guid workId, CancellationToken cancellationToken = default)
    {
        var work = await _dbContext.Works
            .Include(w => w.MetadataFields)
            .FirstOrDefaultAsync(w => w.Id == workId, cancellationToken);

        if (work == null)
        {
            throw new Exception($"Work with ID {workId} not found.");
        }

        var context = new MetadataProviderContext(work.Id, work.PrimaryIdentifier, work.MetadataFields, "ja", cancellationToken);
        var allCandidates = new List<MetadataCandidate>();

        foreach (var provider in _providers)
        {
            try
            {
                var candidates = await provider.FetchAsync(context);
                allCandidates.AddRange(candidates);
            }
            catch (Exception ex)
            {
                // Log and continue to next provider
                Console.WriteLine($"Provider {provider.ProviderId} failed: {ex.Message}");
            }
        }

        if (!allCandidates.Any())
        {
            throw new Exception("No metadata fetched from any provider. This will trigger a retry if attempts < 3.");
        }

        int addedCount = 0;
        foreach (var candidate in allCandidates)
        {
            // Simple logic: if field doesn't exist, add it
            var existingField = work.MetadataFields.FirstOrDefault(m => m.FieldName == candidate.FieldName);
            if (existingField == null)
            {
                work.AddMetadata(new MetadataField(candidate.FieldName, candidate.Value, candidate.ProviderId, true, candidate.Confidence));
                addedCount++;
            }
            else if (existingField.ConfidenceScore < candidate.Confidence)
            {
                existingField.UpdateValue(candidate.Value, candidate.Confidence, candidate.ProviderId);
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            var log = new EventLog(work.Id, "Metadata Updated", "System", "FetchMetadataJob", $"Updated {addedCount} fields");
            _dbContext.EventLogs.Add(log);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return $"{{\"addedCount\": {addedCount}}}";
    }
}
