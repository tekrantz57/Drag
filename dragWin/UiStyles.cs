namespace DragWin;

internal static class UiStyles
{
    public static readonly Color BlueAction = Color.FromArgb(31, 78, 121);
    public static readonly Color GreenAction = Color.FromArgb(31, 103, 67);

    public static void ConfigurePrimaryButton(Button button, Color accentColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;

        void ApplyContrast()
        {
            if (button.Enabled)
            {
                button.BackColor = accentColor;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = ControlPaint.Dark(accentColor);
            }
            else
            {
                button.BackColor = Color.FromArgb(245, 246, 247);
                button.ForeColor = Color.FromArgb(54, 60, 66);
                button.FlatAppearance.BorderColor = Color.FromArgb(160, 166, 172);
            }
        }

        button.EnabledChanged += (_, _) => ApplyContrast();
        ApplyContrast();
    }

    public static void SetSplitterDistanceWhenSized(
        SplitContainer split,
        int preferredDistance,
        int minimumPanel1,
        int minimumPanel2)
    {
        var available = split.Orientation == Orientation.Vertical
            ? split.ClientSize.Width
            : split.ClientSize.Height;
        var maximumDistance = available - split.SplitterWidth - minimumPanel2;
        if (maximumDistance < minimumPanel1)
        {
            return;
        }

        split.SplitterDistance = Math.Clamp(
            preferredDistance,
            minimumPanel1,
            maximumDistance);
    }
}
