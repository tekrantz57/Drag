using System.Globalization;

namespace DragWin;

public sealed record FinishLaneResult(
    int Lane, string Racer, string Car, string Outcome,
    long? ReactionUs, long? ElapsedUs, long? SpeedMphX100,
    int? DialMilliseconds = null, bool IntervalsInstalled = false,
    long? Interval1Us = null, long? Interval2Us = null,
    bool Winner = false, bool Advances = false, bool RedLight = false,
    bool Breakout = false, bool DidNotFinish = false, bool IsBye = false, bool SpeedInvalid = false);

public sealed record PassFinishResult(string Title, bool Simulated, IReadOnlyList<FinishLaneResult> Lanes)
{
    // Compare arrival times on the common race clock, including bracket head starts.
    // A lead is a physical crossing difference, not a decision about legality.
    public string FinishLead()
    {
        if (Lanes.Count != 2 || Lanes.Any(lane => lane.IsBye || lane.DidNotFinish ||
                !lane.ReactionUs.HasValue || !lane.ElapsedUs.HasValue) ||
            Lanes.Select(lane => lane.DialMilliseconds.HasValue).Distinct().Count() != 1)
            return "";

        long Arrival(FinishLaneResult lane) => lane.ReactionUs!.Value + lane.ElapsedUs!.Value -
            (lane.DialMilliseconds.GetValueOrDefault() * 1000L);
        var difference = Arrival(Lanes[0]) - Arrival(Lanes[1]);
        if (difference == 0) return "Finish lead: tied at recorded resolution";
        var leader = difference < 0 ? Lanes[0] : Lanes[1];
        return $"Finish lead: Lane {leader.Lane} by {PassFinishForm.Seconds(Math.Abs(difference))} s";
    }
}

public sealed class PassFinishForm : Form
{
    public PassFinishResult Result { get; }

    public PassFinishForm(PassFinishResult result)
    {
        Result = result;
        Text = "Pass Finish - " + result.Title;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 640);
        Size = new Size(1280, 820);
        BackColor = Color.White;
        ForeColor = Color.FromArgb(24, 29, 33);
        AutoScaleMode = AutoScaleMode.Dpi;

        var lanes = result.Lanes.OrderBy(lane => lane.Lane).ToArray();
        var title = new FitText
        {
            Text = result.Title + (result.Simulated ? " | SIMULATED" : " | CONTROLLER"),
            MaximumFontSize = 22,
            BackColor = Color.FromArgb(30, 37, 42),
            ForeColor = Color.White
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = lanes.Length + 1,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            BackColor = Color.FromArgb(213, 219, 222),
            Margin = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        foreach (var lane in lanes) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / lanes.Length));

        var row = 0;
        void AddRow(string name, Func<FinishLaneResult, string> value, float weight = 1, float fontSize = 28,
            Func<FinishLaneResult, Color>? background = null, Func<FinishLaneResult, Color>? foreground = null)
        {
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.Percent, weight));
            table.Controls.Add(new FitText
            {
                Text = name,
                MaximumFontSize = 17,
                BackColor = Color.FromArgb(238, 241, 243),
                ForeColor = Color.FromArgb(35, 72, 102)
            }, 0, row);
            for (var index = 0; index < lanes.Length; index++)
            {
                var lane = lanes[index];
                table.Controls.Add(new FitText
                {
                    Text = value(lane),
                    MaximumFontSize = fontSize,
                    BackColor = background?.Invoke(lane) ?? Color.White,
                    ForeColor = foreground?.Invoke(lane) ?? ForeColor
                }, index + 1, row);
            }
            row++;
        }

        AddRow("Lane / Racer / Car", lane => string.Join("\n", new[]
            { $"LANE {lane.Lane}", lane.Racer, lane.Car }.Where(text => !string.IsNullOrWhiteSpace(text))),
            2.1F, 25, lane => lane.Winner || lane.Advances ? Color.FromArgb(212, 242, 222) : Color.FromArgb(238, 241, 243));
        AddRow("Result", lane => string.Join("\n", new[]
            { lane.Advances ? "ADVANCES" : "", lane.Outcome.ToUpperInvariant() }.Where(text => text.Length > 0)),
            1.4F, 25, lane => lane.RedLight || lane.DidNotFinish ? Color.FromArgb(255, 222, 224) :
                lane.Breakout ? Color.FromArgb(255, 236, 185) :
                lane.Winner || lane.Advances ? Color.FromArgb(212, 242, 222) : Color.White);
        if (lanes.Any(lane => lane.DialMilliseconds.HasValue))
            AddRow("Dial-In (s)", lane => Seconds(lane.DialMilliseconds * 1000L));
        AddRow("Reaction (s)", lane => Seconds(lane.ReactionUs), foreground: lane =>
            lane.RedLight || lane.ReactionUs < 0 ? Color.FromArgb(170, 22, 35) : ForeColor);
        if (lanes.Any(lane => lane.IntervalsInstalled))
        {
            AddRow("Interval 1 (s)", lane => Interval(lane.Interval1Us, lane.IntervalsInstalled));
            AddRow("Interval 2 (s)", lane => Interval(lane.Interval2Us, lane.IntervalsInstalled));
        }
        AddRow("Elapsed (s)", lane => Seconds(lane.ElapsedUs), 1.4F, 38,
            lane => lane.Breakout ? Color.FromArgb(255, 236, 185) : Color.White);
        if (lanes.Any(lane => lane.DialMilliseconds.HasValue))
            AddRow("ET - Dial (s)", lane => lane.ElapsedUs.HasValue && lane.DialMilliseconds.HasValue
                ? ((lane.ElapsedUs.Value - lane.DialMilliseconds.Value * 1000L) / 1_000_000M)
                    .ToString("+0.0000;-0.0000;0.0000", CultureInfo.CurrentCulture) : "N/A");
        AddRow("MPH", lane => lane.SpeedMphX100.HasValue
            ? (lane.SpeedMphX100.Value / 100M).ToString("0.00", CultureInfo.CurrentCulture)
            : lane.SpeedInvalid ? "Invalid" : "N/A");

        var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(100, 36), Dock = DockStyle.Right };
        UiStyles.ConfigurePrimaryButton(close, UiStyles.BlueAction);
        close.Click += (_, _) => Close();
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        footer.Controls.Add(new FitText { Text = result.FinishLead(), MaximumFontSize = 20 }, 0, 0);
        footer.Controls.Add(close, 1, 0);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        shell.Controls.Add(title, 0, 0);
        shell.Controls.Add(table, 0, 1);
        shell.Controls.Add(footer, 0, 2);
        shell.Controls.Add(new FitText
        {
            Text = "Drag " + BuildIdentity.Current,
            MaximumFontSize = 10,
            ForeColor = Color.DimGray
        }, 0, 3);
        Controls.Add(shell);
        CancelButton = close;
    }

    public static string Seconds(long? value) => value.HasValue
        ? (value.Value / 1_000_000M).ToString("0.0000", CultureInfo.CurrentCulture) : "N/A";

    private static string Interval(long? value, bool installed) => installed
        ? value.HasValue ? Seconds(value) : "Missed" : "N/A";

    // Fit names and numbers within stable cells, including four-lane and high-DPI layouts.
    private sealed class FitText : Control
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float MaximumFontSize { get; init; } = 28;

        public FitText()
        {
            Dock = DockStyle.Fill;
            Margin = new Padding(0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width <= 12 || ClientSize.Height <= 8 || string.IsNullOrEmpty(Text)) return;
            var bounds = Rectangle.Inflate(ClientRectangle, -6, -4);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            for (var size = MaximumFontSize; size >= 6; size -= 0.5F)
            {
                using var font = new Font("Segoe UI", size, FontStyle.Bold);
                var measured = TextRenderer.MeasureText(e.Graphics, Text, font,
                    new Size(bounds.Width, int.MaxValue), flags);
                if ((measured.Width <= bounds.Width && measured.Height <= bounds.Height) || size == 6)
                {
                    TextRenderer.DrawText(e.Graphics, Text, font, bounds, ForeColor, flags);
                    break;
                }
            }
        }
    }
}
