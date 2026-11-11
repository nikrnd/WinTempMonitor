using LibreHardwareMonitor.Hardware;

namespace WinTemp;

public enum RowKind { Temperature, Fan, Load }

public sealed record SensorRow(string Name, float Value, float? Max, RowKind Kind);

public sealed record SensorGroup(string Title, HardwareType Type, IReadOnlyList<SensorRow> Rows);

public sealed record Snapshot(IReadOnlyList<SensorGroup> Groups, float? HeadlineTemp, string HeadlineLabel, DateTime Time)
{
    public static readonly Snapshot Empty = new(Array.Empty<SensorGroup>(), null, "", DateTime.MinValue);
}

/// <summary>Legge i sensori in un thread in background e pubblica istantanee immutabili.</summary>
public sealed class SensorReader : IDisposable
{
    private readonly Computer _computer;
    private readonly System.Threading.Timer _timer;
    private readonly object _lock = new();
    private volatile Snapshot _snapshot = Snapshot.Empty;
    private bool _disposed;

    public Snapshot Current => _snapshot;
    public event Action? Updated;

    public SensorReader(TimeSpan interval)
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = true,
            IsPsuEnabled = true,
            IsBatteryEnabled = true,
        };
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        Task.Run(() =>
        {
            lock (_lock)
            {
                try { _computer.Open(); } catch { /* alcuni sensori possono non essere disponibili */ }
            }
            Tick();
            _timer.Change(interval, interval);
        });
    }

    private void Tick()
    {
        if (!Monitor.TryEnter(_lock))
            return; // aggiornamento precedente ancora in corso
        try
        {
            if (_disposed) return;
            var groups = new List<SensorGroup>();
            foreach (var hw in _computer.Hardware)
                Collect(hw, hw.HardwareType, groups);

            groups.Sort((a, b) => Order(a.Type).CompareTo(Order(b.Type)));
            var (temp, label) = Headline(groups);
            _snapshot = new Snapshot(groups, temp, label, DateTime.Now);
        }
        catch
        {
            // mantieni l'ultima istantanea valida
        }
        finally
        {
            Monitor.Exit(_lock);
        }
        Updated?.Invoke();
    }

    private static void Collect(IHardware hw, HardwareType rootType, List<SensorGroup> groups)
    {
        try { hw.Update(); } catch { }

        var rows = new List<SensorRow>();
        foreach (var s in hw.Sensors)
        {
            if (s.Value is not float v || float.IsNaN(v)) continue;
            switch (s.SensorType)
            {
                case SensorType.Temperature:
                    // scarta letture palesemente non valide (sensori scollegati)
                    if (v <= -50 || v >= 150) continue;
                    // le voci "Distance to TjMax" non sono temperature reali
                    if (s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase)) continue;
                    rows.Add(new SensorRow(s.Name, v, s.Max, RowKind.Temperature));
                    break;
                case SensorType.Fan:
                    rows.Add(new SensorRow(s.Name, v, s.Max, RowKind.Fan));
                    break;
                case SensorType.Load when IsMainLoad(hw.HardwareType, s.Name) && !rows.Any(r => r.Kind == RowKind.Load):
                    var label = hw.HardwareType switch
                    {
                        HardwareType.Cpu => "Utilizzo CPU",
                        HardwareType.Memory => "Memoria in uso",
                        _ => "Utilizzo GPU",
                    };
                    rows.Add(new SensorRow(label, v, s.Max, RowKind.Load));
                    break;
            }
        }

        if (rows.Any(r => r.Kind != RowKind.Load))
        {
            rows.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : 0);
            groups.Add(new SensorGroup(hw.Name, rootType, rows));
        }

        foreach (var sub in hw.SubHardware)
            Collect(sub, rootType, groups);
    }

    private static bool IsMainLoad(HardwareType type, string name) => type switch
    {
        HardwareType.Cpu => name == "CPU Total",
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => name == "GPU Core" || name == "D3D 3D",
        HardwareType.Memory => name == "Memory",
        _ => false,
    };

    private static (float?, string) Headline(List<SensorGroup> groups)
    {
        static float? Pick(SensorGroup g, params string[] names)
        {
            foreach (var n in names)
            {
                var r = g.Rows.FirstOrDefault(x => x.Kind == RowKind.Temperature && x.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                if (r != null) return r.Value;
            }
            var temps = g.Rows.Where(x => x.Kind == RowKind.Temperature).ToList();
            return temps.Count > 0 ? temps.Max(x => x.Value) : null;
        }

        var cpu = groups.FirstOrDefault(g => g.Type == HardwareType.Cpu && g.Rows.Any(r => r.Kind == RowKind.Temperature));
        if (cpu != null)
            return (Pick(cpu, "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Core Average", "Core Max"), "CPU");

        var all = groups.SelectMany(g => g.Rows).Where(r => r.Kind == RowKind.Temperature).ToList();
        return all.Count > 0 ? (all.Max(r => r.Value), "più alta") : (null, "");
    }

    private static int Order(HardwareType t) => t switch
    {
        HardwareType.Cpu => 0,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => 1,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => 2,
        HardwareType.Memory => 3,
        HardwareType.Storage => 4,
        _ => 5,
    };

    public void Dispose()
    {
        _timer.Dispose();
        lock (_lock)
        {
            _disposed = true;
            try { _computer.Close(); } catch { }
        }
    }
}
