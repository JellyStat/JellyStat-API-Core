using Jellystat_API_Core.Data;
using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using Jellystat_API_Core.Services.Reports.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Jellystat_API_Core.Services.Reports.Delivery
{
    public record ReportDeliveryResult(bool Success, string? Error, string? FileName, long ElapsedMs);

    public interface IReportScheduleRunner
    {
        /// <summary>Renders the schedule's report and posts it to the webhook, recording the outcome on the schedule.</summary>
        Task<ReportDeliveryResult> RunAsync(string scheduleId, CancellationToken cancellationToken = default);
    }

    public class ReportScheduleRunner : IReportScheduleRunner
    {
        private readonly ApplicationDbContext _db;
        private readonly IReportService _reports;
        private readonly IReportExporter _exporter;
        private readonly IReportWebhookSender _sender;
        private readonly ILogger<ReportScheduleRunner> _logger;

        public ReportScheduleRunner(ApplicationDbContext db, IReportService reports, IReportExporter exporter, IReportWebhookSender sender, ILogger<ReportScheduleRunner> logger)
        {
            _db = db;
            _reports = reports;
            _exporter = exporter;
            _sender = sender;
            _logger = logger;
        }

        public async Task<ReportDeliveryResult> RunAsync(string scheduleId, CancellationToken cancellationToken = default)
        {
            var started = DateTime.UtcNow;
            var schedule = await _db.ReportSchedules.Include(s => s.Report).FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken)
                ?? throw new ReportDefinitionException("Schedule not found");

            ReportDeliveryResult result;
            try
            {
                var definition = _reports.Parse(schedule.Report!.Definition);
                var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(string.IsNullOrWhiteSpace(schedule.Parameters) ? "{}" : schedule.Parameters, ReportJson.Options);
                // Scheduled runs act for the whole server: there's no signed-in user, and only admins can create schedules
                var model = await _reports.BuildAsync(definition, schedule.Report.Name, schedule.ReportId, new ReportScope(schedule.ServerId, null), parameters, null, cancellationToken);
                var file = _exporter.Export(model, new ReportExportOptions { Format = schedule.Format });
                await _sender.SendAsync(new ReportWebhookTarget
                {
                    Url = schedule.WebhookUrl,
                    Type = schedule.WebhookType,
                    Secret = schedule.WebhookSecret,
                    Message = schedule.Message
                }, model, file, cancellationToken);

                result = new ReportDeliveryResult(true, null, file.FileName, (long)(DateTime.UtcNow - started).TotalMilliseconds);
                _logger.LogInformation("Report schedule {Name} delivered {File}", schedule.Name, file.FileName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Report schedule {Name} failed", schedule.Name);
                result = new ReportDeliveryResult(false, ex.Message, null, (long)(DateTime.UtcNow - started).TotalMilliseconds);
            }

            schedule.LastRunAt = started;
            schedule.LastSuccess = result.Success;
            schedule.LastError = result.Error is { Length: > 1000 } e ? e[..1000] : result.Error;
            schedule.NextRunAt = started.AddMinutes(Math.Max(1, schedule.IntervalMinutes));
            await _db.SaveChangesAsync(CancellationToken.None);
            return result;
        }
    }

    /// <summary>Checks for due report schedules every 30 seconds and runs them one at a time.</summary>
    public class ReportScheduleHostedService : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReportScheduleHostedService> _logger;

        public ReportScheduleHostedService(IServiceScopeFactory scopeFactory, ILogger<ReportScheduleHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let startup (migrations) finish first
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    List<string> due;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var now = DateTime.UtcNow;
                        due = await db.ReportSchedules
                            .Where(s => s.Enabled && s.IntervalMinutes > 0 && (s.NextRunAt == null || s.NextRunAt <= now))
                            .OrderBy(s => s.NextRunAt)
                            .Select(s => s.Id)
                            .ToListAsync(stoppingToken);
                    }

                    foreach (var id in due)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<IReportScheduleRunner>().RunAsync(id, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Report scheduler loop failed");
                }

                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
