using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WISE.Domain.Interfaces;

public record ArchivePage(int Index, string FileName, string ContentType);

public interface IArchiveReader
{
    bool CanRead(string filePath);
    Task<IReadOnlyList<ArchivePage>> GetPagesAsync(string filePath, CancellationToken ct = default);
    Task<Stream> OpenPageAsync(string filePath, int pageIndex, CancellationToken ct = default);
}
