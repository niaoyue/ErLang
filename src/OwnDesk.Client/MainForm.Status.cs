namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private void SetAgentRunning(bool running)
    {
        _agentStatus.Text = running ? "本机上线：运行中" : "本机上线：已停止";
        _startAgentButton.Enabled = !running;
        _stopAgentButton.Enabled = running;
    }

    private void PostStatus(string status)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => PostStatus(status));
            return;
        }

        _agentStatus.Text = status;
        AppendLog(status);
    }

    private void PostAgentRunning(bool running)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => PostAgentRunning(running)));
            return;
        }

        SetAgentRunning(running);
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
            MessageBox.Show(this, ex.Message, "OwnDesk Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AppendLog(string message)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss} {message}{Environment.NewLine}";
        _logOutput.AppendText(line);
        ClientLog.Write(message);
    }
}
