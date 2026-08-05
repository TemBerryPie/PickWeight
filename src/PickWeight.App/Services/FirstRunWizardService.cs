using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PickWeight.Core.Abstractions;
using PickWeight.Core.Models;
using PickWeight_App.ViewModels;

namespace PickWeight_App.Services;

/// <summary>Owns the first-run flow so it can be launched from startup, settings, or an empty state.</summary>
public sealed class FirstRunWizardService
{
    private readonly AppDataService _data;
    private readonly IMemberImportExportService _memberImport;
    private readonly ISettingsService _settings;
    private readonly FileDialogService _files;
    private readonly AnimationService _animations;
    private ContentDialog? _dialog;
    private StackPanel _content = null!;
    private TextBox _names = null!;
    private TextBlock _preview = null!;
    private ComboBox _mode = null!;
    private NumberBox _count = null!;
    private ComboBox _include = null!;
    private ComboBox _exclude = null!;
    private NumberBox _recent = null!;
    private CheckBox _never = null!;
    private int _step;

    public FirstRunWizardService(AppDataService data, IMemberImportExportService memberImport,
        ISettingsService settings, FileDialogService files, AnimationService animations)
    {
        _data = data;
        _memberImport = memberImport;
        _settings = settings;
        _files = files;
        _animations = animations;
    }

    public async Task ShowAsync(XamlRoot root, bool markCompleted = true)
    {
        _step = 0;
        BuildControls();
        _dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "欢迎使用 PickWeight",
            PrimaryButtonText = "下一步",
            SecondaryButtonText = string.Empty,
            CloseButtonText = "取消",
            Content = _content
        };
        _dialog.Opened += Dialog_Opened;
        _dialog.PrimaryButtonClick += async (s, e) => await PrimaryClickAsync(e, markCompleted);
        _dialog.SecondaryButtonClick += (s, e) => { e.Cancel = true; PreviousStep(); };
        await _dialog.ShowAsync();
        _dialog = null;
    }

    private void BuildControls()
    {
        _content = new StackPanel { Spacing = 12, MinWidth = 420 };
        _names = new TextBox { Header = "成员名单", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, PlaceholderText = "每行输入一名成员，也支持“姓名 权重”格式" };
        _preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 120, 120)) };
        _mode = new ComboBox { Header = "点名模式", ItemsSource = new[] { "加权模式", "智能随机", "完全随机", "选择模式" }, SelectedIndex = (int)_settings.Current.DrawMode };
        _count = new NumberBox { Header = "抽取人数", Minimum = 1, Maximum = 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Value = _settings.Current.DrawTargetCount };
        _include = new ComboBox { Header = "仅抽指定小组" };
        _exclude = new ComboBox { Header = "排除指定小组" };
        _recent = new NumberBox { Header = "排除最近抽取记录数", Minimum = 0, Maximum = 10000, Value = _settings.Current.DrawExcludeRecentDrawCount };
        _never = new CheckBox { Content = "只抽从未出现在历史中的成员", IsChecked = _settings.Current.DrawOnlyNeverDrawn };
        LoadGroups();
        ShowStep();
    }

    private void LoadGroups()
    {
        var groups = _data.State.Groups.Select(g => g.GroupName).OrderBy(n => n).ToList();
        _include.ItemsSource = new[] { "不限制" }.Concat(groups).ToList();
        _exclude.ItemsSource = new[] { "不排除" }.Concat(groups).ToList();
        _include.SelectedIndex = Math.Max(0, groups.FindIndex(n => string.Equals(n, _settings.Current.DrawIncludeGroupName, StringComparison.OrdinalIgnoreCase)) + 1);
        _exclude.SelectedIndex = Math.Max(0, groups.FindIndex(n => string.Equals(n, _settings.Current.DrawExcludeGroupName, StringComparison.OrdinalIgnoreCase)) + 1);
    }

    private void ShowStep()
    {
        if (_dialog is null) return;
        _content.Children.Clear();
        switch (_step)
        {
            case 0:
                _content.Children.Add(new TextBlock { Text = "完成几步简单设置后，就可以开始点名。你可以现在导入成员，也可以稍后再配置。", TextWrapping = TextWrapping.Wrap });
                _content.Children.Add(new TextBlock { Text = "向导不会删除现有成员、分组或历史记录。", TextWrapping = TextWrapping.Wrap });
                _dialog.Title = "欢迎使用 PickWeight";
                _dialog.PrimaryButtonText = "开始设置";
                _dialog.SecondaryButtonText = string.Empty;
                break;
            case 1:
                _content.Children.Add(_names);
                var importButton = new Button { Content = "从文件导入" };
                importButton.Click += ImportFile_Click;
                var applyButton = new Button { Content = "应用名单" };
                applyButton.Click += ApplyNames_Click;
                _content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { importButton, applyButton } });
                _content.Children.Add(_preview);
                _dialog.Title = "导入成员";
                _dialog.PrimaryButtonText = "下一步";
                _dialog.SecondaryButtonText = "上一步";
                break;
            case 2:
                _content.Children.Add(_mode); _content.Children.Add(_count); _content.Children.Add(_include); _content.Children.Add(_exclude); _content.Children.Add(_recent); _content.Children.Add(_never);
                _dialog.Title = "配置点名规则";
                _dialog.PrimaryButtonText = "查看摘要";
                _dialog.SecondaryButtonText = "上一步";
                break;
            default:
                _content.Children.Add(new TextBlock { Text = $"成员：{_data.State.Members.Count} 人\n小组：{_data.State.Groups.Count} 个\n点名模式：{_mode.SelectedItem}\n抽取人数：{Math.Max(1, (int)Math.Round(_count.Value))}\n历史筛选：{(_never.IsChecked == true ? "仅从未抽取成员" : "不限制")}\n小组筛选：{(_include.SelectedIndex > 0 || _exclude.SelectedIndex > 0 ? "已设置" : "不限制")}", TextWrapping = TextWrapping.Wrap });
                _dialog.Title = "设置完成";
                _dialog.PrimaryButtonText = "完成并开始使用";
                _dialog.SecondaryButtonText = "上一步";
                break;
        }
    }

    private async Task PrimaryClickAsync(ContentDialogButtonClickEventArgs args, bool markCompleted)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (_step < 3)
            {
                args.Cancel = true;
                if (_step == 1)
                {
                    await PreviewNamesAsync();
                }
                _step++;
                ShowStep();
                return;
            }

            await SaveAsync();
            if (markCompleted) await _data.MarkFirstRunCompletedAsync();
        }
        finally { deferral.Complete(); }
    }

    private void PreviousStep()
    {
        if (_step <= 0) return;
        _step--;
        ShowStep();
    }

    private async void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        var content = await _files.OpenTextAsync(".txt", ".csv");
        if (content is not null) { _names.Text = content; await PreviewNamesAsync(); }
    }

    private async void ApplyNames_Click(object sender, RoutedEventArgs e) => await PreviewNamesAsync();

    private async Task PreviewNamesAsync()
    {
        var preview = _memberImport.PreviewImport(_names.Text, _data.State.Members);
        _preview.Text = $"可新增 {preview.AddCount} 人，跳过 {preview.SkipCount} 行" + (preview.HasErrors ? "，存在格式错误" : "。");
        if (!preview.HasErrors && preview.AddCount > 0)
        {
            var members = _data.State.Members.Select(m => m.Clone()).ToList();
            _memberImport.ApplyImport(preview, members);
            await _data.ReplaceMembersAsync(members);
        }
    }

    private async Task SaveAsync()
    {
        var include = _include.SelectedIndex > 0 ? _include.SelectedItem as string : null;
        var exclude = _exclude.SelectedIndex > 0 ? _exclude.SelectedItem as string : null;
        await _settings.UpdateAsync(s =>
        {
            s.DrawMode = (DrawMode)Math.Clamp(_mode.SelectedIndex, 0, 3);
            s.DrawTargetCount = Math.Clamp((int)Math.Round(_count.Value), 1, 100);
            s.DrawIncludeGroupName = include;
            s.DrawExcludeGroupName = exclude;
            s.DrawExcludeRecentDrawCount = Math.Max(0, (int)Math.Round(_recent.Value));
            s.DrawOnlyNeverDrawn = _never.IsChecked == true;
        });
    }

    private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args) => _animations.PlayPopupEntrance(sender);
}
