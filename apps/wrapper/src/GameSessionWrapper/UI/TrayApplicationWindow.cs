using System.Drawing.Drawing2D;

namespace GameSessionWrapper.UI;

internal sealed class TrayApplicationWindow : Form
{
    private readonly Func<IEnumerable<GameConfig>> gamesProvider;
    private readonly TrayWindowActions actions;
    private TrayPalette palette = TrayTheme.Light;
    private TrayLayoutMetrics layout = TrayLayoutMetrics.ForDpi(96);

    public TrayApplicationWindow(Func<IEnumerable<GameConfig>> gamesProvider, TrayWindowActions actions)
    {
        this.gamesProvider = gamesProvider;
        this.actions = actions;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = true;
        Text = AppBrand.ProductName;
        Icon = AppBrand.LoadApplicationIcon();
        KeyPreview = true;
        AccessibleName = AppBrand.WindowAccessibleName;
        FormClosing += (_, e) =>
        {
            if (TrayWindowLifecycle.ShouldHideOnClose(e.CloseReason))
            {
                e.Cancel = true;
                HideToTray();
            }
        };
    }

    public void ShowWindow(Point anchor)
    {
        layout = TrayLayoutMetrics.ForDpi(TrayDpi.GetAtPoint(anchor));
        RefreshContent();
        WindowState = TrayWindowLifecycle.RestoredState(WindowState);
        if (!Visible)
        {
            Show();
        }
        Activate();
        BringToFront();
        SelectNextControl(null, true, true, true, true);
    }

    public void HideToTray()
    {
        // Changing ShowInTaskbar recreates the native window handle. Hiding a form
        // already removes its taskbar button, so keep the same handle for its lifetime.
        Hide();
    }

    public void RefreshContent()
    {
        palette = TrayTheme.Current();
        SuspendLayout();
        while (Controls.Count > 0)
        {
            Controls[0].Dispose();
        }

        var games = gamesProvider().OrderBy(g => TrayGamePresentation.From(g).Title, StringComparer.OrdinalIgnoreCase).ToList();
        var gamesHeight = Math.Clamp(games.Count * layout.RowStride, layout.GamePanelMinHeight, layout.GamePanelMaxHeight);
        ClientSize = new Size(layout.PopupWidth, layout.ActionAreaHeight + gamesHeight);
        BackColor = palette.Background;

        var gamesPanel = new Panel
        {
            Location = P(8, 6),
            Size = new Size(layout.PopupWidth - S(16), gamesHeight),
            AutoScroll = games.Count * layout.RowStride > gamesHeight,
            BackColor = palette.Background
        };
        Controls.Add(gamesPanel);
        BuildGameRows(gamesPanel, games);

        var addTop = S(12) + gamesHeight;
        var accentText = palette == TrayTheme.Dark ? Color.FromArgb(20, 20, 20) : Color.White;
        var add = CreateButton("+  Add a game", palette.Accent, accentText, actions.AddGame);
        add.Location = new Point(S(10), addTop);
        add.Size = new Size(layout.PopupWidth - S(20), layout.PrimaryButtonHeight);
        add.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        Controls.Add(add);

        var management = new TableLayoutPanel
        {
            Location = new Point(S(6), addTop + S(38)),
            Size = new Size(layout.PopupWidth - S(12), layout.ManagementButtonHeight),
            ColumnCount = 3,
            RowCount = 1,
            BackColor = palette.Background
        };
        management.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        management.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        management.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        management.Controls.Add(CreateSecondaryButton("Settings", actions.OpenSettings), 0, 0);
        management.Controls.Add(CreateSecondaryButton("Launchers", actions.OpenLauncherFolder), 1, 0);
        management.Controls.Add(CreateSecondaryButton("Sync", actions.SynchronizeLaunchers), 2, 0);
        Controls.Add(management);

        var exit = CreateButton($"Exit {AppBrand.ProductName}", palette.Background, palette.MutedText, actions.Exit);
        exit.Location = new Point(S(10), addTop + S(80));
        exit.Size = new Size(layout.PopupWidth - S(20), layout.ExitButtonHeight);
        exit.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(exit);
        ResumeLayout(true);
        Invalidate();
    }

    private void BuildGameRows(Panel panel, List<GameConfig> games)
    {
        if (games.Count == 0)
        {
            panel.Controls.Add(new Label
            {
                Text = "No games yet\nAdd an executable to get started.",
                Location = P(8, 8),
                Size = new Size(panel.ClientSize.Width - S(16), S(64)),
                ForeColor = palette.MutedText,
                Font = new Font("Segoe UI", 9.5f),
                TextAlign = ContentAlignment.MiddleCenter
            });
            return;
        }

        for (var index = 0; index < games.Count; index++)
        {
            var game = games[index];
            var hasScrollbar = games.Count * layout.RowStride > panel.ClientSize.Height;
            var rowWidth = panel.ClientSize.Width - (hasScrollbar ? SystemInformation.VerticalScrollBarWidth : 0);
            var row = new TrayGameRow(game, palette, layout, rowWidth)
            {
                Location = new Point(0, index * layout.RowStride)
            };
            row.LaunchRequested += async (_, _) => await actions.LaunchGame(game.GameId);
            row.EditGameSettingsRequested += async (_, _) => await actions.EditGameSettings(game.GameId);
            row.RemoveRequested += async (_, _) => await actions.RemoveGame(game.GameId);
            panel.Controls.Add(row);
        }
    }

    private Button CreateSecondaryButton(string text, Func<Task> action)
    {
        var button = CreateButton(text, palette.Surface, palette.Text, action);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(S(2));
        button.Font = new Font("Segoe UI", 8.5f);
        return button;
    }

    private Button CreateButton(string text, Color background, Color foreground, Func<Task> action)
    {
        var button = new Button
        {
            Text = text,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = true,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = background == palette.Accent ? palette.AccentHover : palette.Hover;
        button.Click += async (_, _) => await action();
        return button;
    }

    private int S(int logicalPixels) => layout.Scale(logicalPixels);
    private Point P(int x, int y) => new(S(x), S(y));
    private Size Z(int width, int height) => new(S(width), S(height));
}

internal sealed class ApiSettingsDialog : Form
{
    private readonly TextBox baseUrlInput;
    private readonly TextBox apiKeyInput;
    private readonly Label validation;
    private readonly ApiSettingsDialogMetrics layout;

    public string BaseUrl { get; private set; }
    public string ApiKey { get; private set; }

    public ApiSettingsDialog(string baseUrl, string apiKey, TrayPalette palette, int dpi)
    {
        layout = ApiSettingsDialogMetrics.ForDpi(dpi);
        BaseUrl = baseUrl;
        ApiKey = apiKey;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = false;
        ClientSize = layout.ClientSize;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Text = "Settings";
        Icon = AppBrand.LoadApplicationIcon();
        BackColor = palette.Background;
        ForeColor = palette.Text;
        AccessibleName = AppBrand.ApiSettingsAccessibleName;

        Controls.Add(CreateLabel("API base URL", 16, 14, palette));
        baseUrlInput = new TextBox
        {
            Text = baseUrl,
            Location = P(16, 38),
            Size = layout.InputSize,
            MaxLength = ApiConfig.MaximumBaseUrlLength,
            BackColor = palette.Surface,
            ForeColor = palette.Text,
            AccessibleName = "API base URL"
        };
        Controls.Add(baseUrlInput);

        Controls.Add(CreateLabel("API key", 16, 78, palette));
        apiKeyInput = new TextBox
        {
            Text = apiKey,
            Location = P(16, 102),
            Size = layout.InputSize,
            MaxLength = ApiConfig.MaximumApiKeyLength,
            UseSystemPasswordChar = true,
            BackColor = palette.Surface,
            ForeColor = palette.Text,
            AccessibleName = "API key"
        };
        Controls.Add(apiKeyInput);

        validation = new Label
        {
            Location = P(16, 139),
            Size = Z(388, 24),
            Font = new Font("Segoe UI", 8f),
            ForeColor = palette.Danger
        };
        Controls.Add(validation);

        var cancel = CreateDialogButton("Cancel", palette.Surface, palette.Text);
        cancel.Location = P(260, 176);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);

        var save = CreateDialogButton("Save", palette.Accent, palette == TrayTheme.Dark ? Color.FromArgb(20, 20, 20) : Color.White);
        save.Location = P(336, 176);
        save.Click += (_, _) => SaveSettings();
        Controls.Add(save);

        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            baseUrlInput.Focus();
            baseUrlInput.SelectAll();
        };
    }

    private Label CreateLabel(string text, int x, int y, TrayPalette palette) => new()
    {
        Text = text,
        Location = P(x, y),
        Size = Z(388, 20),
        Font = new Font("Segoe UI", 9f),
        ForeColor = palette.Text
    };

    private Button CreateDialogButton(string text, Color background, Color foreground)
    {
        var button = new Button
        {
            Text = text,
            Size = layout.ButtonSize,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void SaveSettings()
    {
        try
        {
            BaseUrl = ApiConfig.NormalizeBaseUrl(baseUrlInput.Text);
            ApiKey = apiKeyInput.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException ex)
        {
            validation.Text = ex.Message;
            baseUrlInput.Focus();
        }
    }

    private int S(int logicalPixels) => layout.Scale(logicalPixels);
    private Point P(int x, int y) => new(S(x), S(y));
    private Size Z(int width, int height) => new(S(width), S(height));
}

internal sealed class GameSettingsDialog : Form
{
    public const string SteamDbUrl = "https://steamdb.info/";

    private readonly TextBox gameNameInput;
    private readonly TextBox gameIdInput;
    private readonly Label validation;
    private readonly ApiSettingsDialogMetrics layout;

    public string GameName { get; private set; }
    public string GameId { get; private set; }

    public GameSettingsDialog(string currentGameName, string currentGameId, TrayPalette palette, int dpi)
    {
        layout = ApiSettingsDialogMetrics.ForDpi(dpi);
        GameName = currentGameName;
        GameId = currentGameId;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = false;
        ClientSize = layout.ClientSize;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Text = "Game settings";
        Icon = AppBrand.LoadApplicationIcon();
        BackColor = palette.Background;
        ForeColor = palette.Text;
        AccessibleName = $"Game settings for {currentGameName}";

        Controls.Add(new Label
        {
            Text = "Game name",
            Location = P(16, 14),
            Size = Z(388, 20),
            Font = new Font("Segoe UI", 9f),
            ForeColor = palette.Text
        });
        gameNameInput = new TextBox
        {
            Text = currentGameName,
            Location = P(16, 38),
            Size = layout.InputSize,
            MaxLength = GameCatalog.MaximumDisplayNameLength,
            BackColor = palette.Surface,
            ForeColor = palette.Text,
            AccessibleName = "Game name"
        };
        Controls.Add(gameNameInput);

        Controls.Add(new Label
        {
            Text = "Game ID",
            Location = P(16, 78),
            Size = Z(100, 20),
            Font = new Font("Segoe UI", 9f),
            ForeColor = palette.Text
        });
        var steamDbLink = new LinkLabel
        {
            Text = "Find on SteamDB",
            Location = P(276, 78),
            Size = Z(128, 20),
            TextAlign = ContentAlignment.TopRight,
            Font = new Font("Segoe UI", 8.5f),
            LinkColor = palette.Accent,
            ActiveLinkColor = palette.AccentHover,
            VisitedLinkColor = palette.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            AccessibleName = "Open SteamDB to find the game ID"
        };
        steamDbLink.LinkClicked += (_, _) => OpenSteamDb();
        Controls.Add(steamDbLink);
        gameIdInput = new TextBox
        {
            Text = currentGameId,
            Location = P(16, 102),
            Size = layout.InputSize,
            MaxLength = GameCatalog.MaximumApiGameIdLength,
            BackColor = palette.Surface,
            ForeColor = palette.Text,
            AccessibleName = "Game ID"
        };
        Controls.Add(gameIdInput);

        validation = new Label
        {
            Location = P(16, 139),
            Size = Z(388, 24),
            Font = new Font("Segoe UI", 8f),
            ForeColor = palette.Danger
        };
        Controls.Add(validation);

        var cancel = CreateDialogButton("Cancel", palette.Surface, palette.Text);
        cancel.Location = P(260, 176);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);

        var save = CreateDialogButton("Save", palette.Accent, palette == TrayTheme.Dark ? Color.FromArgb(20, 20, 20) : Color.White);
        save.Location = P(336, 176);
        save.Click += (_, _) => SaveGameSettings();
        Controls.Add(save);

        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            gameNameInput.Focus();
            gameNameInput.SelectAll();
        };
    }

    private Button CreateDialogButton(string text, Color background, Color foreground)
    {
        var button = new Button
        {
            Text = text,
            Size = layout.ButtonSize,
            BackColor = background,
            ForeColor = foreground,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void SaveGameSettings()
    {
        try
        {
            GameName = GameCatalog.NormalizeDisplayName(gameNameInput.Text);
            GameId = GameCatalog.NormalizeApiGameId(gameIdInput.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException ex)
        {
            validation.Text = ex.Message;
            gameNameInput.Focus();
        }
    }

    private void OpenSteamDb()
    {
        try
        {
            Process.Start(new ProcessStartInfo(SteamDbUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open SteamDB: {ex.Message}", "Open SteamDB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private int S(int logicalPixels) => layout.Scale(logicalPixels);
    private Point P(int x, int y) => new(S(x), S(y));
    private Size Z(int width, int height) => new(S(width), S(height));
}

internal sealed class TrayGameRow : UserControl
{
    private readonly TrayPalette palette;
    private readonly TrayLayoutMetrics layout;
    private bool hovering;
    public event EventHandler? LaunchRequested;
    public event EventHandler? EditGameSettingsRequested;
    public event EventHandler? RemoveRequested;

    public TrayGameRow(GameConfig game, TrayPalette palette, TrayLayoutMetrics layout, int width)
    {
        this.palette = palette;
        this.layout = layout;
        var display = TrayGamePresentation.From(game);
        Size = new Size(width, layout.RowHeight);
        DoubleBuffered = true;
        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = palette.Background;
        AccessibleName = $"Launch {display.Title}";
        AccessibleRole = AccessibleRole.PushButton;

        var icon = new PictureBox { Location = P(8, 10), Size = Z(36, 36), SizeMode = PictureBoxSizeMode.Zoom, Image = LoadGameIcon(game.Launch.ExecutablePath) };
        var moreWidth = layout.OverflowButtonSize;
        var moreLeft = Width - S(8) - moreWidth;
        var textLeft = S(52);
        var textWidth = moreLeft - textLeft - S(6);
        var title = new Label
        {
            Text = display.Title,
            Location = new Point(textLeft, S(5)),
            Size = new Size(textWidth, S(24)),
            ForeColor = palette.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.BottomLeft
        };
        var subtitle = new Label
        {
            Text = display.Subtitle,
            Location = new Point(textLeft, S(29)),
            Size = new Size(textWidth, S(22)),
            ForeColor = palette.MutedText,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f),
            AutoEllipsis = true
        };
        var more = new Button
        {
            Text = "...",
            AccessibleName = $"More actions for {display.Title}",
            Location = new Point(moreLeft, S(13)),
            Size = new Size(moreWidth, layout.OverflowButtonSize),
            FlatStyle = FlatStyle.Flat,
            BackColor = palette.Background,
            ForeColor = palette.MutedText,
            Cursor = Cursors.Hand,
            TabStop = true
        };
        more.FlatAppearance.BorderSize = 0;
        more.FlatAppearance.MouseOverBackColor = palette.Hover;
        more.Click += (_, _) => ShowOverflow(more, display.Title);
        Controls.Add(icon);
        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(more);
        WireLaunch(icon);
        WireLaunch(title);
        WireLaunch(subtitle);
        foreach (Control control in Controls)
        {
            control.MouseEnter += (_, _) => SetHover(true);
            control.MouseLeave += (_, _) => SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
        }
        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) => SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
        KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                LaunchRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
        GotFocus += (_, _) => Invalidate();
        LostFocus += (_, _) => Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        LaunchRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(palette.Background);
        using var brush = new SolidBrush(hovering || Focused ? palette.Hover : palette.Background);
        e.Graphics.FillRoundedRectangle(brush, ClientRectangle, S(8));
        if (Focused)
        {
            using var pen = new Pen(palette.Accent, Math.Max(1f, S(1)));
            var bounds = ClientRectangle;
            bounds.Inflate(-1, -1);
            e.Graphics.DrawRoundedRectangle(pen, bounds, S(7));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var picture in Controls.OfType<PictureBox>()) picture.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void WireLaunch(Control control)
    {
        control.Cursor = Cursors.Hand;
        control.Click += (_, _) => LaunchRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetHover(bool value)
    {
        if (hovering == value) return;
        hovering = value;
        Invalidate();
    }

    private void ShowOverflow(Control anchor, string title)
    {
        var outerPadding = S(2);
        var menu = new ContextMenuStrip
        {
            AutoSize = false,
            BackColor = palette.Surface,
            ForeColor = palette.Text,
            ShowImageMargin = false,
            GripStyle = ToolStripGripStyle.Hidden,
            LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow,
            Padding = new Padding(outerPadding)
        };
        var gameSettings = new ToolStripMenuItem("Game settings...") { AutoSize = false, ForeColor = palette.Text };
        var remove = new ToolStripMenuItem($"Remove {title}") { AutoSize = false, ForeColor = palette.Danger };
        var settingsTextSize = TextRenderer.MeasureText(gameSettings.Text, gameSettings.Font, Size.Empty, TextFormatFlags.NoPadding);
        var removeTextSize = TextRenderer.MeasureText(remove.Text, remove.Font, Size.Empty, TextFormatFlags.NoPadding);
        var horizontalPadding = S(8);
        var itemWidth = Math.Max(settingsTextSize.Width, removeTextSize.Width) + horizontalPadding * 2;
        var itemHeight = Math.Max(S(24), Math.Max(settingsTextSize.Height, removeTextSize.Height) + S(4));
        menu.Size = new Size(itemWidth + outerPadding * 2, itemHeight * 2 + outerPadding * 2);
        foreach (var item in new[] { gameSettings, remove })
        {
            item.Size = new Size(itemWidth, itemHeight);
            item.Padding = new Padding(horizontalPadding, 0, horizontalPadding, 0);
            item.Margin = Padding.Empty;
        }
        var settingsSelected = false;
        var removeSelected = false;
        gameSettings.Click += (_, _) => settingsSelected = true;
        remove.Click += (_, _) => removeSelected = true;
        menu.Items.Add(gameSettings);
        menu.Items.Add(remove);
        menu.Closed += (_, _) => BeginInvoke(new Action(() =>
        {
            menu.Dispose();
            if (settingsSelected)
            {
                EditGameSettingsRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (removeSelected)
            {
                RemoveRequested?.Invoke(this, EventArgs.Empty);
            }
        }));
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private static Bitmap LoadGameIcon(string? path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null) return icon.ToBitmap();
            }
        }
        catch { }
        using var applicationIcon = AppBrand.LoadApplicationIcon();
        return applicationIcon.ToBitmap();
    }

    private int S(int logicalPixels) => layout.Scale(logicalPixels);
    private Point P(int x, int y) => new(S(x), S(y));
    private Size Z(int width, int height) => new(S(width), S(height));
}

internal static class RoundedGraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = CreatePath(bounds, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using var path = CreatePath(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreatePath(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
