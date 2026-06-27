namespace WISE.Domain.Interfaces;

public interface IOutputPathResolver
{
    string Resolve(string outputFolder, string identifier, string originalFileName);
}
