using OwnDesk.Agent;

namespace OwnDesk.Client;

internal sealed class AgentRunner : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _shutdown;
    private Task? _runTask;

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<bool>? RunningChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _runTask is { IsCompleted: false };
            }
        }
    }

    public Task StartAsync(ClientSettings settings)
    {
        settings = settings.Normalize();

        if (string.IsNullOrWhiteSpace(settings.Token))
        {
            throw new InvalidOperationException("Organization token is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.Account))
        {
            throw new InvalidOperationException("Member account is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.Password))
        {
            throw new InvalidOperationException("Password is required.");
        }

        lock (_gate)
        {
            if (_runTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _shutdown = new CancellationTokenSource();
            _runTask = RunAgentAsync(settings, _shutdown.Token);
        }

        StatusChanged?.Invoke(this, "Agent starting");
        RunningChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? shutdown;
        Task? runTask;

        lock (_gate)
        {
            shutdown = _shutdown;
            runTask = _runTask;
            _shutdown = null;
            _runTask = null;
        }

        if (shutdown is null)
        {
            return;
        }

        StatusChanged?.Invoke(this, "Agent stopping");
        await shutdown.CancelAsync();

        if (runTask is not null)
        {
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        shutdown.Dispose();
        StatusChanged?.Invoke(this, "Agent stopped");
        RunningChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        _shutdown?.Cancel();
        _shutdown?.Dispose();
    }

    private async Task RunAgentAsync(ClientSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var options = AgentOptions.Parse(BuildAgentArgs(settings));
            var qualityController = new StreamQualityController(options);
            var captureBackendPlan = ScreenCaptureBackendPlan.Create(options);

            using var screenCapture = new ScreenCaptureService(
                ScreenCaptureBackendFactory.Create(captureBackendPlan),
                captureBackendPlan);

            var agent = new RemoteAgent(options, screenCapture, new InputController(), qualityController);
            StatusChanged?.Invoke(this, $"Agent running as {options.DeviceName}");
            await agent.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Agent stopped: {ex.Message}");
            RunningChanged?.Invoke(this, false);
        }
    }

    private static string[] BuildAgentArgs(ClientSettings settings)
    {
        return
        [
            "--server",
            settings.Server,
            "--account",
            settings.Account,
            "--token",
            settings.Token,
            "--password",
            settings.Password,
            "--device-id",
            settings.DeviceId,
            "--device-name",
            settings.DeviceName,
            "--webrtc",
            settings.EnableWebRtc ? "true" : "false"
        ];
    }
}
