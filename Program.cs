using Avalonia;

namespace TomatoMix;

internal sealed class Program
{
    public const string SingleInstanceMutexName = @"Local\TomatoMix_SingleInstance_Mutex";
    public const string ActivateInstanceEventName = @"Local\TomatoMix_ActivateInstance_Event";

    private static Mutex? _instanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using var activateEvent = EventWaitHandle.OpenExisting(ActivateInstanceEventName);
                activateEvent.Set();
            }
            catch
            {
            }
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try
            {
                _instanceMutex.ReleaseMutex();
                _instanceMutex.Dispose();
            }
            catch
            {
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
