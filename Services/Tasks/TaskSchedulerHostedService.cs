namespace Jellystat_API_Core.Services.Tasks
{
    public class TaskSchedulerHostedService : IHostedService
    {
        private readonly ITaskSchedulerService _taskSchedulerService;
        private readonly ILogger<TaskSchedulerHostedService> _logger;

        public TaskSchedulerHostedService(
            ITaskSchedulerService taskSchedulerService,
            ILogger<TaskSchedulerHostedService> logger)
        {
            _taskSchedulerService = taskSchedulerService;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Task Scheduler Hosted Service is starting.");
            await _taskSchedulerService.StartAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Task Scheduler Hosted Service is stopping.");
            await _taskSchedulerService.StopAsync(cancellationToken);
        }
    }
}
