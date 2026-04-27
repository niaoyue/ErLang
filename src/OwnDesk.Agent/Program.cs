using OwnDesk.Agent;
using System.Windows.Forms;

Application.SetHighDpiMode(HighDpiMode.SystemAware);

var options = AgentOptions.Parse(args);
using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine("OwnDesk Agent");
Console.WriteLine($"Server: {options.Server}");
Console.WriteLine($"Account: {options.Account}");
Console.WriteLine($"Device: {options.DeviceName} ({options.DeviceId})");
var qualityController = new StreamQualityController(options);
var initialQuality = qualityController.Current;
Console.WriteLine($"Quality: {initialQuality.Profile} {initialQuality.FramesPerSecond} FPS, JPEG {initialQuality.JpegQuality}, max {initialQuality.MaxWidth}x{initialQuality.MaxHeight}, WebRTC {initialQuality.WebRtcBitrateKbps} kbps");
Console.WriteLine($"WebRTC: {(options.EnableWebRtc ? $"enabled ({options.WebRtcCodec}, {options.WebRtcBitrateKbps} kbps)" : "disabled")}");
var captureBackendPlan = ScreenCaptureBackendPlan.Create(options);
Console.WriteLine($"Capture backend: requested={captureBackendPlan.RequestedBackend}, selected={captureBackendPlan.SelectedBackend} ({captureBackendPlan.CaptureBackendId})");
foreach (var note in captureBackendPlan.Notes)
{
    Console.WriteLine($"Capture backend note: {note}");
}
Console.WriteLine("Press Ctrl+C to stop.");

using var screenCapture = new ScreenCaptureService(
    ScreenCaptureBackendFactory.Create(captureBackendPlan),
    captureBackendPlan);
var agent = new RemoteAgent(options, screenCapture, new InputController(), qualityController);
await agent.RunAsync(shutdown.Token);
