using OwnDesk.Shared.Messages;

namespace OwnDesk.Client;

internal sealed partial class MainForm
{
    private void SetAuthMode(bool registerMode)
    {
        _registerMode = registerMode;
        _confirmPasswordLabel.Visible = registerMode;
        if (_confirmPasswordHost is not null)
        {
            _confirmPasswordHost.Visible = registerMode;
        }

        _authSubmitButton.Text = registerMode ? "注册并登录" : "登录";
        ApplyButtonStyle(_loginModeButton, active: !registerMode);
        ApplyButtonStyle(_registerModeButton, active: registerMode);
    }

    private void BindAuthState(ClientOrganization organization)
    {
        var loggedIn = organization.SignedIn && organization.HasSavedCredentials;
        _authFieldsPanel.Visible = !loggedIn;
        _memberPanel.Visible = loggedIn;
        _identityStatus.Text = loggedIn
            ? $"组织：{organization.DisplayName}{Environment.NewLine}成员：{organization.Account}"
            : "未登录";
    }

    private async Task AuthenticateAsync()
    {
        var account = _accountInput.Text.Trim();
        var password = _passwordInput.Text;
        var confirmPassword = _confirmPasswordInput.Text;
        var organization = SaveCurrentOrganization(rebind: false);
        ValidateAuthInputs(organization, account, password, confirmPassword, _registerMode);

        _authSubmitButton.Enabled = false;
        try
        {
            var session = _registerMode
                ? await _authClient.RegisterAsync(organization, account, password, CancellationToken.None)
                : await _authClient.LoginAsync(organization, account, password, CancellationToken.None);

            ApplyAuthenticatedSession(organization, session, password);
            BindSettings();
            await StartAgentIfConfiguredAsync(force: true);
            await NavigateViewerAsync(autoLogin: true);
            AppendLog(_registerMode ? "Member registered and logged in." : "Member logged in.");
        }
        finally
        {
            _authSubmitButton.Enabled = true;
        }
    }

    private async Task TryAutoLoginCurrentOrganizationAsync()
    {
        var organization = CurrentOrganization;
        if (!organization.SignedIn || !organization.HasConnection || !organization.HasSavedCredentials)
        {
            BindAuthState(organization);
            return;
        }

        try
        {
            var session = await _authClient.LoginAsync(
                organization,
                organization.Account,
                organization.Password,
                CancellationToken.None);
            ApplyAuthenticatedSession(organization, session, organization.Password);
            BindSettings();
            AppendLog($"Auto logged in as {session.Username}.");
        }
        catch (Exception ex)
        {
            organization = organization with
            {
                SignedIn = false,
                SessionToken = string.Empty,
                SessionExpiresAtUtc = null
            };
            _settings = _settings.WithSelectedOrganization(organization).Normalize();
            _settings.Save();
            BindSettings();
            AppendLog($"Auto login failed: {ex.Message}");
        }
    }

    private async Task LogoutAsync()
    {
        await StopAgentAsync();
        var organization = CurrentOrganization with
        {
            SignedIn = false,
            SessionToken = string.Empty,
            SessionExpiresAtUtc = null
        };
        _settings = _settings.WithSelectedOrganization(organization).Normalize();
        _settings.Save();
        BindSettings();
        await NavigateViewerAsync(autoLogin: false);
        AppendLog("Member logged out.");
    }

    private void ApplyAuthenticatedSession(
        ClientOrganization organization,
        AuthSessionDto session,
        string password)
    {
        organization = organization with
        {
            Account = session.Username,
            Password = password,
            SignedIn = true,
            SessionToken = session.SessionToken,
            SessionExpiresAtUtc = session.ExpiresAtUtc
        };
        _settings = _settings.WithSelectedOrganization(organization).Normalize();
        _settings.Save();
    }

    private static void ValidateAuthInputs(
        ClientOrganization organization,
        string account,
        string password,
        string confirmPassword,
        bool requireConfirmPassword)
    {
        if (!organization.HasConnection)
        {
            throw new InvalidOperationException("请先填写并保存组织的服务器 URL 和组织 Token。");
        }

        if (string.IsNullOrWhiteSpace(account))
        {
            throw new InvalidOperationException("请填写成员账号。");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("请填写成员密码。");
        }

        if (!requireConfirmPassword)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(confirmPassword))
        {
            throw new InvalidOperationException("请再次输入密码。");
        }

        if (password != confirmPassword)
        {
            throw new InvalidOperationException("两次输入的密码不一致。");
        }
    }
}
