using System.Windows.Input;
using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

public sealed class TagsViewModel : WorkspaceBase
{
    private readonly RelayCommand primary = new(() => { }, () => false);

    public TagsViewModel(WorkspaceHost host) : base(host, "Tags", "Nothing has run yet.") { }

    public override ICommand PrimaryCommand => primary;
    public override string PrimaryLabel => "Save tags";
}