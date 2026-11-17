namespace Nuggi;

// Shared colors, fonts and control styles for every Nuggi window.
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(243, 244, 246);
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color StrongBorder = Color.FromArgb(203, 213, 225);
    public static readonly Color Text = Color.FromArgb(15, 23, 42);
    public static readonly Color MutedText = Color.FromArgb(100, 116, 139);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
    public static readonly Color Selection = Color.FromArgb(219, 234, 254);
    public static readonly Color Hover = Color.FromArgb(241, 245, 249);
    public static readonly Color PreviewBackground = Color.FromArgb(241, 245, 249);
    public static readonly Color Disabled = Color.FromArgb(226, 232, 240);

    // Same colors the mask overlay uses, so the legend matches the preview.
    public static readonly Color KeepArea = Color.FromArgb(34, 197, 94);
    public static readonly Color AddedArea = Color.FromArgb(22, 163, 74);
    public static readonly Color RemovedArea = Color.FromArgb(220, 38, 38);

    private const string FontFamily = "Malgun Gothic";
    public static readonly Font BaseFont = new(FontFamily, 9.75f);
    public static readonly Font SmallFont = new(FontFamily, 9f);
    public static readonly Font LabelFont = new(FontFamily, 9f, FontStyle.Bold);
    public static readonly Font TitleFont = new(FontFamily, 12f, FontStyle.Bold);

    public static void ApplyToForm(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = BaseFont;
    }

    public static Panel Card(Padding padding)
    {
        var card = new Panel
        {
            BackColor = Surface,
            Padding = padding,
            Margin = Padding.Empty,
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        return card;
    }

    public static Label Title(string text) => new()
    {
        AutoSize = true,
        Text = text,
        Font = TitleFont,
        Margin = new Padding(0, 0, 0, 4),
    };

    public static Label FieldLabel(string text) => new()
    {
        AutoSize = true,
        Text = text,
        Font = LabelFont,
        ForeColor = MutedText,
        Margin = new Padding(0, 14, 0, 4),
    };

    public static Label MutedLabel(string text) => new()
    {
        AutoSize = true,
        Text = text,
        Font = SmallFont,
        ForeColor = MutedText,
    };

    public static Button PrimaryButton(string text, EventHandler? onClick = null)
    {
        var button = new Button { Text = text };
        StylePrimary(button);
        if (onClick is not null)
        {
            button.Click += onClick;
        }

        return button;
    }

    public static Button SecondaryButton(string text, EventHandler? onClick = null)
    {
        var button = new Button { Text = text };
        StyleSecondary(button);
        if (onClick is not null)
        {
            button.Click += onClick;
        }

        return button;
    }

    public static void StylePrimary(Button button)
    {
        StyleBase(button);
        button.ForeColor = Color.White;
        button.BackColor = Accent;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        button.FlatAppearance.MouseDownBackColor = AccentHover;
        // Flat buttons draw disabled text in system gray; a light background keeps it readable.
        button.EnabledChanged += (_, _) => button.BackColor = button.Enabled ? Accent : Disabled;
    }

    public static void StyleSecondary(Button button)
    {
        StyleBase(button);
        button.ForeColor = Text;
        button.BackColor = Surface;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = StrongBorder;
        button.FlatAppearance.MouseOverBackColor = Hover;
        button.FlatAppearance.MouseDownBackColor = Border;
    }

    // A radio button that looks like one half of a segmented toggle.
    public static void StyleToggle(RadioButton radioButton)
    {
        radioButton.Appearance = Appearance.Button;
        radioButton.FlatStyle = FlatStyle.Flat;
        radioButton.AutoSize = true;
        radioButton.TextAlign = ContentAlignment.MiddleCenter;
        radioButton.Padding = new Padding(10, 4, 10, 4);
        radioButton.Cursor = Cursors.Hand;
        radioButton.BackColor = Surface;
        radioButton.FlatAppearance.BorderColor = StrongBorder;
        radioButton.FlatAppearance.CheckedBackColor = Selection;
        radioButton.FlatAppearance.MouseOverBackColor = Hover;
    }

    private static void StyleBase(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.AutoSize = true;
        button.Padding = new Padding(10, 4, 10, 4);
        button.Cursor = Cursors.Hand;
    }
}
