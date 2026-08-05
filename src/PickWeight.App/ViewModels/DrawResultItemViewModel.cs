using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PickWeight.Core.Models;

namespace PickWeight_App.ViewModels;

public partial class DrawResultItemViewModel : ObservableObject
{
    private readonly Func<DrawResultItemViewModel, Task> _commitAsync;
    private readonly Func<DrawResultItemViewModel, Task> _discardAsync;
    private bool _isCommitted;
    private bool _isDiscarded;

    public DrawResultItemViewModel(
        Member member,
        DrawnMemberWeight weights,
        DrawMode drawMode,
        bool isCommitted,
        bool allowsManualCommit,
        Func<DrawResultItemViewModel, Task> commitAsync,
        Func<DrawResultItemViewModel, Task> discardAsync)
    {
        Member = member;
        DrawMode = drawMode;
        ExplicitWeight = weights.ExplicitWeight;
        CalledWeight = weights.CalledWeight;
        IsCommitted = isCommitted;
        AllowsManualCommit = allowsManualCommit;
        _commitAsync = commitAsync;
        _discardAsync = discardAsync;
    }

    public Member Member { get; }
    public DrawMode DrawMode { get; }
    public string Name => Member.Name;
    public double ExplicitWeight { get; }
    public double CalledWeight { get; }
    public bool AllowsManualCommit { get; }
    public bool CanCommitResult => AllowsManualCommit && !IsCommitted && !IsDiscarded;
    public bool CanDiscardResult => !IsDiscarded;

    public string WeightText => DrawMode switch
    {
        DrawMode.FullRandom => "随机抽取",
        DrawMode.SmartRandom => $"智能权重：{CalledWeight:G6}",
        DrawMode.Selection => $"候选权重：{ExplicitWeight:G6} × {CalledWeight:G6}",
        _ => $"权重：{ExplicitWeight:G6} × {CalledWeight:G6}"
    };

    public string StateText
    {
        get
        {
            if (IsDiscarded)
            {
                return "已丢弃";
            }

            if (IsCommitted)
            {
                return DrawMode == DrawMode.FullRandom
                    ? "已写入抽取历史（不计入被点次数）"
                    : "已计入被点次数";
            }

            return AllowsManualCommit ? "点击姓名计入被点次数" : "不计入被点次数";
        }
    }

    public bool IsCommitted
    {
        get => _isCommitted;
        private set
        {
            if (SetProperty(ref _isCommitted, value))
            {
                NotifyStateChanged();
            }
        }
    }

    public bool IsDiscarded
    {
        get => _isDiscarded;
        private set
        {
            if (SetProperty(ref _isDiscarded, value))
            {
                NotifyStateChanged();
            }
        }
    }

    public void MarkCommitted()
    {
        IsCommitted = true;
    }

    public void MarkDiscarded()
    {
        IsDiscarded = true;
    }

    [RelayCommand]
    private Task CommitAsync() => _commitAsync(this);

    [RelayCommand(CanExecute = nameof(CanDiscardExecute))]
    private Task DiscardAsync() => _discardAsync(this);

    private bool CanDiscardExecute() => CanDiscardResult;

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(CanCommitResult));
        OnPropertyChanged(nameof(CanDiscardResult));
        OnPropertyChanged(nameof(StateText));
        CommitCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }
}
