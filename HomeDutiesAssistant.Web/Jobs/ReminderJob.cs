using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Models;
using HomeDutiesAssistant.Web.Services;
using Microsoft.Extensions.Options;
using Quartz;
using Task = System.Threading.Tasks.Task;

namespace HomeDutiesAssistant.Web.Jobs;

// Daily Web Push reminders: duties due today or in Push:LeadDays days go to
// every subscribed browser in their home. Stateless and date-based — run at
// 08:00, 14:00 and 20:00 by cron (no StartNow), so a duty is re-sent on each
// run of its reminder days.
[DisallowConcurrentExecution]
public sealed class ReminderJob(
    PushRepository repository,
    PushSender sender,
    IOptions<PushOptions> options,
    ILogger<ReminderJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var settings = options.Value;

        // The container clock is UTC; "today" is the household's local date.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var lead = today.AddDays(settings.LeadDays);

        var reminders = await repository.ListDueAsync(today, lead, ct);
        if (reminders.Count == 0)
        {
            logger.LogInformation("No duties due for {Today} or {Lead}.", today, lead);
            return;
        }

        var sent = 0;
        var failed = 0;
        var skip = new HashSet<string>(); // endpoints that failed this run (incl. deleted 404/410) — not retried per duty
        foreach (var home in reminders.GroupBy(reminder => reminder.HomeId))
        {
            try
            {
                var subscriptions = await repository.ListByHomeAsync(home.Key, ct);
                foreach (var reminder in home)
                {
                    var payload = ToPayload(reminder, today, settings.LeadDays);
                    foreach (var subscription in subscriptions)
                    {
                        if (skip.Contains(subscription.Endpoint)) continue;
                        if (await sender.SendAsync(subscription, payload, ct))
                            sent++;
                        else
                        {
                            failed++;
                            skip.Add(subscription.Endpoint);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(exception, "Reminders for home {HomeId} failed; continuing with the next home.", home.Key);
            }
        }

        logger.LogInformation("Reminders: {Duties} duties, {Sent} pushes sent, {Failed} failed.",
            reminders.Count, sent, failed);
    }

    // English only — the job has no request culture (see CLAUDE.md).
    private static PushPayload ToPayload(Reminder reminder, DateOnly today, int leadDays)
    {
        var body = reminder.DueDate == today ? "Due today"
            : leadDays == 1 ? "Due tomorrow"
            : $"Due in {leadDays} days";
        // Same tag for both reminders of a duty: the on-day one replaces the earlier one.
        // Opens the chat ("/"), the one page every role can open (/duties needs Manage).
        return new PushPayload(reminder.Title, body, "/", $"duty-{reminder.DutyId}");
    }
}
