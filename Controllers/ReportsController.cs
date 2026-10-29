using Jellystat_API_Core.Attributes;
using Jellystat_API_Core.Data;
using Jellystat_API_Core.Data.Schema;
using Jellystat_API_Core.Enums;
using Jellystat_API_Core.Extensions;
using Jellystat_API_Core.Models.Reports;
using Jellystat_API_Core.Services.Reports;
using Jellystat_API_Core.Services.Reports.Datasets;
using Jellystat_API_Core.Services.Reports.Delivery;
using Jellystat_API_Core.Services.Reports.Query;
using Jellystat_API_Core.Services.Reports.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Jellystat_API_Core.Controllers
{
    /// <summary>
    /// Custom reports: designer metadata, saving, previews, rendering (JSON/SVG/HTML/PNG/PDF) and webhook delivery.
    /// Anyone signed in can view and render reports, limited to the data they can already see; changing reports,
    /// schedules and webhooks needs an administrator (an admin API key works, for automations).
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly ApplicationDbContext context;
        private readonly IReportService _reports;
        private readonly IReportQueryExecutor _executor;
        private readonly IReportDatasetRegistry _datasets;
        private readonly IReportExporter _exporter;
        private readonly IReportWebhookSender _webhooks;
        private readonly IReportScheduleRunner _scheduleRunner;

        public ReportsController(ApplicationDbContext context, IReportService reports, IReportQueryExecutor executor, IReportDatasetRegistry datasets,
            IReportExporter exporter, IReportWebhookSender webhooks, IReportScheduleRunner scheduleRunner)
        {
            this.context = context;
            _reports = reports;
            _executor = executor;
            _datasets = datasets;
            _exporter = exporter;
            _webhooks = webhooks;
            _scheduleRunner = scheduleRunner;
        }

        private ReportScope Scope(string? serverId)
        {
            if (!HttpContext.User.IsAdministrator() || string.IsNullOrEmpty(serverId))
            {
                serverId = HttpContext.User.GetServerId();
            }
            return new ReportScope(serverId, HttpContext.User.GetScopedUserId());
        }

        private static ReportSummary ToSummary(Data.Schema.Reports r, int scheduleCount = 0) => new()
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
            CreatedBy = r.CreatedBy,
            UpdatedBy = r.UpdatedBy,
            ScheduleCount = scheduleCount
        };

        // ------------------------------------------------------------------ metadata

        [HttpGet("Capabilities", Name = "ReportCapabilities")]
        public ReportCapabilities Capabilities() => new()
        {
            Datasets = _datasets.All.Select(d => new ReportDatasetInfo { Key = d.Key, Label = d.Label, Description = d.Description, Fields = d.Fields }).ToList(),
            Engines = [ReportQueryEngines.EfCore, ReportQueryEngines.Gridify],
            Operators = ReportFilterOperators.All,
            Aggregates = ReportAggregateFunctions.All,
            DateBuckets = ReportDateBuckets.All,
            Formats = ReportFormatter.Formats,
            ExportFormats = ReportFormats.All,
            WebhookTypes = ReportWebhookTypes.All,
            MaxRows = ReportQueryExecutor.MaxLimit
        };

        // ------------------------------------------------------------------ reports

        [HttpGet("List", Name = "ListReports")]
        public async Task<List<ReportSummary>> List()
        {
            var reports = await context.Reports.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
            var counts = await context.ReportSchedules.GroupBy(s => s.ReportId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            return reports.Select(r => ToSummary(r, counts.GetValueOrDefault(r.Id))).ToList();
        }

        [HttpGet("Get", Name = "GetReport")]
        public async Task<ActionResult<ReportDetail>> Get(string Id)
        {
            var report = await context.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == Id);
            if (report == null) return NotFound("Report not found");
            var summary = ToSummary(report, await context.ReportSchedules.CountAsync(s => s.ReportId == Id));
            return new ReportDetail
            {
                Id = summary.Id,
                Name = summary.Name,
                Description = summary.Description,
                CreatedAt = summary.CreatedAt,
                UpdatedAt = summary.UpdatedAt,
                CreatedBy = summary.CreatedBy,
                UpdatedBy = summary.UpdatedBy,
                ScheduleCount = summary.ScheduleCount,
                Definition = _reports.Parse(report.Definition)
            };
        }

        [HttpPost("Save", Name = "SaveReport")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult<ReportSummary>> Save([FromBody] SaveReportModel model)
        {
            string name = (model.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 200) return BadRequest("A report name of 1-200 characters is required");
            try
            {
                _reports.Validate(model.Definition);
            }
            catch (ReportDefinitionException ex)
            {
                return BadRequest(ex.Message);
            }

            var now = DateTime.UtcNow;
            Data.Schema.Reports? report = null;
            if (!string.IsNullOrEmpty(model.Id))
            {
                report = await context.Reports.FirstOrDefaultAsync(r => r.Id == model.Id);
                if (report == null) return NotFound("Report not found");
            }
            if (await context.Reports.AnyAsync(r => r.Name.ToLower() == name.ToLower() && r.Id != (report == null ? "" : report.Id)))
                return BadRequest("A report with this name already exists");

            if (report == null)
            {
                report = new Data.Schema.Reports
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Definition = "{}",
                    CreatedAt = now,
                    CreatedBy = HttpContext.User.GetUsername()
                };
                context.Reports.Add(report);
            }
            report.Name = name;
            report.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            report.Definition = _reports.Serialize(model.Definition);
            report.UpdatedAt = now;
            report.UpdatedBy = HttpContext.User.GetUsername();
            await context.SaveChangesAsync();
            return ToSummary(report);
        }

        [HttpPost("Duplicate", Name = "DuplicateReport")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult<ReportSummary>> Duplicate(string Id)
        {
            var source = await context.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == Id);
            if (source == null) return NotFound("Report not found");

            string baseName = source.Name.Length > 190 ? source.Name[..190] : source.Name;
            string name = $"{baseName} (copy)";
            for (int i = 2; await context.Reports.AnyAsync(r => r.Name.ToLower() == name.ToLower()); i++) name = $"{baseName} (copy {i})";

            var now = DateTime.UtcNow;
            var copy = new Data.Schema.Reports
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Description = source.Description,
                Definition = source.Definition,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = HttpContext.User.GetUsername(),
                UpdatedBy = HttpContext.User.GetUsername()
            };
            context.Reports.Add(copy);
            await context.SaveChangesAsync();
            return ToSummary(copy);
        }

        [HttpDelete("Delete", Name = "DeleteReport")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> Delete(string Id)
        {
            int removed = await context.Reports.Where(r => r.Id == Id).ExecuteDeleteAsync();
            return removed == 0 ? NotFound("Report not found") : Ok();
        }

        // ------------------------------------------------------------------ data

        /// <summary>Choices for a lookup parameter of a saved report (ReportId) or an unsaved definition.</summary>
        [HttpPost("ParameterOptions", Name = "ReportParameterOptions")]
        public async Task<ActionResult<List<ReportParameterOption>>> ParameterOptions([FromBody] ReportParameterOptionsRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var (definition, _, _) = await ResolveReportAsync(request.ReportId, request.Definition, null);
                return await _reports.GetParameterOptionsAsync(definition, request.Parameter, Scope(request.ServerId), request.Parameters, cancellationToken);
            }
            catch (ReportDefinitionException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>Runs one data source, e.g. for the designer's data preview.</summary>
        [HttpPost("Query", Name = "QueryReportData")]
        public async Task<ReportDataResult> Query([FromBody] ReportQueryRequest request, CancellationToken cancellationToken)
        {
            var definition = new ReportDefinition { Parameters = request.ParameterDefinitions ?? [] };
            var parameters = _reports.ResolveParameters(definition, request.Parameters);
            return await _executor.ExecuteAsync(request.DataSource, Scope(request.ServerId), parameters, request.MaxRows, cancellationToken);
        }

        /// <summary>Runs every data source of an (unsaved) definition and returns the render model the UI draws.</summary>
        [HttpPost("Preview", Name = "PreviewReport")]
        public async Task<ActionResult<ReportRenderModel>> Preview([FromBody] ReportPreviewRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return await _reports.BuildAsync(request.Definition, request.Name ?? "Untitled report", null, Scope(request.ServerId), request.Parameters, request.MaxRows, cancellationToken);
            }
            catch (ReportDefinitionException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>Renders a saved or unsaved report in any format. Format=json returns the render model (layout + data).</summary>
        [HttpPost("Render", Name = "RenderReport")]
        public async Task<ActionResult> Render([FromBody] ReportRenderRequest request, bool Download = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var (definition, name, id) = await ResolveReportAsync(request.ReportId, request.Definition, request.Name);
                var scope = Scope(request.ServerId);
                var model = await _reports.BuildAsync(definition, name, id, scope, request.Parameters, null, cancellationToken);
                return FileResult(await _exporter.ExportAsync(model, new ReportExportOptions { Format = request.Format, Page = request.Page, Scale = request.Scale ?? 2 }, scope, cancellationToken), Download);
            }
            catch (ReportDefinitionException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>
        /// GET form of Render for automations and links: /Reports/Export?Id=...&amp;Format=pdf&amp;p.days=7.
        /// Parameters are passed as p.NAME; values that parse as JSON (numbers, true/false, arrays) are used as such.
        /// </summary>
        [HttpGet("Export", Name = "ExportReport")]
        public async Task<ActionResult> Export(string Id, string Format = "pdf", int? Page = null, double Scale = 2, string? ServerId = null, bool Download = false, CancellationToken cancellationToken = default)
        {
            var parameters = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in Request.Query)
            {
                if (!key.StartsWith("p.", StringComparison.OrdinalIgnoreCase) || key.Length <= 2) continue;
                string raw = value.ToString();
                JsonElement element;
                try { element = JsonDocument.Parse(raw).RootElement.Clone(); }
                catch (JsonException) { element = JsonSerializer.SerializeToElement(raw); }
                parameters[key[2..]] = element;
            }
            return await Render(new ReportRenderRequest { ReportId = Id, Format = Format, Page = Page, Scale = Scale, ServerId = ServerId, Parameters = parameters }, Download, cancellationToken);
        }

        private async Task<(ReportDefinition Definition, string Name, string? Id)> ResolveReportAsync(string? reportId, ReportDefinition? definition, string? name)
        {
            if (definition != null) return (definition, string.IsNullOrWhiteSpace(name) ? "Untitled report" : name!, reportId);
            if (string.IsNullOrEmpty(reportId)) throw new ReportDefinitionException("Either ReportId or Definition is required");
            var report = await context.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId)
                ?? throw new KeyNotFoundException("Report not found");
            return (_reports.Parse(report.Definition), report.Name, report.Id);
        }

        private ActionResult FileResult(ReportFile file, bool download)
        {
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (file.ContentType.StartsWith("text/html") || file.ContentType.StartsWith("image/svg"))
            {
                // Rendered reports are static documents: nothing in them may run script or load anything but images
                Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src data: https: http:; style-src 'unsafe-inline'; font-src data:; sandbox";
            }
            var disposition = new System.Net.Mime.ContentDisposition { FileName = file.FileName, Inline = !download };
            Response.Headers.ContentDisposition = disposition.ToString();
            return File(file.Content, file.ContentType);
        }

        // ------------------------------------------------------------------ delivery

        /// <summary>Renders a report and posts it to a webhook now, without saving a schedule.</summary>
        [HttpPost("Deliver", Name = "DeliverReport")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult<ReportDeliveryResult>> Deliver([FromBody] ReportDeliverRequest request, CancellationToken cancellationToken)
        {
            var started = DateTime.UtcNow;
            try
            {
                ReportWebhookSender.ValidateUrl(request.WebhookUrl);
                var (definition, name, id) = await ResolveReportAsync(request.ReportId, request.Definition, request.Name);
                var scope = Scope(request.ServerId);
                var model = await _reports.BuildAsync(definition, name, id, scope, request.Parameters, null, cancellationToken);
                var file = await _exporter.ExportAsync(model, new ReportExportOptions { Format = request.Format }, scope, cancellationToken);
                await _webhooks.SendAsync(new ReportWebhookTarget { Url = request.WebhookUrl, Type = request.WebhookType, Secret = request.WebhookSecret, Message = request.Message }, model, file, cancellationToken);
                return new ReportDeliveryResult(true, null, file.FileName, (long)(DateTime.UtcNow - started).TotalMilliseconds);
            }
            catch (ReportDefinitionException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (HttpRequestException ex)
            {
                return new ReportDeliveryResult(false, ex.Message, null, (long)(DateTime.UtcNow - started).TotalMilliseconds);
            }
        }

        private static ReportScheduleModel ToModel(ReportSchedules s) => new()
        {
            Id = s.Id,
            ReportId = s.ReportId,
            Name = s.Name,
            Enabled = s.Enabled,
            ServerId = s.ServerId,
            IntervalMinutes = s.IntervalMinutes,
            Format = s.Format,
            WebhookUrl = s.WebhookUrl,
            WebhookType = s.WebhookType,
            HasSecret = !string.IsNullOrEmpty(s.WebhookSecret),
            Message = s.Message,
            Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(string.IsNullOrWhiteSpace(s.Parameters) ? "{}" : s.Parameters, ReportJson.Options),
            LastRunAt = s.LastRunAt,
            NextRunAt = s.NextRunAt,
            LastSuccess = s.LastSuccess,
            LastError = s.LastError
        };

        [HttpGet("Schedules", Name = "ListReportSchedules")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<List<ReportScheduleModel>> Schedules(string? ReportId)
        {
            var query = context.ReportSchedules.AsNoTracking();
            if (!string.IsNullOrEmpty(ReportId)) query = query.Where(s => s.ReportId == ReportId);
            return (await query.OrderBy(s => s.Name).ToListAsync()).Select(ToModel).ToList();
        }

        [HttpPost("Schedules/Save", Name = "SaveReportSchedule")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult<ReportScheduleModel>> SaveSchedule([FromBody] ReportScheduleModel model)
        {
            string name = (model.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 200) return BadRequest("A schedule name of 1-200 characters is required");
            if (model.IntervalMinutes < 5 && model.IntervalMinutes != 0) return BadRequest("The interval must be at least 5 minutes (0 = manual only)");
            if (!ReportFormats.All.Contains(model.Format)) return BadRequest($"Format must be one of: {string.Join(", ", ReportFormats.All)}");
            if (!ReportWebhookTypes.All.Contains(model.WebhookType)) return BadRequest($"Webhook type must be one of: {string.Join(", ", ReportWebhookTypes.All)}");
            try
            {
                ReportWebhookSender.ValidateUrl(model.WebhookUrl);
            }
            catch (ReportDefinitionException ex)
            {
                return BadRequest(ex.Message);
            }
            if (!await context.Reports.AnyAsync(r => r.Id == model.ReportId)) return BadRequest("Report not found");

            string serverId = string.IsNullOrEmpty(model.ServerId) ? HttpContext.User.GetServerId() : model.ServerId!;
            if (!await context.Servers.AnyAsync(s => s.Id == serverId)) return BadRequest("Unknown ServerId");

            ReportSchedules? schedule = null;
            if (!string.IsNullOrEmpty(model.Id))
            {
                schedule = await context.ReportSchedules.FirstOrDefaultAsync(s => s.Id == model.Id);
                if (schedule == null) return NotFound("Schedule not found");
            }
            if (schedule == null)
            {
                schedule = new ReportSchedules
                {
                    Id = Guid.NewGuid().ToString(),
                    ReportId = model.ReportId,
                    ServerId = serverId,
                    Name = name,
                    WebhookUrl = model.WebhookUrl
                };
                context.ReportSchedules.Add(schedule);
            }

            bool intervalChanged = schedule.IntervalMinutes != model.IntervalMinutes;
            schedule.ReportId = model.ReportId;
            schedule.ServerId = serverId;
            schedule.Name = name;
            schedule.Enabled = model.Enabled;
            schedule.IntervalMinutes = model.IntervalMinutes;
            schedule.Format = model.Format;
            schedule.WebhookUrl = model.WebhookUrl;
            schedule.WebhookType = model.WebhookType;
            if (model.WebhookSecret != null) schedule.WebhookSecret = model.WebhookSecret.Length == 0 ? null : model.WebhookSecret;
            schedule.Message = string.IsNullOrWhiteSpace(model.Message) ? null : model.Message;
            schedule.Parameters = JsonSerializer.Serialize(model.Parameters ?? [], ReportJson.Options);
            if (schedule.NextRunAt == null || intervalChanged)
                schedule.NextRunAt = model.IntervalMinutes > 0 ? (schedule.LastRunAt ?? DateTime.UtcNow).AddMinutes(model.IntervalMinutes) : null;

            await context.SaveChangesAsync();
            return ToModel(schedule);
        }

        [HttpDelete("Schedules/Delete", Name = "DeleteReportSchedule")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult> DeleteSchedule(string Id)
        {
            int removed = await context.ReportSchedules.Where(s => s.Id == Id).ExecuteDeleteAsync();
            return removed == 0 ? NotFound("Schedule not found") : Ok();
        }

        /// <summary>Runs a schedule now (also how webhooks are tested) and returns the outcome.</summary>
        [HttpPost("Schedules/Run", Name = "RunReportSchedule")]
        [PermissionAuthorize(PermissionTypes.Administrator)]
        public async Task<ActionResult<ReportDeliveryResult>> RunSchedule(string Id, CancellationToken cancellationToken)
        {
            if (!await context.ReportSchedules.AnyAsync(s => s.Id == Id, cancellationToken)) return NotFound("Schedule not found");
            return await _scheduleRunner.RunAsync(Id, cancellationToken);
        }
    }
}
