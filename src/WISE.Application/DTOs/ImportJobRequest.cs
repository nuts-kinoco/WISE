using System.Collections.Generic;

namespace WISE.Application.DTOs;

public class ImportJobRequest
{
    public List<string> InputFolders { get; set; } = new();
    public List<string> InputFiles { get; set; } = new();
    public string? OutputFolder { get; set; }
    
    /// <summary>
    /// "Move" or "Copy"
    /// </summary>
    public string ImportMode { get; set; } = "Copy";
    
    // Future Expansion Fields
    public bool UseMetadataPipeline { get; set; } = true;
    public string? RuleProfile { get; set; }
    public string Priority { get; set; } = "Normal";
    public string? RequestedBy { get; set; }
}
