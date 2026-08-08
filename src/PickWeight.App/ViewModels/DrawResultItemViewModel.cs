using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PickWeight.Core.Models;

namespace PickWeight_App.ViewModels;

public partial class DrawResultItemViewModel : ObservableObject
{
    private readonly Func<DrawResultItemViewModel, Task> _commitAsync;
    private readonly Func<DrawResultItemViewModel, Task> _discardAsync;
    private readonly Func<DrawResultItemViewModel, Task> _replaceAsync = _ => Task.CompletedTask;
    private readonly Func<DrawResultItemViewModel, DrawRating, Task<bool>> _rateAsync = (_, _) => Task.FromResult(false);
    private bool _isCommitted;
    private bool _isDiscarded;
    private DrawRating _rating = DrawRating.None;
    private string? _seatPosition;

    public DrawResultItemViewModel(
        Member member,
        DrawnMemberWeight weights,
        DrawMode drawMode,
        bool isCommitted,
        bool allowsManualCommit,
        string? seatPosition,
        Func<DrawResultItemViewModel, Task> commitAsync,
        Func<DrawResultItemViewModel, Task> discardAsync,
        Func<DrawResultItemViewModel, Task>? replaceAsync = null,
        Func<DrawResultItemViewModel, DrawRating, Task<bool>>? rateAsync = null)
    {
        Member = member;
        DrawMode = drawMode;
        SeatPosition = seatPosition;
        ExplicitWeight = weights.ExplicitWeight;
        CalledWeight = weights.CalledWeight;
        IsCommitted = isCommitted;
        AllowsManualCommit = allowsManualCommit;
        _commitAsync = commitAsync;
        _discardAsync = discardAsync;
        _replaceAsync = replaceAsync ?? (_ => Task.CompletedTask);
        _rateAsync = rateAsync ?? ((_, _) => Task.FromResult(false));
        CanReplace = _replaceAsync is not null;
    }

    public Member Member { get; }
    public DrawMode DrawMode { get; }
    public string Name => Member.Name;
    public string? SeatPosition { get; private set; }
    public bool IsSeated => !string.IsNullOrWhiteSpace(SeatPosition);
    public double ExplicitWeight { get; }
    public double CalledWeight { get; }
    public bool AllowsManualCommit { get; }
    public bool CanCommitResult => AllowsManualCommit && !IsCommitted && !IsDiscarded;
    public bool CanDiscardResult => !IsDiscarded;
    public bool CanReplace { get; }

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

    /// <summary>当前评价等级。</summary>
    public DrawRating Rating
    {
        get => _rating;
        private set
        {
            if (SetProperty(ref _rating, value))
            {
                OnPropertyChanged(nameof(HasRating));
                OnPropertyChanged(nameof(RatingText));
                NotifyRatingCommands();
            }
        }
    }

    /// <summary>是否已经给出评价，用于显示「已评价」小字。</summary>
    public bool HasRating => Rating != DrawRating.None;

    /// <summary>评价结果的中文文本，未评价时为空。</summary>
    public string RatingText => Rating switch
    {
        DrawRating.Excellent => "已评价：良好",
        DrawRating.Fair => "已评价：一般",
        DrawRating.NoAnswer => "已评价：未答上",
        DrawRating.Volunteered => "已评价：主动",
        _ => string.Empty
    };

    public void MarkCommitted()
    {
        IsCommitted = true;
    }

    public void UpdateSeatPosition(string? seatPosition)
    {
        if (SetProperty(ref _seatPosition, seatPosition))
        {
            OnPropertyChanged(nameof(IsSeated));
        }
    }

    public void MarkDiscarded()
    {
        IsDiscarded = true;
    }

    [RelayCommand]
    private Task CommitAsync() => _commitAsync(this);

    [RelayCommand(CanExecute = nameof(CanDiscardExecute))]
    private Task DiscardAsync() => _discardAsync(this);

    [RelayCommand]
    private Task ReplaceAsync() => _replaceAsync(this);

    private bool CanDiscardExecute() => CanDiscardResult;

    [RelayCommand]
    private async Task RateGoodAsync()
    {
        if (await _rateAsync(this, DrawRating.Excellent))
        {
            Rating = DrawRating.Excellent;
        }
    }

    [RelayCommand]
    private async Task RateFairAsync()
    {
        if (await _rateAsync(this, DrawRating.Fair))
        {
            Rating = DrawRating.Fair;
        }
    }

    private void NotifyRatingCommands()
    {
        RateGoodCommand.NotifyCanExecuteChanged();
        RateFairCommand.NotifyCanExecuteChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(CanCommitResult));
        OnPropertyChanged(nameof(CanDiscardResult));
        OnPropertyChanged(nameof(StateText));
        CommitCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        ReplaceCommand.NotifyCanExecuteChanged();
    }
}
