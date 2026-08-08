using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PlayStationSaveManager.Services;

namespace PlayStationSaveManager;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException +=
            App_DispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException +=
            CurrentDomain_UnhandledException;

        TaskScheduler.UnobservedTaskException +=
            TaskScheduler_UnobservedTaskException;
    }

    private static void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.WriteCrash(
            e.Exception,
            "WPF Dispatcher");

        // Do not set e.Handled. The logger records the real crash
        // without suppressing an otherwise-fatal exception.
    }

    private static void CurrentDomain_UnhandledException(
        object? sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppLog.WriteCrash(
                exception,
                "AppDomain");
        }
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        AppLog.WriteCrash(
            e.Exception,
            "Unobserved Task");
    }
}
