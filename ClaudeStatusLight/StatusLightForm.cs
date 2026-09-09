using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace ClaudeStatusLight;

/// <summary>One row in the panel: a session's status dot plus its label.</summary>
internal sealed record SessionRow(string Label, Color Color);

/// <summary>
/// Win32 SetWindowPos, used to re-assert the topmost z-order band on every poll tick.
/// WinForms' own TopMost property only sets this once; it doesn't self-heal if something
/// else (another topmost window claiming the band, an external tool moving/z-ordering this
/// window) knocks it back down.
/// </summary>
internal static class NativeMethods
{
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);
}

public sealed class StatusLightForm : Form
{
    private const int DotDiameter = 12;
    private const int RowHeight = 28;
    private const int PaddingX = 10;
    private const int PaddingY = 6;
    private const int LabelGap = 8;
    private const int MinWidth = 140;
    private const int MaxWidth = 320;
    private const int CornerRadius = 10;

    private static readonly Color BackgroundColor = Color.FromArgb(30, 32, 36);
    private static readonly Color BorderColor = Color.FromArgb(60, 62, 68);
    private static readonly Color LabelColor = Color.FromArgb(225, 226, 230);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeStatusLight");

    private static readonly string SessionsDir = Path.Combine(DataDir, "sessions");
    private static readonly string PositionFile = Path.Combine(DataDir, "position.json");
    private static readonly string AliasesFile = Path.Combine(DataDir, "aliases.json");

    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 500 };
    private readonly Font _labelFont = CreateLabelFont();

    private Point _dragStart;
    private bool _dragging;
    private string _lastSignature = string.Empty;
    private List<SessionRow> _rows = new();
    private Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _aliasesLastWriteUtc = DateTime.MinValue;

    public StatusLightForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = BackgroundColor;
        DoubleBuffered = true;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        Size = new Size(MinWidth, PaddingY * 2 + RowHeight);
        ApplyRoundedRegion();
        Location = ClampToWorkingArea(LoadPosition() ?? DefaultPosition());

        var menu = new ContextMenuStrip();
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Application.Exit();
        menu.Items.Add(exitItem);
        ContextMenuStrip = menu;

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;

        _pollTimer.Tick += (_, _) => PollStatus();
        _pollTimer.Start();

        PollStatus();
    }

    private static Point DefaultPosition()
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        return new Point(wa.Right - MinWidth - 24, 24);
    }

    /// <summary>
    /// JetBrains Mono if it's installed, otherwise Segoe UI. System.Drawing's Font
    /// constructor doesn't throw for an unknown family, it silently substitutes a generic
    /// GDI default -- checking FontFamily.Families first gets a deliberate, readable
    /// fallback instead of leaving that to chance.
    /// </summary>
    private static Font CreateLabelFont()
    {
        var hasJetBrainsMono = FontFamily.Families
            .Any(f => f.Name.Equals("JetBrains Mono", StringComparison.OrdinalIgnoreCase));
        return new Font(hasJetBrainsMono ? "JetBrains Mono" : "Segoe UI", 9f);
    }

    private Point ClampToWorkingArea(Point location)
    {
        var wa = Screen.GetWorkingArea(location);
        var x = Math.Clamp(location.X, wa.Left, Math.Max(wa.Left, wa.Right - Width));
        var y = Math.Clamp(location.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
        return new Point(x, y);
    }

    private static Point? LoadPosition()
    {
        try
        {
            if (!File.Exists(PositionFile)) return null;
            var json = File.ReadAllText(PositionFile);
            var pos = JsonSerializer.Deserialize<SavedPosition>(json);
            return pos is null ? null : new Point(pos.X, pos.Y);
        }
        catch
        {
            return null;
        }
    }

    private void SavePosition()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var pos = new SavedPosition { X = Location.X, Y = Location.Y };
            File.WriteAllText(PositionFile, JsonSerializer.Serialize(pos));
        }
        catch
        {
            // best effort, position just won't persist
        }
    }

    private void PollStatus()
    {
        // Re-assert topmost every tick: another app's own topmost window, or an external
        // tool repositioning us via SetWindowPos with a non-topmost z-order reference, can
        // silently knock this window out of the topmost band without ever touching the
        // TopMost property's cached value -- so setting it once at startup isn't durable.
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        var sessions = ReadActiveSessions();

        var rows = sessions.Count == 0
            ? new List<SessionRow> { new("No active session", ColorForStatus("idle")) }
            : sessions.Select(s => new SessionRow(s.Label, ColorForStatus(s.Status))).ToList();

        var signature = string.Join("|", rows.Select(r => $"{r.Label}:{r.Color.ToArgb()}"));
        if (signature == _lastSignature) return;
        _lastSignature = signature;
        _rows = rows;

        Size = new Size(ComputeWidth(rows), PaddingY * 2 + RowHeight * rows.Count);
        ApplyRoundedRegion();
        Location = ClampToWorkingArea(Location);
        Invalidate();
    }

    private int ComputeWidth(List<SessionRow> rows)
    {
        using var g = CreateGraphics();
        var maxLabelWidth = rows.Count == 0
            ? 0
            : rows.Max(r => TextRenderer.MeasureText(g, r.Label, _labelFont).Width);
        var width = PaddingX + DotDiameter + LabelGap + maxLabelWidth + PaddingX;
        return Math.Clamp(width, MinWidth, MaxWidth);
    }

    private sealed record ActiveSession(string Status, string Label);

    private List<ActiveSession> ReadActiveSessions()
    {
        LoadAliasesIfChanged();

        var result = new List<ActiveSession>();
        var labelCounts = new Dictionary<string, int>();

        if (!Directory.Exists(SessionsDir)) return result;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(SessionsDir, "*.json").OrderBy(f => f, StringComparer.Ordinal);
        }
        catch
        {
            return result;
        }

        foreach (var file in files)
        {
            SessionPayload? data;
            try
            {
                var json = File.ReadAllText(file);
                data = JsonSerializer.Deserialize<SessionPayload>(json);
            }
            catch (IOException)
            {
                continue; // hook script is mid-write, just retry next tick
            }
            catch
            {
                continue; // corrupt/partial file, skip it
            }

            if (data?.status is null) continue;

            DateTime? updatedUtc = null;
            if (data.updated is not null &&
                DateTime.TryParse(data.updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            {
                updatedUtc = dt.ToUniversalTime();
            }

            if (updatedUtc is null || DateTime.UtcNow - updatedUtc.Value > StaleAfter)
            {
                // Session crashed or ended without firing SessionEnd; stop showing it.
                TryDelete(file);
                continue;
            }

            var id = Path.GetFileNameWithoutExtension(file);
            var baseLabel = FriendlyName(data.cwd, id, _aliases);
            labelCounts[baseLabel] = labelCounts.GetValueOrDefault(baseLabel) + 1;
            result.Add(new ActiveSession(data.status, baseLabel));
        }

        // Disambiguate sessions that share a folder name (e.g. worktrees) with a numeric suffix.
        var seen = new Dictionary<string, int>();
        for (var i = 0; i < result.Count; i++)
        {
            var row = result[i];
            if (labelCounts[row.Label] <= 1) continue;
            var n = seen[row.Label] = seen.GetValueOrDefault(row.Label) + 1;
            result[i] = row with { Label = $"{row.Label} ({n})" };
        }

        return result;
    }

    private static string FriendlyName(string? cwd, string sessionId, IReadOnlyDictionary<string, string> aliases)
    {
        string? folderName = null;
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            var name = Path.GetFileName(cwd.TrimEnd('\\', '/'));
            if (!string.IsNullOrWhiteSpace(name)) folderName = name;
        }

        // aliases.json maps folder names only -- never consult it for the sessionId fallback
        // below, so an alias key can't accidentally rename a session with no usable cwd.
        if (folderName is not null)
        {
            return aliases.TryGetValue(folderName, out var alias) && !string.IsNullOrWhiteSpace(alias)
                ? alias
                : folderName;
        }

        return sessionId.Length > 8 ? sessionId[..8] : sessionId;
    }

    /// <summary>
    /// Loads %LOCALAPPDATA%\ClaudeStatusLight\aliases.json, a flat { "folder-name": "Label" }
    /// map for renaming rows in the panel. Optional -- absent or malformed just means no
    /// aliases apply. Re-read only when the file's mtime changes, so hand-editing it while the
    /// app is running takes effect within one poll tick, without re-parsing every tick.
    /// </summary>
    private void LoadAliasesIfChanged()
    {
        if (!File.Exists(AliasesFile))
        {
            if (_aliases.Count > 0) _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _aliasesLastWriteUtc = DateTime.MinValue;
            return;
        }

        DateTime writeTimeUtc;
        try
        {
            writeTimeUtc = File.GetLastWriteTimeUtc(AliasesFile);
        }
        catch
        {
            return; // transient I/O issue, retry next tick
        }
        if (writeTimeUtc == _aliasesLastWriteUtc) return;

        // Mark this mtime as attempted *before* parsing, even if parsing below fails, so a
        // permanently malformed file gets re-parsed only when its mtime next changes rather
        // than on every 500ms poll tick.
        _aliasesLastWriteUtc = writeTimeUtc;
        try
        {
            var json = File.ReadAllText(AliasesFile);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            _aliases = parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Malformed aliases.json -- keep the last good mapping rather than losing labels.
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort cleanup */ }
    }

    private static Color ColorForStatus(string status) => status switch
    {
        "waiting" => Color.FromArgb(230, 60, 60),
        "running" => Color.FromArgb(255, 190, 30),
        "done" => Color.FromArgb(45, 200, 110),
        _ => Color.FromArgb(130, 130, 130), // idle
    };

    private void ApplyRoundedRegion()
    {
        using var path = RoundedRectPath(new Rectangle(Point.Empty, Size), CornerRadius);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    private static GraphicsPath RoundedRectPath(Rectangle rect, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var border = new Pen(BorderColor, 1f))
        {
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var borderPath = RoundedRectPath(rect, CornerRadius);
            g.DrawPath(border, borderPath);
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var top = PaddingY + i * RowHeight;
            var dotRect = new Rectangle(PaddingX, top + (RowHeight - DotDiameter) / 2, DotDiameter, DotDiameter);

            using (var fill = new SolidBrush(row.Color))
            {
                g.FillEllipse(fill, dotRect);
            }
            using (var outline = new Pen(Color.FromArgb(70, 0, 0, 0), 1.5f))
            {
                g.DrawEllipse(outline, dotRect);
            }

            var textRect = new Rectangle(
                dotRect.Right + LabelGap, top,
                Width - dotRect.Right - LabelGap - PaddingX, RowHeight);
            TextRenderer.DrawText(g, row.Label, _labelFont, textRect, LabelColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStart = e.Location;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        SavePosition();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Dispose();
            _labelFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class SessionPayload
    {
        public string? status { get; set; }
        public string? updated { get; set; }
        public string? cwd { get; set; }
    }

    private sealed class SavedPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
