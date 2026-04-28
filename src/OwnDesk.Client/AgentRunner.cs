using System.Text;
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

    public Task StartAsync(ClientOrganization organization, ClientSettings settings)
    {
        organization = organization.Normalize(0);
        settings = settings.Normalize();

        if (string.IsNullOrWhiteSpace(organization.Server))
        {
            throw new InvalidOperationException("Organization server URL is required.");
        }

        if (string.IsNullOrWhiteSpace(organization.Token))
        {
            throw new InvalidOperationException("Organization token is required.");
        }

        if (string.IsNullOrWhiteSpace(organization.Account))
        {
            throw new InvalidOperationException("Member account is required.");
        }

        if (string.IsNullOrWhiteSpace(organization.Password))
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
            _runTask = RunAgentAsync(organization, settings, _shutdown.Token);
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

    private async Task RunAgentAsync(
        ClientOrganization organization,
        ClientSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = AgentOptions.Parse(BuildAgentArgs(organization, settings));
            var qualityController = new StreamQualityController(options);
            var captureBackendPlan = ScreenCaptureBackendPlan.Create(options);

            using var consoleLog = new AgentConsoleLogScope();
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

    private static string[] BuildAgentArgs(ClientOrganization organization, ClientSettings settings)
    {
        return
        [
            "--server",
            organization.Server,
            "--account",
            organization.Account,
            "--token",
            organization.Token,
            "--password",
            organization.Password,
            "--device-id",
            settings.DeviceId,
            "--device-name",
            settings.DeviceName,
            "--webrtc",
            settings.EnableWebRtc ? "true" : "false"
        ];
    }

    private sealed class AgentConsoleLogScope : IDisposable
    {
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;
        private readonly AgentConsoleLogWriter _writer;

        public AgentConsoleLogScope()
        {
            _originalOut = Console.Out;
            _originalError = Console.Error;
            _writer = new AgentConsoleLogWriter(_originalOut);
            Console.SetOut(_writer);
            Console.SetError(_writer);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            _writer.Dispose();
        }
    }

    private sealed class AgentConsoleLogWriter(TextWriter forward) : TextWriter
    {
        private readonly object _gate = new();
        private readonly StringBuilder _buffer = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_gate)
            {
                if (value == '\r')
                {
                    return;
                }

                if (value == '\n')
                {
                    FlushLine();
                    return;
                }

                _buffer.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value is null)
            {
                return;
            }

            foreach (var item in value)
            {
                Write(item);
            }
        }

        public override void WriteLine()
        {
            lock (_gate)
            {
                FlushLine();
            }
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            WriteLine();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_gate)
                {
                    FlushLine();
                }
            }

            base.Dispose(disposing);
        }

        private void FlushLine()
        {
            if (_buffer.Length == 0)
            {
                return;
            }

            var line = _buffer.ToString();
            _buffer.Clear();
            ClientLog.Write($"Agent: {line}");

            try
            {
                forward.WriteLine(line);
            }
            catch
            {
                // Console forwarding is best effort only.
            }
        }
    }
}
