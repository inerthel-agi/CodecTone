using AudioConverter.Desktop.Mvvm;

namespace AudioConverter.Desktop.ViewModels;

public enum QueueState { Waiting, Running, Done, Failed, Skipped }

/// <summary>One file in a batch, with its own status line and progress.</summary>
public sealed class QueueItem(string path, string name) : ObservableObject
{
    private string status = "Waiting";
    private double progress;
    private QueueState state;
    private bool included = true;

    public string Path { get; } = path;
    public string Name { get; } = name;
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string Status { get => status; private set => Set(ref status, value); }
    /// <summary>Completion from 0 to 100 while <see cref="State"/> is Running.</summary>
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public QueueState State { get => state; private set { if (Set(ref state, value)) { Raise(nameof(IsRunning)); Raise(nameof(CanToggle)); } } }
    public bool IsRunning => State == QueueState.Running;
    /// <summary>Skipped files cannot be switched back on.</summary>
    public bool CanToggle => State != QueueState.Skipped;
    /// <summary>False when the user excluded the file from the batch.</summary>
    public bool Included { get => included; set => Set(ref included, value); }

    public void Update(QueueState newState, string newStatus, double newProgress = 0)
    {
        State = newState;
        Status = newStatus;
        Progress = Math.Clamp(newProgress, 0, 100);
    }
}
