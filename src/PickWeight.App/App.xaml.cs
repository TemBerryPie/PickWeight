using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Services;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PickWeight_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static string IconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PickWeight");
        var services = new ServiceCollection();
        services.AddSingleton<IStateStore>(new JsonStateStore(Path.Combine(dataDirectory, "state.json")));
        services.AddSingleton<IImplicitWeightProvider>(new JsonImplicitWeightProvider(
            Path.Combine(AppContext.BaseDirectory, "implicit_config.json")));
        services.AddSingleton<IDrawService, DrawService>();
        services.AddSingleton<IMemberImportExportService, MemberImportExportService>();
        services.AddSingleton<IMemberSpreadsheetImportService, ExcelMemberImportService>();
        services.AddSingleton<IGroupImportExportService, GroupImportExportService>();
        services.AddSingleton<IHistoryExportService, HistoryExportService>();
        services.AddSingleton<AppDataService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<AnimationService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<StartupService>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<QuickDrawWindowService>();
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<TrayMenuWindowService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AppLogService>();
        services.AddSingleton<ObsOverlayService>();
        services.AddSingleton<LocalApiService>();
        services.AddSingleton<IClassIslandNotificationBridge, ClassIslandNotificationBridge>();
        services.AddSingleton<IDrawExecutionService, DrawExecutionService>();
        services.AddSingleton<FileDialogService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<FirstRunWizardService>();
        services.AddTransient<DrawPageViewModel>();
        services.AddTransient<HistoryPageViewModel>();
        services.AddTransient<MembersPageViewModel>();
        services.AddTransient<GroupsPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        Services = services.BuildServiceProvider();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        await Services.GetRequiredService<AppDataService>().InitializeAsync();
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
        Services.GetRequiredService<TrayIconService>().Initialize();
        Services.GetRequiredService<NotificationService>().Register();
        Services.GetRequiredService<StartupService>().Apply();
        await Services.GetRequiredService<AppLogService>().InfoAsync("Application started.");
        Services.GetRequiredService<LocalApiService>().Start();
        Services.GetRequiredService<ThemeService>().Apply();
        var quickDraw = Services.GetRequiredService<QuickDrawWindowService>();
        quickDraw.QuickDrawRequested = PrepareQuickDrawAsync;
        quickDraw.Apply();
        Services.GetRequiredService<GlobalHotkeyService>().Start();
        if (Services.GetRequiredService<AppDataService>().IsFirstRunRequired)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                // XamlRoot is assigned only after the first layout pass. Never pass a
                // null root to ContentDialog.ShowAsync; WinUI can fail-fast in that case.
                if (Window.Content is not FrameworkElement root)
                {
                    return;
                }

                for (var attempt = 0; attempt < 10 && root.XamlRoot is null; attempt++)
                {
                    await Task.Delay(50);
                }

                if (root.XamlRoot is not null)
                {
                    await Services.GetRequiredService<FirstRunWizardService>().ShowAsync(root.XamlRoot);
                }
            });
        }
    }

    private static async Task<QuickDrawPresentation?> PrepareQuickDrawAsync(CancellationToken cancellationToken)
    {
        var execution = GetService<IDrawExecutionService>();
        var settings = GetService<ISettingsService>();
        var prepared = await execution.PrepareAsync(
            new DrawExecutionRequest(
                TargetCount: 1,
                Filters: new PickWeight.Core.Models.DrawFilterOptions(
                    settings.Current.DrawIncludeGroupName,
                    settings.Current.DrawExcludeGroupName,
                    Math.Max(0, settings.Current.DrawExcludeRecentDrawCount),
                    settings.Current.DrawOnlyNeverDrawn),
                Mode: settings.Current.DrawMode,
                AutoCommitSelection: true),
            cancellationToken);
        var selected = prepared.SelectedMembers.FirstOrDefault();
        if (selected is null)
        {
            return null;
        }

        return new QuickDrawPresentation(
            prepared.CandidateNames,
            selected.Name,
            async token =>
            {
                await execution.CommitAsync(prepared, confirmSelection: true, cancellationToken: token);
            },
            token => execution.DiscardAsync(prepared, token));
    }

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();
}
