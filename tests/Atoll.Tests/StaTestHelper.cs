using System.Windows.Threading;
using Atoll.Core.Activities;
using Atoll.Core.State;
using Atoll.Modules;
using Atoll.Services;
using Atoll.Settings;

namespace Atoll.Tests;

internal static class StaTestHelper
{
    public static void Run(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw new AggregateException(captured);
        }
    }

    public static ModuleContext CreateTestModuleContext(string tempSettingsPath)
    {
        var settings = new SettingsService(tempSettingsPath);
        var activityMgr = new ActivityManager();
        var stateMachine = new IslandStateMachine(activityMgr);

        return new ModuleContext
        {
            ActivityManager = activityMgr,
            StateMachine = stateMachine,
            SettingsService = settings,
            AudioService = new AudioEndpointService(),
            BrightnessService = new BrightnessService(),
            ImageCache = new ImageCacheService(),
            FocusAssist = new FocusAssistService(),
            UiDispatcher = Dispatcher.CurrentDispatcher
        };
    }
}

