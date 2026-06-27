using System.IO;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Services;

public class DefaultOutputPathResolver : IOutputPathResolver
{
    public string Resolve(string outputFolder, string identifier, string originalFileName)
    {
        return Path.Combine(outputFolder, identifier, originalFileName);
    }
}
