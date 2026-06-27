using System.Collections.Generic;
using System.Threading.Tasks;

namespace WISE.Application.Services;

public record ImportPreviewDto(
    int TotalFiles,
    int NewWorksCount,
    int DuplicateCandidatesCount,
    int UnknownCount,
    List<ImportPreviewItemDto> Items
);

public record ImportPreviewItemDto(
    string FileName,
    string FilePath,
    string Status, // "New", "Duplicate", "Unknown"
    string SuggestedIdentifier,
    string SuggestedTitle
);

public interface IDummyPipelineService
{
    Task<ImportPreviewDto> GeneratePreviewAsync(string folderPath);
    Task ExecuteImportAsync(ImportPreviewDto preview);
}
