namespace WISE.Domain.Models;

public class MetadataProviderOptions
{
    public int Priority { get; set; } = 50;
    public bool IsEnabled { get; set; } = true;
}
