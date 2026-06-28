using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;

namespace Jellystat_API_Core.Services.TaskLogging
{
    public interface ITaskLogging
    {
        Task<Logs> CreateLogAsync(TaskTypes taskType, string serverId, CancellationToken cancellationToken = default);
        Task AddMessageAsync(Logs log, string message, CancellationToken cancellationToken = default);
        Task SuccessAsync(Logs log, CancellationToken cancellationToken = default);
        Task ErrorAsync(Logs log, CancellationToken cancellationToken = default);
    }
}
