using System.Windows.Input;
using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

public sealed class InspectViewModel : WorkspaceBase
{
    private readonly RelayCommand primary = new(() => { }, () => false);

    public InspectViewModel(WorkspaceHost host) : base(host, "Inspect", "Nothing has run yet.") { }

    public override ICommand PrimaryCommand => primary;
    public override string PrimaryLabel => "Save spectrogram";
}