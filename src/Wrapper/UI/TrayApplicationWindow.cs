using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Wrapper.UI;

internal sealed class TrayApplicationWindow : Form
{
    private readonly Func<IEnumerable<GameConfig>> gamesProvider;
    private readonly Func<string, BackupStatusPresentation> backupStatusProvider;
    private readonly TrayWindowActions actions;
    private TrayGameListPanel? gamesPanel;
    private TrayPalette palette = TrayTheme.Light;
    private TrayLayoutMetrics layout = TrayLayoutMetrics.ForDpi(96);

    public TrayApplicationWindow(
        Func<IEnumerable<GameConfig>> gamesProvider,
        Func<string, BackupStatusPresentation> backupStatusProvider,
        TrayWindowActions actions)
    {
        this.gamesProvider = gamesProvider;
        this.backupStatusProvider = backupStatusProvider;
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

        gamesPanel = new TrayGameListPanel(palette, layout)
        {
            Location = P(8, 6),
            Size = new Size(layout.PopupWidth - S(16), gamesHeight),
            BackColor = palette.Background
        };
        gamesPanel.SetContentHeight(games.Count * layout.RowStride);
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

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (gamesPanel is not null && gamesPanel.RectangleToScreen(gamesPanel.ClientRectangle).Contains(Cursor.Position))
        {
            gamesPanel.ScrollByWheel(e.Delta);
            return;
        }

        base.OnMouseWheel(e);
    }

    private void BuildGameRows(TrayGameListPanel panel, List<GameConfig> games)
    {
        if (games.Count == 0)
        {
            panel.AddContent(new Label
            {
                Text = "No games yet\nAdd an executable to get started.",
                Location = P(8, 8),
                Size = new Size(panel.ContentWidth - S(16), S(64)),
                ForeColor = palette.MutedText,
                Font = new Font("Segoe UI", 9.5f),
                TextAlign = ContentAlignment.MiddleCenter
            });
            return;
        }

        for (var index = 0; index < games.Count; index++)
        {
            var game = games[index];
            var rowWidth = panel.ContentWidth;
            var row = new TrayGameRow(game, backupStatusProvider(game.GameId), palette, layout, rowWidth)
            {
                Location = new Point(0, index * layout.RowStride)
            };
            row.LaunchRequested += async (_, _) => await actions.LaunchGame(game.GameId);
            row.BackupRequested += async (_, _) => await actions.BackupGame(game.GameId);
            row.EditGameSettingsRequested += async (_, _) => await actions.EditGameSettings(game.GameId);
            row.RemoveRequested += async (_, _) => await actions.RemoveGame(game.GameId);
            panel.AddContent(row);
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

internal sealed class TrayGameListPanel : Panel
{
    private readonly Panel content;
    private readonly TrayScrollBar scrollbar;
    private readonly TrayLayoutMetrics layout;
    private int contentHeight;

    public int ContentWidth => content.Width;

    public TrayGameListPanel(TrayPalette palette, TrayLayoutMetrics layout)
    {
        this.layout = layout;
        DoubleBuffered = true;
        TabStop = false;

        content = new Panel
        {
            BackColor = palette.Background,
            Location = Point.Empty,
            TabStop = false
        };
        scrollbar = new TrayScrollBar(palette, layout)
        {
            Visible = false
        };
        scrollbar.ValueChanged += (_, _) => content.Top = -scrollbar.Value;
        Controls.Add(content);
        Controls.Add(scrollbar);
        scrollbar.BringToFront();
    }

    public void SetContentHeight(int height)
    {
        contentHeight = Math.Max(0, height);
        UpdateScrollLayout();
    }

    public void AddContent(Control control) => content.Controls.Add(control);

    public void ScrollByWheel(int delta)
    {
        if (!scrollbar.Visible || delta == 0) return;

        var lines = SystemInformation.MouseWheelScrollLines;
        if (lines == 0) return;
        var step = lines < 0
            ? ClientSize.Height
            : Math.Max(1, layout.RowStride * lines / 3);
        scrollbar.Value -= (int)Math.Round(delta / (double)SystemInformation.MouseWheelScrollDelta * step);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        UpdateScrollLayout();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollByWheel(e.Delta);
    }

    private void UpdateScrollLayout()
    {
        var viewportHeight = Math.Max(0, ClientSize.Height);
        var hasOverflow = contentHeight > viewportHeight;
        var gutter = hasOverflow ? layout.ScrollbarWidth + layout.ScrollbarMargin * 2 : 0;
        content.Size = new Size(
            Math.Max(0, ClientSize.Width - gutter),
            Math.Max(contentHeight, viewportHeight));
        scrollbar.Visible = hasOverflow;
        scrollbar.Bounds = new Rectangle(
            Math.Max(0, ClientSize.Width - layout.ScrollbarWidth - layout.ScrollbarMargin),
            layout.ScrollbarMargin,
            layout.ScrollbarWidth,
            Math.Max(0, viewportHeight - layout.ScrollbarMargin * 2));
        scrollbar.SetRange(viewportHeight, contentHeight);
        content.Top = -scrollbar.Value;
    }
}

internal sealed class TrayScrollBar : Control
{
    private readonly TrayPalette palette;
    private readonly TrayLayoutMetrics layout;
    private int maximum;
    private int value;
    private int viewportHeight;
    private int contentHeight;
    private bool dragging;
    private int dragStartY;
    private int dragStartValue;

    public event EventHandler? ValueChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => value;
        set
        {
            var next = Math.Clamp(value, 0, maximum);
            if (next == this.value) return;
            this.value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public TrayScrollBar(TrayPalette palette, TrayLayoutMetrics layout)
    {
        this.palette = palette;
        this.layout = layout;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        AccessibleName = "Game list scrollbar";
        AccessibleRole = AccessibleRole.ScrollBar;
        BackColor = palette.Background;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public void SetRange(int viewportHeight, int contentHeight)
    {
        this.viewportHeight = Math.Max(0, viewportHeight);
        this.contentHeight = Math.Max(0, contentHeight);
        maximum = Math.Max(0, this.contentHeight - this.viewportHeight);
        Value = Math.Min(Value, maximum);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (maximum <= 0 || Height <= 0) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var trackWidth = Math.Max(2, layout.ScrollbarWidth / 3);
        var track = new Rectangle((Width - trackWidth) / 2, 0, trackWidth, Height);
        using var trackBrush = new SolidBrush(palette.Border);
        e.Graphics.FillRoundedRectangle(trackBrush, track, trackWidth / 2);

        var thumb = ThumbBounds();
        using var thumbBrush = new SolidBrush(palette.MutedText);
        e.Graphics.FillRoundedRectangle(thumbBrush, thumb, Math.Max(1, thumb.Width / 2));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || maximum <= 0) return;

        var thumb = ThumbBounds();
        if (thumb.Contains(e.Location))
        {
            dragging = true;
            dragStartY = e.Y;
            dragStartValue = Value;
            Capture = true;
        }
        else
        {
            Value += e.Y < thumb.Top ? -viewportHeight : viewportHeight;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging || maximum <= 0) return;

        var available = Math.Max(1, Height - ThumbBounds().Height);
        Value = dragStartValue + (int)Math.Round((e.Y - dragStartY) * maximum / (double)available);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragging = false;
        Capture = false;
    }

    private Rectangle ThumbBounds()
    {
        var thumbHeight = contentHeight <= 0
            ? Height
            : Math.Clamp(
                (int)Math.Round(Height * viewportHeight / (double)contentHeight),
                Math.Min(layout.ScrollbarMinimumThumbHeight, Height),
                Height);
        var available = Math.Max(0, Height - thumbHeight);
        var top = maximum <= 0 ? 0 : (int)Math.Round(available * Value / (double)maximum);
        return new Rectangle(0, top, Width, thumbHeight);
    }
}

internal sealed class ApiSettingsDialog : Form
{
    private readonly TextBox baseUrlInput;
    private readonly TextBox apiKeyInput;
    private readonly TextBox ludusaviPathInput;
    private readonly Label validation;
    private readonly ApiSettingsDialogMetrics layout;

    public string BaseUrl { get; private set; }
    public string ApiKey { get; private set; }
    public string LudusaviExecutablePath { get; private set; }

    public ApiSettingsDialog(string baseUrl, string apiKey, string ludusaviExecutablePath, TrayPalette palette, int dpi)
    {
        layout = ApiSettingsDialogMetrics.ForDpi(dpi);
        BaseUrl = baseUrl;
        ApiKey = apiKey;
        LudusaviExecutablePath = ludusaviExecutablePath;
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

        Controls.Add(CreateLabel("Ludusavi executable", 16, 142, palette));
        ludusaviPathInput = new TextBox
        {
            Text = ludusaviExecutablePath,
            Location = P(16, 166),
            Size = Z(310, 27),
            MaxLength = LudusaviConfig.MaximumExecutablePathLength,
            BackColor = palette.Surface,
            ForeColor = palette.Text,
            AccessibleName = "Ludusavi executable path"
        };
        Controls.Add(ludusaviPathInput);
        var browse = CreateDialogButton("Browse...", palette.Surface, palette.Text);
        browse.Location = P(336, 165);
        browse.Click += (_, _) => BrowseForLudusavi();
        Controls.Add(browse);

        validation = new Label
        {
            Location = P(16, 205),
            Size = Z(388, 34),
            Font = new Font("Segoe UI", 8f),
            ForeColor = palette.Danger
        };
        Controls.Add(validation);

        var cancel = CreateDialogButton("Cancel", palette.Surface, palette.Text);
        cancel.Location = P(260, 256);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);

        var save = CreateDialogButton("Save", palette.Accent, palette == TrayTheme.Dark ? Color.FromArgb(20, 20, 20) : Color.White);
        save.Location = P(336, 256);
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
            LudusaviExecutablePath = LudusaviConfig.NormalizeExecutablePath(ludusaviPathInput.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException ex)
        {
            validation.Text = ex.Message;
            baseUrlInput.Focus();
        }
    }

    private void BrowseForLudusavi()
    {
        using var picker = new OpenFileDialog
        {
            Filter = "Ludusavi (ludusavi.exe)|ludusavi.exe|Executables (*.exe)|*.exe",
            Title = "Select Ludusavi"
        };
        if (picker.ShowDialog(this) == DialogResult.OK)
        {
            ludusaviPathInput.Text = picker.FileName;
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
    private readonly CheckBox dailyBackupInput;
    private readonly Label validation;
    private readonly ApiSettingsDialogMetrics layout;

    public string GameName { get; private set; }
    public string GameId { get; private set; }
    public bool DailyBackupEnabled { get; private set; }

    public GameSettingsDialog(string currentGameName, string currentGameId, bool dailyBackupEnabled, TrayPalette palette, int dpi)
    {
        layout = ApiSettingsDialogMetrics.ForDpi(dpi);
        GameName = currentGameName;
        GameId = currentGameId;
        DailyBackupEnabled = dailyBackupEnabled;
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

        dailyBackupInput = new CheckBox
        {
            Text = "Back up saves daily with Ludusavi",
            Checked = dailyBackupEnabled,
            Location = P(16, 139),
            Size = Z(388, 26),
            ForeColor = palette.Text,
            AccessibleName = "Enable daily Ludusavi backup"
        };
        Controls.Add(dailyBackupInput);

        validation = new Label
        {
            Location = P(16, 174),
            Size = Z(388, 40),
            Font = new Font("Segoe UI", 8f),
            ForeColor = palette.Danger
        };
        Controls.Add(validation);

        var cancel = CreateDialogButton("Cancel", palette.Surface, palette.Text);
        cancel.Location = P(260, 256);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);

        var save = CreateDialogButton("Save", palette.Accent, palette == TrayTheme.Dark ? Color.FromArgb(20, 20, 20) : Color.White);
        save.Location = P(336, 256);
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
            DailyBackupEnabled = dailyBackupInput.Checked;
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
    private readonly ToolTip backupTooltip = new();
    public event EventHandler? LaunchRequested;
    public event EventHandler? BackupRequested;
    public event EventHandler? EditGameSettingsRequested;
    public event EventHandler? RemoveRequested;

    public TrayGameRow(GameConfig game, BackupStatusPresentation backupStatus, TrayPalette palette, TrayLayoutMetrics layout, int width)
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
        var statusSize = S(22);
        var statusLeft = moreLeft - statusSize - S(2);
        var textLeft = S(52);
        var textWidth = statusLeft - textLeft - S(6);
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
        var backupIndicator = new Label
        {
            Text = backupStatus.Glyph,
            AccessibleName = backupStatus.Tooltip,
            Location = new Point(statusLeft, S(17)),
            Size = new Size(statusSize, S(22)),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = IndicatorColor(backupStatus.Kind),
            BackColor = Color.Transparent
        };
        backupTooltip.SetToolTip(backupIndicator, backupStatus.Tooltip);
        Controls.Add(icon);
        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(backupIndicator);
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
            backupTooltip.Dispose();
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
        var menuLayout = TrayMenuMetrics.ForDpi(layout.Dpi);
        var menu = new ContextMenuStrip
        {
            AutoSize = false,
            BackColor = palette.Surface,
            ForeColor = palette.Text,
            ShowImageMargin = false,
            GripStyle = ToolStripGripStyle.Hidden,
            LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow,
            Padding = new Padding(
                menuLayout.OuterPadding,
                menuLayout.OuterPadding,
                0,
                0),
            DropShadowEnabled = true,
            Font = new Font("Segoe UI", 9f),
            Renderer = new TrayContextMenuRenderer(palette, menuLayout)
        };
        var backup = new ToolStripMenuItem("Back up saves now") { AutoSize = false, ForeColor = palette.Text };
        var gameSettings = new ToolStripMenuItem("Game settings") { AutoSize = false, ForeColor = palette.Text };
        var separator = new ToolStripSeparator { AutoSize = false };
        var remove = new ToolStripMenuItem($"Remove {title}")
        {
            AutoSize = false,
            ForeColor = palette.Danger,
            Tag = TrayContextMenuRenderer.DangerItemTag
        };
        var backupTextSize = TextRenderer.MeasureText(backup.Text, backup.Font, Size.Empty, TextFormatFlags.NoPadding);
        var settingsTextSize = TextRenderer.MeasureText(gameSettings.Text, gameSettings.Font, Size.Empty, TextFormatFlags.NoPadding);
        var removeTextSize = TextRenderer.MeasureText(remove.Text, remove.Font, Size.Empty, TextFormatFlags.NoPadding);
        var itemWidth = Math.Max(backupTextSize.Width, Math.Max(settingsTextSize.Width, removeTextSize.Width)) + menuLayout.HorizontalPadding * 2;
        menu.Size = new Size(
            itemWidth + menuLayout.OuterPadding,
            menuLayout.ItemHeight * 3 + menuLayout.SeparatorHeight + menuLayout.OuterPadding);
        foreach (var item in new[] { backup, gameSettings, remove })
        {
            item.Size = new Size(itemWidth, menuLayout.ItemHeight);
            item.Padding = new Padding(menuLayout.HorizontalPadding, 0, menuLayout.HorizontalPadding, 0);
            item.Margin = Padding.Empty;
        }
        separator.Size = new Size(itemWidth, menuLayout.SeparatorHeight);
        separator.Margin = Padding.Empty;
        var backupSelected = false;
        var settingsSelected = false;
        var removeSelected = false;
        backup.Click += (_, _) => backupSelected = true;
        gameSettings.Click += (_, _) => settingsSelected = true;
        remove.Click += (_, _) => removeSelected = true;
        menu.Items.Add(backup);
        menu.Items.Add(gameSettings);
        menu.Items.Add(separator);
        menu.Items.Add(remove);
        menu.Opening += (_, _) => TrayContextMenuRenderer.ApplyRoundedRegion(menu, menuLayout.CornerRadius);
        menu.Closed += (_, _) => BeginInvoke(new Action(() =>
        {
            menu.Dispose();
            if (backupSelected)
            {
                BackupRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (settingsSelected)
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

    private Color IndicatorColor(BackupIndicatorKind kind) => kind switch
    {
        BackupIndicatorKind.Success => Color.FromArgb(34, 139, 74),
        BackupIndicatorKind.Failed => palette.Danger,
        BackupIndicatorKind.Due => Color.FromArgb(210, 140, 0),
        BackupIndicatorKind.Running => palette.Accent,
        _ => palette.MutedText
    };

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

internal sealed class TrayContextMenuRenderer(TrayPalette palette, TrayMenuMetrics layout) : ToolStripProfessionalRenderer
{
    public const string DangerItemTag = "danger";

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(palette.Surface);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(Point.Empty, e.ToolStrip.Size);
        bounds.Width -= 1;
        bounds.Height -= 1;
        using var path = CreateRoundedPath(bounds, layout.CornerRadius);
        using var pen = new Pen(palette.Border);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var inset = layout.Scale(1);
        var bounds = new Rectangle(inset, inset, e.Item.Width - inset * 2, e.Item.Height - inset * 2);
        var isDanger = string.Equals(e.Item.Tag as string, DangerItemTag, StringComparison.Ordinal);
        using var brush = new SolidBrush(isDanger ? Color.FromArgb(38, palette.Danger) : palette.Hover);
        using var path = CreateRoundedPath(bounds, layout.ItemCornerRadius);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(palette.Border);
        e.Graphics.DrawLine(pen, layout.HorizontalPadding, y, e.Item.Width - layout.HorizontalPadding, y);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var textBounds = new Rectangle(
            layout.HorizontalPadding,
            0,
            Math.Max(0, e.Item.Width - layout.HorizontalPadding * 2),
            e.Item.Height);
        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            textBounds,
            e.Item.ForeColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding);
    }

    public static void ApplyRoundedRegion(Control control, int radius)
    {
        var bounds = control.ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var oldRegion = control.Region;
        using var path = CreateRoundedPath(bounds, radius);
        control.Region = new Region(path);
        oldRegion?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
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
