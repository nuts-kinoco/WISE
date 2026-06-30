using System.Collections.Generic;
using System.Linq;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Archive;

public class ArchiveReaderSelector
{
    private readonly IReadOnlyList<IArchiveReader> _readers;

    public ArchiveReaderSelector(IEnumerable<IArchiveReader> readers)
        => _readers = readers.ToList();

    public IArchiveReader? Select(string filePath)
        => _readers.FirstOrDefault(r => r.CanRead(filePath));
}
