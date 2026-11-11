namespace WinTemp;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // WinTemp.exe --dump <file>: scrive una lettura dei sensori su file ed esce (diagnostica)
        if (args.Length == 2 && args[0] == "--dump")
        {
            Dump(args[1]);
            return;
        }

        using var mutex = new Mutex(true, @"Global\WinTemp.SingleInstance", out bool created);
        if (!created)
            return; // già in esecuzione

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }

    private static void Dump(string path)
    {
        using var reader = new SensorReader(TimeSpan.FromSeconds(1));
        var deadline = DateTime.Now.AddSeconds(30);
        while (reader.Current.Time == DateTime.MinValue && DateTime.Now < deadline)
            Thread.Sleep(200);
        Thread.Sleep(1500);

        var s = reader.Current;
        using var w = new StreamWriter(path);
        w.WriteLine($"Headline: {s.HeadlineLabel} {s.HeadlineTemp}");
        foreach (var g in s.Groups)
        {
            w.WriteLine($"[{g.Type}] {g.Title}");
            foreach (var r in g.Rows)
                w.WriteLine($"   {r.Name,-30} {HoverPopup.Format(r.Kind, r.Value),12}   max {r.Max}");
        }

        // anteprima della finestra popup accanto al file di testo
        ApplicationConfiguration.Initialize();
        Render(s, false, null, Path.ChangeExtension(path, ".png"));
        Render(s, true, "Temperatura CPU non disponibile: driver PawnIO non installato.", Path.ChangeExtension(path, ".pinned.png"));
    }

    private static void Render(Snapshot s, bool pinned, string? note, string file)
    {
        using var popup = new HoverPopup { Pinned = pinned };
        popup.SetData(s, note);
        using var bmp = new Bitmap(popup.Width, popup.Height);
        popup.DrawToBitmap(bmp, new Rectangle(Point.Empty, popup.Size));
        bmp.Save(file);
    }
}
