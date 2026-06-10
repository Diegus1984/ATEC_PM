using System.Windows;
using System.Windows.Threading;

namespace ATEC.PM.Client.Services;

/// <summary>
/// Polling adattivo per notifiche: rallenta se inattivo, accelera su nuove notifiche,
/// pausa quando la finestra perde il focus.
/// </summary>
public sealed class NotificationPollingService
{
    private static readonly TimeSpan IntervalBase = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IntervalMedium = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan IntervalSlow = TimeSpan.FromSeconds(120);

    private const int ThresholdMedium = 5;
    private const int ThresholdSlow = 10;

    private readonly DispatcherTimer _timer;
    private readonly Func<Task<int>> _pollAction;

    private int _emptyConsecutiveCycles;
    private int _lastKnownCount;
    private bool _windowFocused = true;

    public event Action<int>? BadgeUpdated;

    public NotificationPollingService(Func<Task<int>> pollAction)
    {
        _pollAction = pollAction;
        _timer = new DispatcherTimer { Interval = IntervalBase };
        _timer.Tick += async (_, _) => await ExecutePoll();
    }

    public void Start()
    {
        _ = ExecutePoll();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void OnWindowActivated()
    {
        if (_windowFocused) return;
        _windowFocused = true;
        ResetToBase();
        _ = ExecutePoll();
    }

    public void OnWindowDeactivated()
    {
        _windowFocused = false;
        _timer.Stop();
    }

    private void ResetToBase()
    {
        _emptyConsecutiveCycles = 0;
        _timer.Interval = IntervalBase;
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    private async Task ExecutePoll()
    {
        try
        {
            int unreadCount = await _pollAction();

            if (unreadCount > _lastKnownCount)
            {
                ResetToBase();
            }
            else if (unreadCount == 0 || unreadCount <= _lastKnownCount)
            {
                _emptyConsecutiveCycles++;
                AdjustInterval();
            }

            _lastKnownCount = unreadCount;
            BadgeUpdated?.Invoke(unreadCount);
        }
        catch
        {
            // Errore di rete — non interrompe il polling
        }
    }

    private void AdjustInterval()
    {
        TimeSpan newInterval = _emptyConsecutiveCycles switch
        {
            >= ThresholdSlow => IntervalSlow,
            >= ThresholdMedium => IntervalMedium,
            _ => IntervalBase
        };

        if (_timer.Interval != newInterval)
            _timer.Interval = newInterval;
    }
}
