using System.Windows.Input;
using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

public sealed class SplitViewModel : WorkspaceBase
{
    private readonly RelayCommand primary = new(() => { }, () => false);

    public SplitViewModel(WorkspaceHost host) : base(host, "Split", "Nothing has run yet.") { }

    public override ICommand PrimaryCommand => primary;
    public override string PrimaryLabel => "Split tracks";
}