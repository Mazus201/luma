using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed class ToggleSwitch : Control
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 15 };
    private float position;
    private bool hovered;
    private bool pressed;

    public event EventHandler? CheckedChanged;
    public bool Checked { get; private set; }

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(278, 42);
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
        AccessibleName = "Подсветка";
        AccessibleDefaultActionDescription = "Переключить подсветку";
        animation.Tick += Animate;
        Disposed += (_, _) => animation.Dispose();
        UpdateAccessibleState();
    }

    public void SetChecked(bool value, bool animateChange = false)
    {
        var changed = Checked != value;
        Checked = value;
        UpdateAccessibleState();
        if (animateChange) animation.Start();
        else
        {
            position = value ? 1f : 0f;
            Invalidate();
        }
        if (changed) CheckedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateAccessibleState()
    {
        AccessibleName = "Подсветка";
        AccessibleDescription = Checked ? "Состояние: включена" : "Состояние: выключена";
    }

    private void Animate(object? sender, EventArgs e)
    {
        var target = Checked ? 1f : 0f;
        position += Math.Sign(target - position) * Math.Min(.16f, Math.Abs(target - position));
        Invalidate();
        if (Math.Abs(target - position) >= .01f) return;
        position = target;
        animation.Stop();
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ToggleAccessibleObject(this);

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            pressed = true;
            Focus();
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var activate = e.Button == MouseButtons.Left && pressed && ClientRectangle.Contains(e.Location);
        pressed = false;
        Invalidate();
        if (activate) SetChecked(!Checked, animateChange: true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            SetChecked(!Checked, animateChange: true);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(e.Graphics, "Подсветка", Font, new Point(0, 12), ForeColor,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

        var stateText = Checked ? "Включена" : "Выключена";
        using var stateFont = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(e.Graphics, stateText, stateFont, new Point(100, 13),
            Checked ? Color.FromArgb(150, 185, 255) : Color.FromArgb(160, 165, 176),
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

        const float trackWidth = 42;
        const float trackHeight = 26;
        var x = Width - 48;
        const float y = 8;
        var trackColor = Checked ? Color.FromArgb(78, 137, 246)
            : pressed ? Color.FromArgb(48, 51, 58)
            : hovered ? Color.FromArgb(77, 80, 88)
            : Color.FromArgb(59, 62, 70);
        using var track = new SolidBrush(trackColor);
        using var trackPath = Rounded(x, y, trackWidth, trackHeight, trackHeight / 2);
        e.Graphics.FillPath(track, trackPath);

        var knobX = x + 3 + position * 16;
        using var knob = new SolidBrush(Color.White);
        e.Graphics.FillEllipse(knob, knobX, y + 3, 20, 20);
        if (Focused)
        {
            using var focus = new Pen(Color.FromArgb(145, 180, 255), 1);
            using var focusPath = Rounded(x - 2, y - 2, trackWidth + 4, trackHeight + 4, trackHeight / 2 + 2);
            e.Graphics.DrawPath(focus, focusPath);
        }
    }

    private static GraphicsPath Rounded(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class ToggleAccessibleObject(ToggleSwitch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);
        public override void DoDefaultAction() => owner.SetChecked(!owner.Checked, animateChange: true);
    }
}

internal sealed class FluentSlider : Control
{
    private bool dragging;
    private int value;

    public event EventHandler? ValueChanged;
    public int Minimum { get; set; } = 0;
    public int Maximum { get; set; } = 100;

    public int Value
    {
        get => value;
        set
        {
            var next = Math.Clamp(value, Minimum, Maximum);
            if (this.value == next) return;
            this.value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public FluentSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(278, 32);
        TabStop = true;
        BackColor = Color.FromArgb(29, 30, 34);
        AccessibleRole = AccessibleRole.Slider;
        AccessibleName = "Яркость";
        AccessibleDefaultActionDescription = "Изменить яркость";
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibleObject(this);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        dragging = true;
        Capture = true;
        Focus();
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        SetFromX(e.X);
        dragging = false;
        Capture = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = e.Shift ? 10 : 1;
        if (e.KeyCode == Keys.Left) Value -= step;
        else if (e.KeyCode == Keys.Right) Value += step;
        else if (e.KeyCode == Keys.Home) Value = Minimum;
        else if (e.KeyCode == Keys.End) Value = Maximum;
        else
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        const float left = 9;
        var right = Width - 9;
        var centerY = Height / 2f;
        var radius = Math.Max(1, right - left);
        var ratio = Maximum == Minimum ? 0 : (float)(Value - Minimum) / (Maximum - Minimum);
        var thumbX = left + ratio * radius;

        using var inactive = new Pen(Color.FromArgb(68, 71, 80), 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var active = new Pen(Color.FromArgb(78, 137, 246), 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(inactive, left, centerY, right, centerY);
        if (thumbX > left) e.Graphics.DrawLine(active, left, centerY, thumbX, centerY);

        using var knob = new SolidBrush(Color.FromArgb(78, 137, 246));
        using var rim = new Pen(Color.FromArgb(235, 241, 252), 1.5f);
        e.Graphics.FillEllipse(knob, thumbX - 7, centerY - 7, 14, 14);
        e.Graphics.DrawEllipse(rim, thumbX - 7, centerY - 7, 14, 14);
        if (Focused)
        {
            using var focus = new Pen(Color.FromArgb(90, 145, 250), 1);
            e.Graphics.DrawRectangle(focus, 1, 1, Width - 3, Height - 3);
        }
    }

    private void SetFromX(int x)
    {
        const float left = 9;
        var right = Width - 9;
        Value = Minimum + (int)Math.Round(
            Math.Clamp((x - left) / (right - left), 0f, 1f) * (Maximum - Minimum));
    }

    private sealed class SliderAccessibleObject(FluentSlider owner) : ControlAccessibleObject(owner)
    {
        public override string? Value
        {
            get => owner.Value.ToString();
            set
            {
                if (int.TryParse(value, out var parsed)) owner.Value = parsed;
            }
        }
    }
}

internal sealed class ColorPresetButton : Control
{
    private bool hovered;
    private bool pressed;

    public string Title { get; set; } = "Цвет";
    public Color SwatchColor { get; set; } = Color.RoyalBlue;
    public bool Selected { get; set; }
    public event EventHandler? Clicked;

    public ColorPresetButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Size = new Size(58, 30);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleDefaultActionDescription = "Применить цветовой пресет";
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            pressed = true;
            Focus();
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var activate = e.Button == MouseButtons.Left && pressed && ClientRectangle.Contains(e.Location);
        pressed = false;
        Invalidate();
        if (activate) Clicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            pressed = true;
            Invalidate();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if ((e.KeyCode is Keys.Space or Keys.Enter) && pressed)
        {
            pressed = false;
            Invalidate();
            Clicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new RectangleF(1, 1, Width - 3, Height - 3);
        using var path = Rounded(box, 7f);
        var fillColor = pressed ? Color.FromArgb(31, 34, 41)
            : hovered ? Color.FromArgb(52, 55, 63)
            : Color.FromArgb(39, 41, 47);
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(Selected ? Color.FromArgb(112, 158, 255) : Color.FromArgb(65, 68, 76), Selected ? 1.5f : 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        using var dot = new SolidBrush(SwatchColor);
        using var dotEdge = new Pen(Color.FromArgb(135, 145, 165), .6f);
        e.Graphics.FillEllipse(dot, 7, 11, 8, 8);
        e.Graphics.DrawEllipse(dotEdge, 7, 11, 8, 8);
        TextRenderer.DrawText(e.Graphics, Title, Font, new Point(19, 8), Color.White,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused)
        {
            using var focus = new Pen(Color.FromArgb(145, 180, 255), 1);
            e.Graphics.DrawRectangle(focus, 1, 1, Width - 3, Height - 3);
        }
    }

    private static GraphicsPath Rounded(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
