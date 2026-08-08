using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PickWeight.Core.Models;
using PickWeight.Core.Services;
using PickWeight_App.Services;
using PickWeight_App.ViewModels;

namespace PickWeight_App.Pages;

/// <summary>
/// 座位表编辑器：列式布局。每列独立人数，可留过道，靠门/靠窗镜像。
/// 所有修改先落内存，点「保存」才写入班级数据。
/// </summary>
public sealed partial class SeatMapPage : Page
{
    private readonly AppDataService _data;
    private SeatMap? _draft;
    private List<string> _allMemberNames = [];
    private IReadOnlySet<string> _highlightNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public SeatMapPage()
    {
        InitializeComponent();
        _data = App.GetService<AppDataService>();
        DataContext = App.GetService<SeatMapPageViewModel>();
        ViewModel.Initialize(SaveAsync, Revert);
        Loaded += (_, _) =>
        {
            LoadDraft();
            PlayPageEntrance();
        };
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(ViewModel.StatusMessage);

    public SeatMapPageViewModel ViewModel => (SeatMapPageViewModel)DataContext;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _highlightNames = e.Parameter is IEnumerable<string> names
            ? names.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public void LoadDraft()
    {
        _draft = _data.ActiveClassroom.SeatMap?.Clone() ?? new SeatMap();
        if (_draft.Columns.Count == 0)
        {
            SeatMapService.AddColumn(_draft);
        }

        // 对齐每列长度
        foreach (var column in _draft.Columns)
        {
            while (column.MemberNames.Count < column.SeatCount)
            {
                column.MemberNames.Add(null);
            }

            if (column.MemberNames.Count > column.SeatCount)
            {
                column.MemberNames = column.MemberNames.Take(column.SeatCount).ToList();
            }
        }

        _allMemberNames = _data.ActiveClassroom.Members
            .Where(member => member.IsActive && !string.IsNullOrWhiteSpace(member.Name))
            .Select(member => member.Name.Trim())
            .ToList();
        DoorStartRadio.IsChecked = _draft.DoorSide == SeatSideOrientation.Start;
        DoorEndRadio.IsChecked = _draft.DoorSide == SeatSideOrientation.End;
        ViewModel.IsDirty = false;
        RebuildSeatGrid();
        RefreshUnassigned();
    }

    public async Task SaveAsync()
    {
        if (_draft is null)
        {
            return;
        }

        var error = SeatMapService.Validate(_draft);
        if (error is not null)
        {
            ViewModel.SetStatus(error, InfoBarSeverity.Error);
            return;
        }

        _data.ActiveClassroom.SeatMap = _draft.Clone();
        await _data.SaveAsync();
        ViewModel.IsDirty = false;
        ViewModel.SetStatus("座位表已保存。", InfoBarSeverity.Success);
    }

    public void Revert()
    {
        LoadDraft();
        ViewModel.SetStatus("已撤销未保存的修改。", InfoBarSeverity.Informational);
    }

    private void PlayPageEntrance()
    {
        var animations = App.GetService<AnimationService>();
        var sections = new UIElement[] { HeaderSection, CommandSection, DoorSection, SeatGridPanel, UnassignedSection };
        for (var index = 0; index < sections.Length; index++)
        {
            animations.PlayPageSection(sections[index], index);
        }
    }

    // ---------- 座位网格构建 ----------

    private void RebuildSeatGrid()
    {
        SeatGridPanel.Children.Clear();
        if (_draft is null)
        {
            return;
        }

        var reversed = _draft.DoorSide == SeatSideOrientation.End;
        var columns = _draft.Columns;
        for (var index = 0; index < columns.Count; index++)
        {
            var logicalIndex = reversed ? columns.Count - 1 - index : index;
            var column = columns[logicalIndex];
            var columnPanel = new StackPanel { Spacing = 4, Margin = new Thickness(4, 0, 4, 0) };

            // 列头：列号 + 操作按钮
            var header = new Grid { ColumnSpacing = 4 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = $"第 {logicalIndex + 1} 列",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var settingsButton = new Button
            {
                Content = new SymbolIcon(Symbol.Setting),
                Padding = new Thickness(6),
                Tag = logicalIndex
            };
            ToolTipService.SetToolTip(settingsButton, "设置列人数与过道");
            settingsButton.Click += ColumnSettings_Click;
            var removeButton = new Button
            {
                Content = new SymbolIcon(Symbol.Delete),
                Padding = new Thickness(6),
                Tag = logicalIndex
            };
            ToolTipService.SetToolTip(removeButton, "删除整列");
            removeButton.Click += ColumnRemove_Click;
            Grid.SetColumn(label, 0);
            Grid.SetColumn(settingsButton, 1);
            Grid.SetColumn(removeButton, 2);
            header.Children.Add(label);
            header.Children.Add(settingsButton);
            header.Children.Add(removeButton);
            columnPanel.Children.Add(header);

            // 座位列表
            for (var row = 0; row < column.MemberNames.Count; row++)
            {
                var seatName = column.MemberNames[row];
                var seatButton = new Button
                {
                    Width = 92,
                    Height = 44,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 0, 6, 0),
                    Tag = (Column: logicalIndex, Row: row)
                };
                ToolTipService.SetToolTip(seatButton, seatName is null ? "空座位（点击分配）" : $"{seatName}（点击更换）");
                seatButton.Click += Seat_Click;

                if (seatName is null)
                {
                    seatButton.Content = new FontIcon { Glyph = "", FontSize = 14 };
                    seatButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    seatButton.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 128, 128, 128));
                }
                else
                {
                    seatButton.Content = new TextBlock
                    {
                        Text = seatName,
                        FontSize = 13,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap
                    };

                    if (_highlightNames.Contains(seatName))
                    {
                        var accentBrush = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush
                            ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212));
                        seatButton.BorderBrush = accentBrush;
                        seatButton.BorderThickness = new Thickness(2);
                    }
                }

                columnPanel.Children.Add(seatButton);
            }

            // 列底人数
            var countText = new TextBlock
            {
                Text = $"{column.MemberNames.Count} 人",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
            };
            columnPanel.Children.Add(countText);

            SeatGridPanel.Children.Add(columnPanel);

            // 过道：视觉分隔。镜像（DoorSide=End）时过道归属「上一逻辑列」，
            // 使镜像后过道仍落在两列之间，而不是出现在门侧边缘。
            if (reversed)
            {
                if (logicalIndex > 0 && columns[logicalIndex - 1].HasAisleAfter)
                {
                    SeatGridPanel.Children.Add(new Border { Width = 24, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) });
                }
            }
            else if (column.HasAisleAfter)
            {
                SeatGridPanel.Children.Add(new Border { Width = 24, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) });
            }
        }
    }

    private void RefreshUnassigned()
    {
        if (_draft is null)
        {
            return;
        }

        var seated = SeatMapService.SeatedNames(_draft);
        var unassigned = _allMemberNames
            .Where(name => !seated.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        UnassignedList.ItemsSource = unassigned;
        ViewModel.HasUnassigned = unassigned.Count > 0;
    }

    private void MarkDirty()
    {
        ViewModel.IsDirty = true;
        ViewModel.SetStatus("有未保存的修改。", InfoBarSeverity.Informational);
    }

    // ---------- 事件处理 ----------

    private async void Seat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: (int columnIndex, int rowIndex) })
        {
            return;
        }

        var (column, row) = ((int, int))((Button)sender).Tag;
        var currentName = _draft?.Columns[column].MemberNames[row];
        await ShowMemberPickerAsync(
            $"{(currentName is null ? "分配" : $"更换 {currentName}")}",
            currentName,
            (selected) =>
            {
                if (selected is null)
                {
                    if (currentName is not null)
                    {
                        SeatMapService.Clear(_draft!, column, row);
                        MarkDirty();
                        RebuildSeatGrid();
                        RefreshUnassigned();
                    }

                    return;
                }

                SeatMapService.Assign(_draft!, column, row, selected);
                MarkDirty();
                RebuildSeatGrid();
                RefreshUnassigned();
            },
            includeEmptyOption: currentName is not null);
    }

    private void UnassignedMember_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            // 点击未分配成员：如果有空座位，自动放入第一个空位
            var empty = FindFirstEmptySeat();
            if (empty is { } position)
            {
                SeatMapService.Assign(_draft!, position.Column, position.Row, name);
                MarkDirty();
                RebuildSeatGrid();
                RefreshUnassigned();
            }
            else
            {
                ViewModel.SetStatus("没有空座位了，请先添加列或清空座位。", InfoBarSeverity.Warning);
            }
        }
    }

    private (int Column, int Row)? FindFirstEmptySeat()
    {
        if (_draft is null)
        {
            return null;
        }

        for (var columnIndex = 0; columnIndex < _draft.Columns.Count; columnIndex++)
        {
            var names = _draft.Columns[columnIndex].MemberNames;
            for (var row = 0; row < names.Count; row++)
            {
                if (names[row] is null)
                {
                    return (columnIndex, row);
                }
            }
        }

        return null;
    }

    private async void ColumnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int columnIndex } || _draft is null)
        {
            return;
        }

        var column = _draft.Columns[columnIndex];
        var countBox = new NumberBox
        {
            Header = "该列人数",
            Minimum = 0,
            Maximum = SeatMapService.MaxSeatCountPerColumn,
            Value = column.SeatCount,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        var aisleToggle = new ToggleSwitch
        {
            Header = "此列之后留过道",
            IsOn = column.HasAisleAfter,
            OffContent = "不留",
            OnContent = "留"
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(countBox);
        panel.Children.Add(aisleToggle);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"第 {columnIndex + 1} 列设置",
            Content = panel,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消"
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var removed = SeatMapService.ResizeColumn(_draft, columnIndex, (int)Math.Round(countBox.Value));
        column.HasAisleAfter = aisleToggle.IsOn;
        if (removed.Count > 0)
        {
            ViewModel.SetStatus($"列人数调小，移出：{string.Join("、", removed)}。", InfoBarSeverity.Warning);
        }

        MarkDirty();
        RebuildSeatGrid();
        RefreshUnassigned();
    }

    private async void ColumnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int columnIndex } || _draft is null)
        {
            return;
        }

        var column = _draft.Columns[columnIndex];
        var occupied = column.MemberNames.Count(name => name is not null);
        var message = occupied > 0
            ? $"确定删除第 {columnIndex + 1} 列吗？{occupied} 名成员将被移出座位。"
            : $"确定删除第 {columnIndex + 1} 列吗？";
        if (!await App.GetService<DialogService>().ConfirmAsync("删除整列", message, "删除"))
        {
            return;
        }

        var removed = SeatMapService.RemoveColumn(_draft, columnIndex);
        if (removed.Count > 0)
        {
            ViewModel.SetStatus($"已删除列，移出：{string.Join("、", removed)}。", InfoBarSeverity.Warning);
        }

        MarkDirty();
        RebuildSeatGrid();
        RefreshUnassigned();
    }

    private void AddColumn_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null)
        {
            return;
        }

        SeatMapService.AddColumn(_draft);
        MarkDirty();
        RebuildSeatGrid();
    }

    private void AutoAssign_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null)
        {
            return;
        }

        var unassigned = SeatMapService.AutoAssign(_draft, _allMemberNames);
        MarkDirty();
        RebuildSeatGrid();
        RefreshUnassigned();
        if (unassigned.Count > 0)
        {
            ViewModel.SetStatus($"已自动排入，{unassigned.Count} 人未排入（座位不足）：{string.Join("、", unassigned.Take(10))}{(unassigned.Count > 10 ? "…" : "")}", InfoBarSeverity.Warning);
        }
        else
        {
            ViewModel.SetStatus("自动排入完成。", InfoBarSeverity.Success);
        }
    }

    private async void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null)
        {
            return;
        }

        if (!await App.GetService<DialogService>().ConfirmAsync("清空座位", "确定清空所有座位吗？成员不会被删除。", "清空"))
        {
            return;
        }

        foreach (var column in _draft.Columns)
        {
            for (var row = 0; row < column.MemberNames.Count; row++)
            {
                column.MemberNames[row] = null;
            }
        }

        MarkDirty();
        RebuildSeatGrid();
        RefreshUnassigned();
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private void Revert_Click(object sender, RoutedEventArgs e) => Revert();

    private void DoorSide_Checked(object sender, RoutedEventArgs e)
    {
        if (_draft is null || sender is not RadioButton radio)
        {
            return;
        }

        var newSide = string.Equals(radio.Tag as string, "End", StringComparison.Ordinal)
            ? SeatSideOrientation.End
            : SeatSideOrientation.Start;
        if (_draft.DoorSide == newSide)
        {
            return;
        }

        _draft.DoorSide = newSide;
        MarkDirty();
        RebuildSeatGrid();
    }

    // ---------- 成员选择对话框 ----------

    private List<string> _pickerSource = [];
    private Action<string?>? _pickerCallback;

    private async Task ShowMemberPickerAsync(
        string title,
        string? currentMemberName,
        Action<string?> onSelected,
        bool includeEmptyOption = false)
    {
        _pickerCallback = onSelected;
        var seated = SeatMapService.SeatedNames(_draft!);
        // 可选列表 = 未排座成员 + 当前座位成员（允许原地换人时不把自己排除）
        _pickerSource = _allMemberNames
            .Where(name => !seated.Contains(name)
                || string.Equals(name, currentMemberName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (includeEmptyOption)
        {
            _pickerSource.Insert(0, "（清空此座）");
        }

        MemberSearchBox.Text = string.Empty;
        MemberPickerList.ItemsSource = _pickerSource;
        MemberPickerDialog.Title = title;
        MemberPickerDialog.XamlRoot = XamlRoot;
        var result = await MemberPickerDialog.ShowAsync();
        if (result == ContentDialogResult.Primary
            && MemberPickerList.SelectedItem is string selectedName)
        {
            _pickerCallback?.Invoke(selectedName.StartsWith("（清空", StringComparison.Ordinal) ? null : selectedName);
        }
    }

    private void MemberSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = MemberSearchBox.Text.Trim();
        MemberPickerList.ItemsSource = query.Length == 0
            ? _pickerSource
            : _pickerSource.Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}



