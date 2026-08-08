using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight_App.ViewModels;

/// <summary>
/// The lightweight row shown by the member-management list. It deliberately
/// displays only the name, the called-count heat bar and the active toggle, so
/// the list stays focused; detailed edits are performed by a dialog.
/// </summary>
public partial class MemberRowViewModel : ObservableObject
{
    private readonly Member _member;
    private readonly Action _changed;

    public MemberRowViewModel(Member member, Action changed)
    {
        _member = member;
        _changed = changed;
        OriginalName = member.Name;
    }

    public string OriginalName { get; private set; }

    public string Name
    {
        get => _member.Name;
        private set
        {
            if (string.Equals(value, _member.Name, StringComparison.Ordinal))
            {
                return;
            }

            _member.Name = value;
            OnPropertyChanged();
        }
    }

    public MemberGender? Gender
    {
        get => _member.Gender;
        private set
        {
            if (_member.Gender == value)
            {
                return;
            }

            _member.Gender = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GenderText));
            OnPropertyChanged(nameof(GenderSymbol));
        }
    }

    public string GenderText => Gender switch
    {
        MemberGender.Male => "男",
        MemberGender.Female => "女",
        _ => "未设置"
    };

    public string GenderSymbol => Gender switch
    {
        MemberGender.Male => "♂",
        MemberGender.Female => "♀",
        _ => string.Empty
    };

    public bool IsActive
    {
        get => _member.IsActive;
        set
        {
            if (value == _member.IsActive)
            {
                return;
            }

            _member.IsActive = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public double Weight => _member.ExplicitWeight;
    public string WeightText => $"权重 {Weight:G6}";

    public int CalledCount => _member.CalledCount;
    public string CalledCountText => $"被点 {CalledCount}";

    /// <summary>学号，用于区分重名成员；排序时学号为空的成员排最后。</summary>
    public string StudentId
    {
        get => _member.StudentId ?? string.Empty;
        private set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(normalized, _member.StudentId, StringComparison.Ordinal))
            {
                return;
            }

            _member.StudentId = normalized;
            OnPropertyChanged();
        }
    }

    public string Note
    {
        get => _member.Note ?? string.Empty;
        private set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(normalized, _member.Note, StringComparison.Ordinal))
            {
                return;
            }

            _member.Note = normalized;
            OnPropertyChanged();
        }
    }

    public bool IsAbsentToday
    {
        get => _member.IsAbsentToday();
        set
        {
            DateOnly? next = value ? DateOnly.FromDateTime(DateTime.Today) : null;
            if (_member.AbsentOn == next)
            {
                return;
            }

            _member.AbsentOn = next;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The automatic factor derived from the number of times a member has been
    /// called. It is displayed for transparency but is never directly editable.
    /// </summary>
    public double CalledWeight => MemberRules.CalledWeight(_member.CalledCount);

    /// <summary>
    /// Smart weighting is calculated by the draw rules. The persisted legacy
    /// link flag is deliberately not exposed as an editable member setting.
    /// </summary>
    public string SmartWeightText => "自动计算";

    public string CalledWeightText => CalledWeight.ToString("G6");
    public bool IsWeightAdjustedByRating => _member.IsWeightAdjustedByRating;
    public string WeightBadgeText => IsWeightAdjustedByRating ? "已按课堂表现调整" : string.Empty;
    public Visibility WeightBadgeVisibility => IsWeightAdjustedByRating ? Visibility.Visible : Visibility.Collapsed;

    private double _heatBarValue;
    public double HeatBarValue
    {
        get => _heatBarValue;
        private set
        {
            if (Math.Abs(_heatBarValue - value) < 0.0001d)
            {
                return;
            }

            _heatBarValue = value;
            OnPropertyChanged();
        }
    }

    public void UpdateHeatBar(int maxCalledCount)
    {
        HeatBarValue = maxCalledCount > 0 ? Math.Clamp(CalledCount * 100d / maxCalledCount, 0d, 100d) : 0d;
    }

    public string SeatText { get; private set; } = string.Empty;

    public bool IsSeated { get; private set; }

    public (int Column, int Row) SeatTuple { get; private set; } = (-1, -1);

    public DateTimeOffset? LastDrawnAt { get; private set; }

    public void UpdateSeat((int Column, int Row)? seat)
    {
        IsSeated = seat.HasValue;
        SeatTuple = seat ?? (-1, -1);
        SeatText = seat.HasValue ? $"第 {seat.Value.Column + 1} 列 · 第 {seat.Value.Row + 1} 排" : string.Empty;
        OnPropertyChanged(nameof(SeatText));
        OnPropertyChanged(nameof(IsSeated));
    }

    public void UpdateLastDrawnAt(DateTimeOffset? drawnAt)
    {
        LastDrawnAt = drawnAt;
        OnPropertyChanged(nameof(LastDrawnAt));
    }

    public Member ToModel()
    {
        var model = _member.Clone();
        model.Name = model.Name.Trim();
        return model;
    }

    public void ApplyEdit(
        string name,
        MemberGender? gender,
        double explicitWeight,
        int calledCount,
        string? studentId,
        string? note,
        bool isAbsentToday)
    {
        var normalizedName = name.Trim();
        var normalizedWeight = Math.Clamp(
            double.IsFinite(explicitWeight) ? explicitWeight : 0d,
            0d,
            MemberRules.MaximumWeight);
        var normalizedCalledCount = Math.Max(0, calledCount);
        var normalizedStudentId = string.IsNullOrWhiteSpace(studentId) ? null : studentId.Trim();
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DateOnly? normalizedAbsentOn = isAbsentToday ? DateOnly.FromDateTime(DateTime.Today) : null;
        var changed = !string.Equals(normalizedName, _member.Name, StringComparison.Ordinal)
            || _member.Gender != gender
            || Math.Abs(_member.ExplicitWeight - normalizedWeight) > 0.0000001d
            || _member.CalledCount != normalizedCalledCount
            || !string.Equals(normalizedStudentId, _member.StudentId, StringComparison.Ordinal)
            || !string.Equals(normalizedNote, _member.Note, StringComparison.Ordinal)
            || _member.AbsentOn != normalizedAbsentOn;

        if (!changed)
        {
            return;
        }

        Name = normalizedName;
        Gender = gender;
        StudentId = normalizedStudentId ?? string.Empty;
        Note = normalizedNote ?? string.Empty;
        IsAbsentToday = isAbsentToday;

        // Update the count first. Setting the explicit weight afterwards ensures
        // that a user-entered value remains exactly the value the user chose.
        MemberRules.ChangeCalledCount(_member, normalizedCalledCount);
        MemberRules.SetExplicitWeight(_member, normalizedWeight);

        OnPropertyChanged(nameof(Weight));
        OnPropertyChanged(nameof(WeightText));
        OnPropertyChanged(nameof(CalledCount));
        OnPropertyChanged(nameof(CalledCountText));
        OnPropertyChanged(nameof(CalledWeight));
        OnPropertyChanged(nameof(CalledWeightText));
        _changed();
    }

    public void SetWeight(double explicitWeight)
    {
        var normalizedWeight = Math.Clamp(double.IsFinite(explicitWeight) ? explicitWeight : 0d, 0d, MemberRules.MaximumWeight);
        if (Math.Abs(_member.ExplicitWeight - normalizedWeight) < 0.0000001d) return;
        MemberRules.SetExplicitWeight(_member, normalizedWeight);
        OnPropertyChanged(nameof(Weight));
        OnPropertyChanged(nameof(WeightText));
        _changed();
    }

    public void AcceptChanges()
    {
        _member.Name = _member.Name.Trim();
        OriginalName = _member.Name;
        OnPropertyChanged(nameof(Name));
    }
}


