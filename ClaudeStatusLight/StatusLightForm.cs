using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace ClaudeStatusLight;

public sealed class StatusLightForm : Form
{
    private const int Diameter = 48;
    private static readonly Color TransparentColor = Color.FromArgb(255, 1, 2, 3);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeStatusLight");

    private static readonly string StatusFile = Path.Combine(DataDir, "status.json");
    private static readonly string PositionFile = Path.Combine(DataDir, "position.json");

    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 500 };
    private readonly ToolTip _toolTip = new();

    private Point _dragStart;
    private bool _dragging;

    private string _currentStatus = string.Empty;
    private Color _currentColor = Color.Gray;

    public StatusLightForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(Diameter, Diameter);
        BackColor = TransparentColor;
        TransparencyKey = TransparentColor;
        DoubleBuffered = true;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

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
        return new Point(wa.Right - Diameter - 24, 24);
    }

    private static Point ClampToWorkingArea(Point location)
    {
        var wa = Screen.GetWorkingArea(location);
        var x = Math.Clamp(location.X, wa.Left, Math.Max(wa.Left, wa.Right - Diameter));
        var y = Math.Clamp(location.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - Diameter));
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
        var status = "idle";
        DateTime? updatedUtc = null;

        try
        {
            if (File.Exists(StatusFile))
            {
                var json = File.ReadAllText(StatusFile);
                var data = JsonSerializer.Deserialize<StatusPayload>(json);
                if (data is not null)
                {
                    status = data.status ?? "idle";
                    if (DateTime.TryParse(data.updated, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var dt))
                    {
                        updatedUtc = dt.ToUniversalTime();
                    }
                }
            }
        }
        catch (IOException)
        {
            return; // hook script is mid-write, just retry next tick
        }
        catch
        {
            status = "idle";
        }

        // If any non-idle status hasn't been refreshed in a while (session killed, crash,
        // laptop slept mid-task), fall back to idle rather than showing a stale state forever.
        if (updatedUtc is not null && DateTime.UtcNow - updatedUtc.Value > StaleAfter)
        {
            status = "idle";
        }

        if (status == _currentStatus) return;

        _currentStatus = status;
        _currentColor = ColorForStatus(status);
        _toolTip.SetToolTip(this, TooltipForStatus(status));
        Invalidate();
    }

    private static Color ColorForStatus(string status) => status switch
    {
        "waiting" => Color.FromArgb(230, 60, 60),
        "running" => Color.FromArgb(255, 190, 30),
        "done" => Color.FromArgb(45, 200, 110),
        _ => Color.FromArgb(130, 130, 130), // idle / no session
    };

    private static string TooltipForStatus(string status) => status switch
    {
        "waiting" => "Claude Code: waiting for confirmation",
        "running" => "Claude Code: running",
        "done" => "Claude Code: finished, ready for a new task",
        _ => "Claude Code: no active session",
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(4, 4, Diameter - 8, Diameter - 8);

        using var fill = new SolidBrush(_currentColor);
        g.FillEllipse(fill, rect);

        using var outline = new Pen(Color.FromArgb(60, 0, 0, 0), 2f);
        g.DrawEllipse(outline, rect);
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

    private sealed class StatusPayload
    {
        public string? status { get; set; }
        public string? updated { get; set; }
    }

    private sealed class SavedPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
