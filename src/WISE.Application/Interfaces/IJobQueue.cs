using System.Threading.Tasks;
using WISE.Application.DTOs;

namespace WISE.Application.Interfaces;

public interface IJobQueue
{
    Task<string> EnqueueImportJobAsync(ImportJobRequest request);
}
