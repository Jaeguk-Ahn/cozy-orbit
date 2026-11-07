using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CozyOrbit.App.Infra;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Views;

/// <summary>
/// 설정 탭의 업데이트 칸: 지금 버전 · [지금 확인] · 상태 줄 · [설치하고 다시 시작](설치할 수 있을 때) ·
/// [릴리스 페이지 열기](새 버전은 있는데 여기서 설치할 수 없을 때) · 자동 확인 체크.
/// </summary>
public sealed class UpdatePanel : UserControl
{
    private readonly TextBlock _version = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 18, Classes = { "dim" } };
    private readonly Button _check = new() { Content = UpdateTexts.CheckNow };
    private readonly Button _install = new() { Content = UpdateTexts.InstallAndRestart, Classes = { "accent" } };
    private readonly Button _page = new() { Content = UpdateTexts.OpenReleasePage };

    public UpdatePanel(GameHost host)
    {
        var auto = new CheckBox { Content = UpdateTexts.AutoCheck, IsChecked = host.State.Settings.CheckUpdates };
        auto.IsCheckedChanged += (_, _) => UpdateService.SetAutoCheck(auto.IsChecked == true);

        _check.Click += async (_, _) => await UpdateService.CheckNowAsync();
        _install.Click += async (_, _) => await UpdateService.InstallAsync();
        _page.Click += async (_, _) => await UpdateService.OpenReleasePageAsync(TopLevel.GetTopLevel(this));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _check, _install, _page } };
        Content = new StackPanel { Spacing = 6, Children = { _version, buttons, _status, auto } };
        Refresh();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateService.Changed += Refresh;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        UpdateService.Changed -= Refresh;
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        var checker = UpdateService.Checker;
        var installer = UpdateService.Installer;
        _version.Text = UpdateTexts.CurrentVersion(checker);
        _status.Text = UpdateTexts.Status(checker, installer);
        _status.IsVisible = _status.Text.Length > 0;
        _check.IsEnabled = checker.LastStatus != UpdateChecker.Status.Checking && !installer.IsBusy;
        bool canInstall = UpdateService.CanInstall;
        _install.IsVisible = canInstall || installer.IsBusy;
        _install.IsEnabled = canInstall;
        _page.IsVisible = checker.Available != null && !canInstall && !installer.IsBusy;
    }
}
