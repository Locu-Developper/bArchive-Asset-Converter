using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace BlueArchive_Assets_Converter;

public static class MauiProgram
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
    public static MauiApp CreateMauiApp()
    {
        AllocConsole();
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}