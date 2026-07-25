using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Switcher.App.Rendering;
using Switcher.Contracts;
using Brush = System.Windows.Media.Brush;

namespace Switcher.App.ViewModels;

/// <summary>
/// One program bus's audio output device.
///
/// Each bus gets its own row because the buses are independent: PGM1 can feed front-of-house while PGM2
/// feeds a stream encoder. The row is coloured with that bus's tally colour so the audio routing reads
/// the same way as everything else on the console.
/// </summary>
public sealed class AudioOutputRowViewModel : INotifyPropertyChanged
{
    /// <summary>Placeholder for "don't play this bus anywhere", so the list can express silence.</summary>
    public static readonly AudioDeviceInfo NoDevice = new(string.Empty, "(no audio output)", false);

    private AudioDeviceInfo? _selectedDevice;

    public AudioOutputRowViewModel(ProgramBus bus)
    {
        Bus = bus;
        Label = bus == ProgramBus.Pgm2 ? "PGM2" : "PGM1";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProgramBus Bus { get; }

    public string Label { get; }

    /// <summary>Tally colour of this bus, so the row is identifiable at a glance.</summary>
    public Brush BusBrush => TallyPalette.ProgramBrush(Bus);

    public ObservableCollection<AudioDeviceInfo> Devices { get; } = [];

    public AudioDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value))
            {
                return;
            }

            _selectedDevice = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Rebuilds the device list, keeping the current selection if that device still exists.</summary>
    public void SyncDevices(IReadOnlyList<AudioDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var previousId = SelectedDevice?.Id;

        Devices.Clear();
        Devices.Add(NoDevice);
        foreach (var device in devices)
        {
            Devices.Add(device);
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Id == previousId) ?? NoDevice;
    }

    /// <summary>Selects the device with this id, falling back to "no output" when it has gone away
    /// (a USB interface unplugged since the routing was saved, say).</summary>
    public void Select(string deviceId) =>
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == deviceId) ?? NoDevice;

    /// <summary>The assignment for this row, or <c>null</c> when the bus is routed nowhere.</summary>
    public AudioOutputAssignment? ToAssignment() =>
        SelectedDevice is null || ReferenceEquals(SelectedDevice, NoDevice) || SelectedDevice.Id.Length == 0
            ? null
            : new AudioOutputAssignment(Bus, SelectedDevice.Id, SelectedDevice.Name);

    public void RaiseBusBrushChanged() => OnPropertyChanged(nameof(BusBrush));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
