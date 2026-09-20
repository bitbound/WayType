using WayType.Libraries.Core.Platform;
using WayType.Views;

namespace WayType.ViewModels;

public sealed partial class AboutViewModel : ViewModelBase<AboutView>
{
    private readonly IAppInfo _appInfo;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    public AboutViewModel(IAppInfo appInfo)
    {
        _appInfo = appInfo;
    }

    public string RepositoryUrl => _appInfo.RepositoryUrl;

    public string LicenseUrl => $"{_appInfo.RepositoryUrl}/blob/main/LICENSE";

    public string IssuesUrl => $"{_appInfo.RepositoryUrl}/issues";

    protected override Task OnInitializeAsync()
    {
        AppVersion = _appInfo.Version.ToString();

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not open {url}: {ex}");
        }
    }
}
