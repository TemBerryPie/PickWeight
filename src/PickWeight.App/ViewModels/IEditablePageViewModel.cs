namespace PickWeight_App.ViewModels;

public interface IEditablePageViewModel
{
    bool IsDirty { get; }
    Task SaveAsync();
}
