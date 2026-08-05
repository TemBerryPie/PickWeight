using CommunityToolkit.Mvvm.ComponentModel;
using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight_App.ViewModels;

/// <summary>
/// The lightweight row shown by the member-management list. Detailed edits are
/// intentionally performed by a dialog so that the list can stay focused on
/// recognition and availability.
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
        }
    }

    public string GenderText => Gender switch
    {
        MemberGender.Male => "男",
        MemberGender.Female => "女",
        _ => "未设置"
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

    public int CalledCount => _member.CalledCount;

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

    public Member ToModel()
    {
        var model = _member.Clone();
        model.Name = model.Name.Trim();
        return model;
    }

    public void ApplyEdit(string name, MemberGender? gender, double explicitWeight, int calledCount)
    {
        var normalizedName = name.Trim();
        var normalizedWeight = Math.Clamp(
            double.IsFinite(explicitWeight) ? explicitWeight : 0d,
            0d,
            MemberRules.MaximumWeight);
        var normalizedCalledCount = Math.Max(0, calledCount);
        var changed = !string.Equals(normalizedName, _member.Name, StringComparison.Ordinal)
            || _member.Gender != gender
            || Math.Abs(_member.ExplicitWeight - normalizedWeight) > 0.0000001d
            || _member.CalledCount != normalizedCalledCount;

        if (!changed)
        {
            return;
        }

        Name = normalizedName;
        Gender = gender;

        // Update the count first. Setting the explicit weight afterwards ensures
        // that a user-entered value remains exactly the value the user chose.
        MemberRules.ChangeCalledCount(_member, normalizedCalledCount);
        MemberRules.SetExplicitWeight(_member, normalizedWeight);

        OnPropertyChanged(nameof(Weight));
        OnPropertyChanged(nameof(CalledCount));
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
        _changed();
    }

    public void AcceptChanges()
    {
        _member.Name = _member.Name.Trim();
        OriginalName = _member.Name;
        OnPropertyChanged(nameof(Name));
    }
}
