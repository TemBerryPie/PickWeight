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
        UnhandledException += OnUnhandledException;

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PickWeight");
        var services = new ServiceCollection();
        services.AddSingleton<IStateStore>(new JsonStateStore(Path.Combine(dataDirectory, "state.json")));
        services.AddSingleton<IImplicitWeightProviderFactory, JsonImplicitWeightProviderFactory>();
        services.AddSingleton<IDrawService, DrawService>();
        services.AddSingleton<IMemberImportExportService, MemberImportExportService>();
        services.AddSingleton<IMemberSpreadsheetImportService, ExcelMemberImportService>();
        services.AddSingleton<IGroupImportExportService, GroupImportExportService>();
        services.AddSingleton<IHistoryExportService, HistoryExportService>();
        services.AddSingleton<AppDataService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDrawExecutionCoordinator, DrawExecutionCoordinator>();
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
        services.AddSingleton<ClassIslandPeriodService>();
        services.AddSingleton<IDrawExecutionService, DrawExecutionService>();
        services.AddSingleton<FileDialogService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<FirstRunWizardService>();
        services.AddTransient<DrawPageViewModel>();
        services.AddTransient<HistoryPageViewModel>();
        services.AddTransient<MembersPageViewModel>();
        services.AddTransient<GroupsPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<SeatMapPageViewModel>();
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
        Services.GetRequiredService<ClassIslandPeriodService>().Start();
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

    /// <summary>
    /// Last-resort guard for exceptions that escape an <c>async void</c> event
    /// handler. Without this the WinUI message loop fail-fasts and the window
    /// simply vanishes, which gives the user no idea what went wrong.
    /// </summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var exception = e.Exception;
        e.Handled = true;

        try
        {
            _ = Services.GetRequiredService<AppLogService>()
                .InfoAsync($"Unhandled exception: {exception}");
        }
        catch
        {
            // Diagnostics must never mask the original failure.
        }

        try
        {
            DispatcherQueue?.TryEnqueue(async () =>
            {
                if (Window?.Content is not FrameworkElement { XamlRoot: { } root })
                {
                    return;
                }

                var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    XamlRoot = root,
                    Title = "发生意外错误",
                    Content = $"操作未能完成，应用会继续运行。\n\n{exception.Message}",
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            });
        }
        catch
        {
            // If we cannot even show the dialog, staying alive still beats crashing.
        }
    }

    private static async Task<QuickDrawPresentation?> PrepareQuickDrawAsync(CancellationToken cancellationToken)
    {
        var execution = GetService<IDrawExecutionService>();
        var settings = GetService<ISettingsService>();
        var targetCount = Math.Max(1, settings.Current.DrawTargetCount);
        var prepared = await execution.PrepareAsync(
            new DrawExecutionRequest(
                TargetCount: targetCount,
                Filters: new PickWeight.Core.Models.DrawFilterOptions(
                    settings.Current.DrawIncludeGroupName,
                    settings.Current.DrawExcludeGroupName,
                    Math.Max(0, settings.Current.DrawExcludeRecentDrawCount),
                    settings.Current.DrawOnlyNeverDrawn),
                Mode: settings.Current.DrawMode,
                AutoCommitSelection: true),
            cancellationToken);
        var selected = prepared.SelectedMembers;
        if (selected.Count == 0)
        {
            await execution.DiscardAsync(prepared, CancellationToken.None);
            return null;
        }

        // 滚动池取全班启用成员姓名，让滚动观感更丰富；空则窗口内回退候选名。
        var rollingNames = GetService<AppDataService>()
            .ActiveClassroom.Members
            .Where(member => member.IsActive)
            .Select(member => member.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return new QuickDrawPresentation(
            prepared.CandidateNames,
            selected.Select(member => member.Name).ToList(),
            rollingNames,
            async token =>
            {
                await execution.CommitAsync(prepared, confirmSelection: true, cancellationToken: token);
            },
            token => execution.DiscardAsync(prepared, token));
    }

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();
}
