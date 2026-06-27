namespace WISE.Domain.Models;

public record ResolvedMetadataCandidate(
    MetadataCandidate Candidate,
    bool IsPrimary
);
