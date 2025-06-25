using MauldaschTracker.PretixApi;

namespace MauldaschTracker;

public class PretixSyncService : IHostedService, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<PretixSyncService> _logger;
    private readonly PretixApiClient _pretixApiClient;
    private readonly MauldaschTrackerService _mauldaschTrackerService;
    private Timer? _timer = null;

    public PretixSyncService(ILogger<PretixSyncService> logger, PretixApiClient pretixApiClient, MauldaschTrackerService mauldaschTrackerService)
    {
        _logger = logger;
        _pretixApiClient = pretixApiClient;
        _mauldaschTrackerService = mauldaschTrackerService;
    }

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        await DoWork();

        _timer = new Timer(x => DoWork(), null, TimeSpan.Zero,
            TimeSpan.FromMinutes(5));
    }

    private async Task DoWork()
    {
        await _semaphore.WaitAsync();
        try
        {
            _logger.LogInformation("Syncing Items with pretix...");
            var luggageItems = await _pretixApiClient.GetLuggageItems();
            await _mauldaschTrackerService.UpdateOrAddItems(luggageItems);
            _logger.LogInformation("Done.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while syncing with pretix");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
