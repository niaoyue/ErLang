namespace OwnDesk.Agent;

internal sealed class WebRtcMediaState
{
    private int _activeVideoSessions;

    public bool HasActiveVideo => Volatile.Read(ref _activeVideoSessions) > 0;

    public void AddVideoSession()
    {
        Interlocked.Increment(ref _activeVideoSessions);
    }

    public void RemoveVideoSession()
    {
        int current;
        do
        {
            current = Volatile.Read(ref _activeVideoSessions);
            if (current <= 0)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _activeVideoSessions, current - 1, current) != current);
    }
}
