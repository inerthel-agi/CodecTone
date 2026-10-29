using AudioConverter.Desktop.Services;
using AudioConverter.Desktop.ViewModels;

namespace AudioConverter.Desktop.Tests;

[TestClass]
public sealed class NormalizeViewModelTests
{
    [TestMethod]
    public void TargetPresetAndCustomValueReplaceEachOther()
    {
        var vm = new NormalizeViewModel(new WorkspaceHost(new SilentDialogs()));
        Assert.AreEqual("-14 LUFS", vm.SelectedTargetPreset);

        vm.CustomTargetLufs = "-18";
        Assert.IsNull(vm.SelectedTargetPreset);

        vm.SelectedTargetPreset = "-23 LUFS";
        Assert.AreEqual("", vm.CustomTargetLufs);
    }

    [TestMethod]
    public void WithoutASource_ActionsAreDisabledAndTheListIsEmpty()
    {
        var vm = new NormalizeViewModel(new WorkspaceHost(new SilentDialogs()));

        Assert.IsFalse(vm.NormalizeCommand.CanExecute(null));
        Assert.IsFalse(vm.AnalyzeCommand.CanExecute(null));
        Assert.IsFalse(vm.HasRows);
        Assert.IsTrue(vm.IsAnalysisEmpty);
        Assert.AreEqual("Normalize", vm.PrimaryLabel);
    }

    [TestMethod]
    public async Task DroppedFile_BecomesTheSourceAndEnablesActions()
    {
        var vm = new NormalizeViewModel(new WorkspaceHost(new SilentDialogs()));
        var path = typeof(NormalizeViewModelTests).Assembly.Location;

        await vm.AcceptDropAsync([path]);

        Assert.AreEqual(path, vm.SourcePath);
        Assert.IsTrue(vm.NormalizeCommand.CanExecute(null));
        Assert.IsTrue(vm.AnalyzeCommand.CanExecute(null));
        Assert.IsTrue(ReferenceEquals(vm.PrimaryCommand, vm.NormalizeCommand));
    }

    private sealed class SilentDialogs : IDialogService
    {
        public string? ChooseAudioFile() => null;
        public IReadOnlyList<string>? ChooseAudioFiles() => null;
        public string? ChooseFile(string filter) => null;
        public string? ChooseSaveFile(string filter, string fileName) => null;
        public string? ChooseFolder(string? initialDirectory = null) => null;
        public string? ChooseCoverImage() => null;
        public void Error(string message) { }
        public bool Confirm(string message) => false;
    }
}
