using System.IO;
using System.Runtime.InteropServices;

namespace AudioConverter.Desktop.Services;

/// <summary>
/// Plays the PCM WAV previews FFmpeg renders through the winmm waveOut API, which every Windows edition ships
/// (WPF's MediaPlayer needs Windows Media Player, absent on N and LTSC editions). Reports the real position and
/// supports pause, seek and live volume. Call <see cref="Poll"/> regularly to detect the end of playback.
/// </summary>
public sealed class AudioPreviewPlayer : IDisposable
{
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint DoneFlag = 1;
    private const uint TimeBytes = 4;
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<WaveHeader>();
    private nint device;
    private nint buffer;
    private nint header;
    private int dataLength;
    private int bytesPerSecond;
    private int blockAlign;
    private int seekOffset;
    private double volume = 0.25;

    public event Action? Ended;
    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }

    /// <summary>Linear volume from 0 to 1; applies immediately.</summary>
    public double Volume
    {
        get => volume;
        set { volume = Math.Clamp(value, 0, 1); ApplyVolume(); }
    }

    public TimeSpan Position
    {
        get
        {
            if (device == 0 || bytesPerSecond == 0) return TimeSpan.Zero;
            var time = new MmTime { Type = TimeBytes };
            if (waveOutGetPosition(device, ref time, (uint)Marshal.SizeOf<MmTime>()) != 0) return TimeSpan.Zero;
            return TimeSpan.FromSeconds((seekOffset + (long)time.Value) / (double)bytesPerSecond);
        }
        set
        {
            if (device == 0) return;
            var offset = (int)Math.Clamp((long)(value.TotalSeconds * bytesPerSecond), 0, dataLength);
            Queue(offset - offset % Math.Max(1, blockAlign));
        }
    }

    public void Play(string path)
    {
        Stop();
        var (format, data) = ReadPcmWave(path);
        var result = waveOutOpen(out device, WaveMapper, ref format, 0, 0, 0);
        if (result != 0) { device = 0; throw new InvalidOperationException($"No audio output is available to play the preview (waveOut error {result})."); }
        buffer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, buffer, data.Length);
        header = Marshal.AllocHGlobal((int)HeaderSize);
        dataLength = data.Length;
        bytesPerSecond = (int)format.AverageBytesPerSecond;
        blockAlign = format.BlockAlign;
        ApplyVolume();
        IsPlaying = true;
        Queue(0);
    }

    public void TogglePause()
    {
        if (!IsPlaying) return;
        _ = IsPaused ? waveOutRestart(device) : waveOutPause(device);
        IsPaused = !IsPaused;
    }

    /// <summary>Raises <see cref="Ended"/> once the device has played the whole buffer.</summary>
    public void Poll()
    {
        if (!IsPlaying || header == 0) return;
        if ((Marshal.PtrToStructure<WaveHeader>(header).Flags & DoneFlag) == 0) return;
        Stop();
        Ended?.Invoke();
    }

    public void Stop()
    {
        if (device != 0)
        {
            _ = waveOutReset(device);
            _ = waveOutUnprepareHeader(device, header, HeaderSize);
            _ = waveOutClose(device);
            device = 0;
        }
        if (header != 0) { Marshal.FreeHGlobal(header); header = 0; }
        if (buffer != 0) { Marshal.FreeHGlobal(buffer); buffer = 0; }
        IsPlaying = false;
        IsPaused = false;
        seekOffset = 0;
    }

    public void Dispose() => Stop();

    private void Queue(int offset)
    {
        _ = waveOutReset(device);
        _ = waveOutUnprepareHeader(device, header, HeaderSize);
        seekOffset = offset;
        Marshal.StructureToPtr(new WaveHeader { Data = buffer + offset, BufferLength = (uint)(dataLength - offset) }, header, false);
        _ = waveOutPrepareHeader(device, header, HeaderSize);
        if (IsPaused) _ = waveOutPause(device);
        _ = waveOutWrite(device, header, HeaderSize);
    }

    private void ApplyVolume()
    {
        if (device == 0) return;
        var level = (uint)Math.Round(volume * 0xFFFF);
        _ = waveOutSetVolume(device, level | level << 16);
    }

    /// <summary>Reads the fmt and data chunks of a PCM RIFF/WAVE file.</summary>
    internal static (WaveFormat Format, byte[] Data) ReadPcmWave(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("The preview is not a WAV file.");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("The preview is not a WAV file.");
        WaveFormat? format = null;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                var chunk = reader.ReadBytes(size);
                var tag = BitConverter.ToUInt16(chunk, 0);
                if (tag != 1) throw new InvalidDataException("Only PCM previews can be played.");
                format = new WaveFormat
                {
                    FormatTag = tag,
                    Channels = BitConverter.ToUInt16(chunk, 2),
                    SamplesPerSecond = BitConverter.ToUInt32(chunk, 4),
                    AverageBytesPerSecond = BitConverter.ToUInt32(chunk, 8),
                    BlockAlign = BitConverter.ToUInt16(chunk, 12),
                    BitsPerSample = BitConverter.ToUInt16(chunk, 14),
                };
            }
            else if (id == "data")
            {
                if (format is null) throw new InvalidDataException("The WAV data chunk comes before its format.");
                var available = (int)Math.Min(size < 0 ? int.MaxValue : size, reader.BaseStream.Length - reader.BaseStream.Position);
                return (format.Value, reader.ReadBytes(available));
            }
            else reader.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
        }
        throw new InvalidDataException("The WAV file has no audio data.");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public uint Loops;
        public nint Next;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MmTime
    {
        public uint Type;
        public uint Value;
        public uint Padding;
    }

    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out nint device, uint deviceId, ref WaveFormat format, nint callback, nint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutPause(nint device);
    [DllImport("winmm.dll")] private static extern uint waveOutRestart(nint device);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(nint device);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(nint device);
    [DllImport("winmm.dll")] private static extern uint waveOutGetPosition(nint device, ref MmTime time, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutSetVolume(nint device, uint volume);
}
