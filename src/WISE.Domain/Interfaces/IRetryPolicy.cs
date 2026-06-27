using System;
using System.Threading.Tasks;

namespace WISE.Domain.Interfaces;

public interface IRetryPolicy
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action);
    Task ExecuteAsync(Func<Task> action);
}
