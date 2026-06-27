namespace WISE.Application.DTOs;

public class ExecuteImportJobResult
{
    public int WorksAdded { get; set; }
    public int AssetsAdded { get; set; }
    public int DuplicatesMerged { get; set; }
}
