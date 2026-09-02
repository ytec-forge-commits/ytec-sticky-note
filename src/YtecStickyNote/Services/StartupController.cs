using Windows.ApplicationModel;

namespace YtecStickyNote.Services;

public interface IStartupController
{
    Task<StartupRegistrationStatus> GetStatusAsync();

    Task<bool> IsEnabledAsync();

    Task SetEnabledAsync(bool enabled);
}

public sealed class PortableStartupController(StartupService service) : IStartupController
{
    public Task<StartupRegistrationStatus> GetStatusAsync() => Task.FromResult(service.GetRegistrationStatus());

    public async Task<bool> IsEnabledAsync() =>
        await GetStatusAsync() == StartupRegistrationStatus.Enabled;

    public Task SetEnabledAsync(bool enabled)
    {
        service.SetEnabled(enabled);
        return Task.CompletedTask;
    }
}

public sealed class PackagedStartupController : IStartupController
{
    public const string TaskId = "KeisaiStartup";

    public async Task<StartupRegistrationStatus> GetStatusAsync() =>
        await IsEnabledAsync()
            ? StartupRegistrationStatus.Enabled
            : StartupRegistrationStatus.Disabled;

    public async Task<bool> IsEnabledAsync()
    {
        var task = await StartupTask.GetAsync(TaskId);
        return IsEnabled(task.State);
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (enabled)
        {
            var state = await task.RequestEnableAsync();
            if (!IsEnabled(state))
            {
                throw new InvalidOperationException(
                    state == StartupTaskState.DisabledByPolicy
                        ? "組織のWindowsポリシーにより自動起動を有効にできません。"
                        : "Windowsのスタートアップ設定で無効にされています。設定の［アプリ］→［スタートアップ］から罫彩を有効にしてください。");
            }

            return;
        }

        task.Disable();
        if (IsEnabled(task.State))
        {
            throw new InvalidOperationException("Windowsポリシーにより自動起動を無効にできません。");
        }
    }

    private static bool IsEnabled(StartupTaskState state) =>
        state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
}
