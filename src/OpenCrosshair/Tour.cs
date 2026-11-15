using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static OpenCrosshair.Lang;

namespace OpenCrosshair;

/// <summary>
/// One stop of the tour. <see cref="Target"/> gives the screen area to highlight (null: none, the card is centered).
/// Steps with <see cref="Done"/> wait for you to try the highlighted part.
/// </summary>
internal sealed record TourStep(string Title, string Text, Func<Rectangle?>? Target = null, Func<bool>? Done = null, Action? Enter = null);

/// <summary>
/// A skippable guided tour of a window. Everything is dimmed except the part being explained; the spotlight
/// glides from part to part and pulses, and you can click through it to use that part ("Try it" steps move on
/// by themselves once you do). A card next to the spotlight explains it, with Back, Next and Skip.
/// </summary>
internal sealed class Tour : IDisposable
{
    private const double MoveSeconds = 0.35, FadeSeconds = 0.25, PraiseSeconds = 0.9;

    private readonly Form _owner;
    private readonly IReadOnlyList<TourStep> _steps;
    private readonly OverlayWindow _shade = new(clickThrough: false);
    private readonly Card _card = new();
    private readonly Label _counter = new() { AutoSize = true, Tag = "subtle" };
    private readonly Label _title = new() { AutoSize = true, MaximumSize = new Size(340, 0), Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(3, 2, 3, 6) };
    private readonly Label _text = new() { AutoSize = true, MinimumSize = new Size(340, 0), MaximumSize = new Size(340, 0) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(340, 0), Margin = new Padding(3, 8, 3, 0) };
    private readonly Button _back = new() { AutoSize = true }, _next = new() { AutoSize = true, Tag = "keep" }, _skip = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Bitmap? _buffer;
    private Rectangle _hole, _holeFrom;
    private Point _cardFrom;
    private double _movedAt, _doneAt = double.NaN, _closingAt = double.NaN;
    private int _index = -1;
    private bool _disposed, _cardClosed;

    /// <summary>Raised once, when the tour has finished, was skipped or its window closed.</summary>
    public event Action? Ended;

    public int Step => _index;

    /// <summary>For pictures of the tour: the dim layer as last drawn, and the card.</summary>
    internal (Bitmap? Shade, Form Card) Parts => (_buffer, _card);

    public Tour(Form owner, IReadOnlyList<TourStep> steps)
    {
        _owner = owner;
        _steps = steps;
        _card.Font = owner.Font;
        _back.Text = T("Back");
        _skip.Text = T("Skip tour");
        _back.Click += (_, _) => Back();
        _next.Click += (_, _) => Next();
        _skip.Click += (_, _) => Skip();
        _card.AcceptButton = _next;
        _card.CancelButton = _skip;   // Esc skips

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange([_next, _back, _skip]);
        var inner = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(14), Location = new Point(2, 2),
            BackColor = Theme.Back,   // else it would take on the card's accent color
        };
        inner.Controls.AddRange([_counter, _title, _text, _status, buttons]);
        _card.Controls.Add(inner);
        _card.Tag = "keep";   // its accent color is the border around the inner panel
        _card.BackColor = Theme.Accent;
        Theme.Style(_card);
        _counter.ForeColor = _status.ForeColor = Theme.Subtle;
        _next.FlatStyle = FlatStyle.Flat;   // the main button stands out in both themes
        _next.FlatAppearance.BorderColor = _next.BackColor = Theme.Accent;
        _next.ForeColor = Color.White;
        inner.SizeChanged += (_, _) => _card.ClientSize = inner.Size + new Size(4, 4);
        _card.FormClosed += (_, _) =>   // Alt+F4, or the window it belongs to closed
        {
            _cardClosed = true;
            Dispose();
        };

        _shade.Owner = owner;
        _card.Owner = _shade;   // owned windows stay above their owner: window, then shade, then card
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        var area = Area();
        _hole = _holeFrom = new Rectangle(area.X + area.Width / 2, area.Y + area.Height / 2, 0, 0);   // grows out of the middle
        _shade.Show();
        _card.Location = new Point(-32000, -32000);   // placed by the first tick, once its size is known
        _card.Show();
        if (Form.ActiveForm == _owner) _card.Activate();   // keyboard (Enter, Esc) goes to the card
        Go(0);
        _timer.Start();
        Tick();
    }

    internal void Next() => Go(_index + 1);
    internal void Back() => Go(Math.Max(0, _index - 1));

    internal void Skip()
    {
        if (double.IsNaN(_closingAt)) _closingAt = Now;   // fades out, then ends
    }

    private double Now => _clock.Elapsed.TotalSeconds;

    private void Go(int index)
    {
        if (_disposed || !double.IsNaN(_closingAt)) return;
        if (index >= _steps.Count)
        {
            Skip();
            return;
        }
        _index = index;
        var step = _steps[index];
        step.Enter?.Invoke();
        _counter.Text = T("Step {0} of {1}", index + 1, _steps.Count);
        _title.Text = step.Title;
        _text.Text = step.Text;
        _status.Text = step.Done is null ? "" : T("Your turn! Or press Next to skip this step.");
        _back.Visible = index > 0;
        _skip.Visible = index < _steps.Count - 1;
        _next.Text = index == _steps.Count - 1 ? T("Finish") : T("Next");
        _card.AccessibleName = step.Title;
        _holeFrom = _hole;
        _cardFrom = _card.Location;
        _movedAt = Now;
        _doneAt = double.NaN;
    }

    private void Tick()
    {
        if (_disposed) return;
        double now = Now;
        var step = _steps[_index];
        if (step.Done is { } done && double.IsNaN(_doneAt) && done())
        {
            _doneAt = now;
            _status.Text = "✓ " + T("Nice!");
        }
        if (!double.IsNaN(_doneAt) && now - _doneAt > PraiseSeconds)
        {
            Next();
            return;
        }

        double fade = Math.Min(1, now / FadeSeconds);
        if (!double.IsNaN(_closingAt)) fade = Math.Max(0, 1 - (now - _closingAt) / FadeSeconds);
        if (fade <= 0)
        {
            Dispose();
            return;
        }

        var area = Area();
        var target = step.Target?.Invoke() is { } spotlight
            ? Rectangle.Inflate(spotlight, 6, 6)
            : new Rectangle(area.X + area.Width / 2, area.Y + area.Height / 2, 0, 0);
        double k = Ease(Math.Min(1, (now - _movedAt) / MoveSeconds));
        _hole = Lerp(_holeFrom, target, k);
        var spot = CardSpot(target, _card.Size, area);
        _card.Location = _cardFrom.X == -32000 ? spot : Lerp(new Rectangle(_cardFrom, Size.Empty), new Rectangle(spot, Size.Empty), k).Location;
        _card.Opacity = fade;
        Paint(now, fade, waiting: step.Done is not null && double.IsNaN(_doneAt));
    }

    /// <summary>The dim layer: dark everywhere but the spotlight, which is fully clear so clicks reach the control below.</summary>
    private void Paint(double now, double fade, bool waiting)
    {
        var bounds = _owner.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (_buffer?.Size != bounds.Size)
        {
            _buffer?.Dispose();
            _buffer = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        }
        using (var g = Graphics.FromImage(_buffer))
        {
            g.Clear(Color.FromArgb(175, 0, 0, 0));
            if (_hole.Width > 0 && _hole.Height > 0)
            {
                var hole = _hole with { X = _hole.X - bounds.X, Y = _hole.Y - bounds.Y };
                using (var path = Rounded(hole, 8))
                using (var clear = new SolidBrush(Color.Transparent))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.FillPath(clear, path);
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var glow = Color.FromArgb(80, 160, 255);
                    int pulse = (int)(150 + 100 * Math.Sin(now * (waiting ? 7 : 4)));
                    using var ring = new Pen(Color.FromArgb(pulse, glow), waiting ? 3.5f : 2.5f);
                    g.DrawPath(ring, path);
                }
                if (waiting)
                {
                    // "Click here": a ring that keeps rippling outwards from the spotlight.
                    double p = now * 0.9 % 1;
                    int grow = (int)(p * 18);
                    using var ripple = Rounded(Rectangle.Inflate(hole, grow, grow), 8 + grow);
                    using var pen = new Pen(Color.FromArgb((int)((1 - p) * 200), 80, 160, 255), 2f);
                    g.DrawPath(pen, ripple);
                }
            }
        }
        _shade.SetImage(_buffer, (byte)(255 * fade), bounds);
    }

    private Rectangle Area() => _owner.RectangleToScreen(_owner.ClientRectangle);

    /// <summary>Next to the spotlight (below, right, left, above: the first that fits in the window), or centered.</summary>
    internal static Point CardSpot(Rectangle hole, Size card, Rectangle area)
    {
        const int gap = 16;
        Point spot;
        if (hole.Width == 0) spot = new Point(area.X + (area.Width - card.Width) / 2, area.Y + (area.Height - card.Height) / 2);
        else
        {
            Point[] tries =
            [
                new(hole.Left, hole.Bottom + gap), new(hole.Right + gap, hole.Top),
                new(hole.Left - gap - card.Width, hole.Top), new(hole.Left, hole.Top - gap - card.Height),
            ];
            spot = tries.FirstOrDefault(p => area.Contains(new Rectangle(p, card)), tries[0]);
        }
        spot = Clamp(spot, card, area);
        // Keep it on the screen too, unless the window itself is off-screen.
        var screen = Screen.FromRectangle(area).WorkingArea;
        return screen.IntersectsWith(area) ? Clamp(spot, card, screen) : spot;
    }

    private static Point Clamp(Point p, Size size, Rectangle area) => new(
        Math.Max(area.Left, Math.Min(p.X, area.Right - size.Width)),
        Math.Max(area.Top, Math.Min(p.Y, area.Bottom - size.Height)));

    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);   // fast start, gentle stop

    private static Rectangle Lerp(Rectangle a, Rectangle b, double t)
    {
        int L(int x, int y) => (int)Math.Round(x + (y - x) * t);
        return Rectangle.FromLTRB(L(a.Left, b.Left), L(a.Top, b.Top), L(a.Right, b.Right), L(a.Bottom, b.Bottom));
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = Math.Max(1, Math.Min(2 * radius, Math.Min(r.Width, r.Height)));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        if (!_cardClosed) _card.Close();   // closing a shown window also disposes it
        _shade.Close();
        _buffer?.Dispose();
        Ended?.Invoke();
    }

    /// <summary>The explanation card: a borderless window that doesn't steal focus when it appears.</summary>
    private sealed class Card : Form
    {
        public Card()
        {
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Text = "OpenCrosshair";
        }

        protected override bool ShowWithoutActivation => true;
    }
}
