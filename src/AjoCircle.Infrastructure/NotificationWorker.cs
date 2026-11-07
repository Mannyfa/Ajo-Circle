using AjoCircle.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AjoCircle.Infrastructure;

/// <summary>
/// Durable background loop (brief §10.5): every few seconds it emails pending notifications, and about once a minute it
/// creates date-based reminders. All state lives in the database, so a restart loses nothing and nothing depends on a
/// browser tab or in-memory timer.
/// </summary>
public sealed class NotificationWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<NotificationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Notifications:PollSeconds", 5)));
        var reminderEvery = TimeSpan.FromSeconds(Math.Max(10, config.GetValue("Notifications:ReminderSeconds", 60)));
        var lastReminders = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - lastReminders >= reminderEvery)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var created = await scope.ServiceProvider.GetRequiredService<ReminderScheduler>().RunAsync(stoppingToken);
                    if (created > 0) log.LogInformation("Created {Count} reminder notifications.", created);
                    var purged = await scope.ServiceProvider.GetRequiredService<IdentityRetention>().PurgeAsync(stoppingToken);
                    if (purged > 0) log.LogInformation("Purged identity data for {Count} decided checks past retention.", purged);
                    lastReminders = DateTimeOffset.UtcNow;
                }

                int sent;
                do
                {
                    await using var scope = scopes.CreateAsyncScope();
                    sent = await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(20, stoppingToken);
                } while (sent == 20 && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Notification worker cycle failed; retrying next cycle.");
            }

            try { await Task.Delay(poll, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
