using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WISE.Domain.Interfaces;
using WISE.Domain.Entities;
using WISE.Domain.ValueObjects;

namespace WISE.Domain.Providers;

public class FileNameEvidenceProvider : IEvidenceProvider
{
    public string ProviderId => "Core.FileNameProvider";

    public Task<IEnumerable<Evidence>> CollectEvidencesAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        var evidences = new List<Evidence>();

        if (!string.IsNullOrWhiteSpace(asset.OriginalFilename))
        {
            if (asset.OriginalFilename.Contains("[") && asset.OriginalFilename.Contains("]"))
            {
                evidences.Add(new Evidence("FileNameMatch", asset.OriginalFilename, new ConfidenceScore(50), ProviderId));
            }
            else
            {
                evidences.Add(new Evidence("FileNameMatch", asset.OriginalFilename, new ConfidenceScore(10), ProviderId));
            }
        }

        return Task.FromResult<IEnumerable<Evidence>>(evidences);
    }
}
