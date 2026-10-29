using System.Windows.Input;
using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

public sealed class NormalizeViewModel : WorkspaceBase
{
    private readonly RelayCommand primary = new(() => { }, () => false);

    public NormalizeViewModel(WorkspaceHost host) : base(host, "Normalize", "Nothing has run yet.") { }

    public override ICommand PrimaryCommand => primary;
    public override string PrimaryLabel => "Normalize";
}