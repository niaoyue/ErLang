namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private ClientOrganization CurrentOrganization => _settings.SelectedOrganization();

    private void BindSettings()
    {
        _isBinding = true;
        try
        {
            BindOrganizationList();
            BindSelectedOrganization();
            BindDeviceSettings();
            SetAuthMode(registerMode: false);
        }
        finally
        {
            _isBinding = false;
        }
    }

    private void BindOrganizationList()
    {
        var selectedId = _settings.SelectedOrganizationId;
        _organizationSelect.BeginUpdate();
        _organizationSelect.Items.Clear();
        foreach (var organization in _settings.Organizations)
        {
            _organizationSelect.Items.Add(new OrganizationItem(organization.Id, organization.DisplayName));
        }

        for (var index = 0; index < _organizationSelect.Items.Count; index++)
        {
            if (_organizationSelect.Items[index] is OrganizationItem item && item.Id == selectedId)
            {
                _organizationSelect.SelectedIndex = index;
                break;
            }
        }

        if (_organizationSelect.SelectedIndex < 0 && _organizationSelect.Items.Count > 0)
        {
            _organizationSelect.SelectedIndex = 0;
        }

        _organizationSelect.EndUpdate();
    }

    private void BindSelectedOrganization()
    {
        var organization = CurrentOrganization;
        _organizationNameInput.Text = organization.Name;
        _serverInput.Text = organization.Server;
        _tokenInput.Text = organization.Token;
        _accountInput.Text = organization.Account;
        _passwordInput.Text = organization.Password;
        _confirmPasswordInput.Clear();
        BindAuthState(organization);
    }

    private void BindDeviceSettings()
    {
        _deviceIdInput.Text = _settings.DeviceId;
        _deviceNameInput.Text = _settings.DeviceName;
        _startOnLaunchInput.Checked = _settings.StartAgentOnLaunch;
        _webRtcInput.Checked = _settings.EnableWebRtc;
    }

    private ClientOrganization SaveCurrentOrganization(bool rebind)
    {
        var organization = CurrentOrganization with
        {
            Name = _organizationNameInput.Text,
            Server = _serverInput.Text,
            Token = _tokenInput.Text
        };
        _settings = (_settings with
        {
            DeviceId = _deviceIdInput.Text,
            DeviceName = _deviceNameInput.Text,
            StartAgentOnLaunch = _startOnLaunchInput.Checked,
            EnableWebRtc = _webRtcInput.Checked
        }).WithSelectedOrganization(organization).Normalize();
        _settings.Save();

        if (rebind)
        {
            _isBinding = true;
            try
            {
                BindOrganizationList();
                BindSelectedOrganization();
                BindDeviceSettings();
            }
            finally
            {
                _isBinding = false;
            }

            AppendLog("Organization saved.");
        }

        return CurrentOrganization;
    }

    private async Task AddOrganizationAsync()
    {
        await StopAgentAsync();
        var organization = ClientOrganization.Create($"组织 {_settings.Organizations.Count + 1}");
        _settings = _settings.AddOrganization(organization).Normalize();
        _settings.Save();
        BindSettings();
        await NavigateViewerAsync(autoLogin: false);
        AppendLog("Organization added.");
    }

    private async Task DeleteOrganizationAsync()
    {
        await StopAgentAsync();
        _settings = _settings.RemoveOrganization(CurrentOrganization.Id).Normalize();
        _settings.Save();
        BindSettings();
        await NavigateViewerAsync(autoLogin: false);
        AppendLog("Organization deleted.");
    }

    private async Task SwitchOrganizationAsync()
    {
        if (_isBinding || _organizationSelect.SelectedItem is not OrganizationItem item)
        {
            return;
        }

        await StopAgentAsync();
        _settings = _settings.WithSelectedOrganizationId(item.Id).Normalize();
        _settings.Save();
        BindSettings();
        await TryAutoLoginCurrentOrganizationAsync();
        await StartAgentIfConfiguredAsync();
        await NavigateViewerAsync(CurrentOrganization.SignedIn);
    }

    private sealed record OrganizationItem(string Id, string Text)
    {
        public override string ToString()
        {
            return Text;
        }
    }
}
