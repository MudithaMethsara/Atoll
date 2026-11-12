using System.Windows.Threading;
using Atoll.Core.Activities;
using Atoll.Core.State;
using Atoll.Services;
using Atoll.Settings;

namespace Atoll.Modules;

public sealed class ModuleContext
{
    public required ActivityManager ActivityManager { get; init; }
    public required IslandStateMachine StateMachine { get; init; }
    public required SettingsService SettingsService { get; init; }
    public required AudioEndpointService AudioService { get; init; }
    public required BrightnessService BrightnessService { get; init; }
    public required ImageCacheService ImageCache { get; init; }
    public required FocusAssistService FocusAssist { get; init; }
    public required Dispatcher UiDispatcher { get; init; }
    public Func<IntPtr> GetWindowHandle { get; init; } = () => IntPtr.Zero;
}

public interface IIslandModule : IDisposable
{
    string ModuleId { get; }
    string DisplayName { get; }
    bool IsEnabled { get; }
    bool IsActive { get; }

    Task InitializeAsync(ModuleContext context);
    void SetEnabled(bool enabled);
}

