using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class TrayWindow : Form
{
    private static readonly Color Background = Color.FromArgb(29, 30, 34);
    private static readonly Color Panel = Color.FromArgb(39, 41, 47);
    private static readonly Color Muted = Color.FromArgb(160, 165, 176);

    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MysticLightControl", "settings.json");
    private readonly SettingsStore settingsStore;
    private readonly ToolTip presetToolTip = new() { ShowAlways = true };
    private readonly FluentSlider brightness = new();
    private readonly FluentSlider effectSpeed = new();
    private readonly Label brightnessValue = new() { AutoSize = true, ForeColor = Color.FromArgb(190, 196, 207) };
    private readonly Label effectSpeedValue = new() { AutoSize = true, ForeColor = Color.FromArgb(190, 196, 207) };
    private readonly FlowLayoutPanel palette = new() { AutoSize = true, WrapContents = true, Width = 278, Height = 34 };
    private readonly Label hex = new() { AutoSize = true, ForeColor = Color.FromArgb(190, 196, 207) };
    private readonly ComboBox modePicker = new()
    {
        Width = 188, DropDownStyle = ComboBoxStyle.DropDownList,
        BackColor = Panel, ForeColor = Color.White, IntegralHeight = false, Height = 28
    };
    private readonly FlowLayoutPanel colorSection = new()
    {
        Width = 278, AutoSize = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Margin = new Padding(0), BackColor = Background
    };
    private readonly Panel speedSection = new() { Width = 278, Height = 57, BackColor = Background, Visible = false };
    private readonly FlowLayoutPanel gradientSection = new()
    {
        Width = 278, Height = 68, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Margin = new Padding(0), BackColor = Background, Visible = false
    };
    private readonly Button gradientStartButton = new() { Width = 128, Height = 25, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
    private readonly Button gradientEndButton = new() { Width = 128, Height = 25, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
    private readonly Button editLedsButton = new() { Width = 92, Height = 25, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Text = "Светодиоды…" };
    private readonly Label gradientInfo = new() { AutoSize = false, Width = 278, Height = 16, ForeColor = Muted };
    private readonly ToggleSwitch power = new();
    private readonly Label connection = new()
    {
        Text = "●  Не проверено", Font = new Font("Segoe UI", 8), ForeColor = Muted,
        AutoSize = true, Location = new Point(143, 11)
    };
    private readonly FlowLayoutPanel presetRow = new()
    {
        Width = 278, Height = 38, WrapContents = false, Margin = new Padding(0)
    };
    private readonly System.Windows.Forms.Timer brightnessTimer = new() { Interval = 220 };
    private readonly System.Windows.Forms.Timer speedTimer = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer settingsSaveTimer = new() { Interval = 450 };

    private LumaSettings settings;
    private NotifyIcon tray = null!;
    private Icon trayIcon = null!;
    private TrayWheelListener? trayWheel;
    private bool loading;
    private bool exiting;
    private bool applyRunning;
    private bool applyAgain;
    private bool brightnessSyncRunning;
    private DateTime lastBrightnessSyncUtc = DateTime.MinValue;
    private bool canSaveSettings = true;
    private readonly bool preview;
    private DeviceCapabilities? capabilities;
    private Task<DeviceCapabilities>? capabilityLoadTask;
    private bool capabilitiesLoaded;
    private bool effectColorAvailable;
    private int pendingSpeedPercent;

    public TrayWindow(bool preview = false)
    {
        this.preview = preview;
        settingsStore = new SettingsStore(settingsPath);
        var loaded = settingsStore.Load();
        settings = loaded.Settings;
        effectColorAvailable = settings.EffectColorControlAvailable;
        canSaveSettings = loaded.State is SettingsLoadState.Missing
            or SettingsLoadState.Loaded
            or SettingsLoadState.CorruptBackedUp;
        settings.Normalize();
        if (loaded.Warning is not null) WriteLog("Настройки: " + loaded.Warning);

        ConfigureWindow();
        BuildInterface();
        InitializeControlValues();
        WireControlEvents();
        CreateTrayIcon();

        Shown += OnWindowShown;
        FormClosing += OnWindowClosing;
        Deactivate += (_, _) =>
        {
            if (Visible && !this.preview) Hide();
        };
        if (this.preview) TopMost = true;
    }

    private void ConfigureWindow()
    {
        Text = "Luma";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = preview;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(320, 450);
        BackColor = Background;
        ForeColor = Color.White;
        DoubleBuffered = true;
    }

    private void BuildInterface()
    {
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(20, 16, 20, 12),
            WrapContents = false,
            BackColor = Background
        };
        Controls.Add(root);

        root.Controls.Add(BuildHeader());
        power.ForeColor = Color.White;
        power.Margin = new Padding(0, 12, 0, 5);
        root.Controls.Add(power);
        root.Controls.Add(BuildModeSection());
        BuildColorPalette();
        colorSection.Controls.Add(BuildColorHeader());
        colorSection.Controls.Add(palette);
        colorSection.Controls.Add(BuildColorPickerRow());
        root.Controls.Add(colorSection);
        root.Controls.Add(BuildSpeedSection());
        root.Controls.Add(BuildGradientSection());
        root.Controls.Add(BuildBrightnessSection());
        root.Controls.Add(BuildPresetSection());
    }

    private Control BuildModeSection()
    {
        var section = new FlowLayoutPanel
        {
            Width = 278, Height = 38, WrapContents = false, Margin = new Padding(0),
            BackColor = Background, Anchor = AnchorStyles.Left
        };
        section.Controls.Add(new Label
        {
            Text = "РЕЖИМ", AutoSize = false, Width = 74, Height = 28,
            ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 8)
        });
        modePicker.Items.Add(new ModeOption(LightingModes.Static, "Статический цвет"));
        modePicker.DisplayMember = nameof(ModeOption.Text);
        modePicker.SelectedIndex = 0;
        section.Controls.Add(modePicker);
        return section;
    }

    private Control BuildHeader()
    {
        var header = new Panel { Width = 278, Height = 34, BackColor = Background };
        var title = new Label
        {
            Text = "◉  Luma", Font = new Font("Segoe UI Semibold", 15), ForeColor = Color.White,
            AutoSize = true, Location = new Point(0, 2)
        };
        var settingsButton = new Button
        {
            Text = "⚙", AccessibleName = "Настройки Luma", AccessibleDescription = "Показать информацию о настройках",
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, ForeColor = Muted,
            BackColor = Background, Size = new Size(28, 28), Location = new Point(248, 0)
        };
        settingsButton.FlatAppearance.BorderSize = 0;
        settingsButton.Click += (_, _) => MessageBox.Show(
            "Настройки Luma сохраняются автоматически.", "Luma", MessageBoxButtons.OK, MessageBoxIcon.Information);
        header.Controls.AddRange([title, connection, settingsButton]);
        return header;
    }

    private static Control BuildColorHeader()
    {
        var header = new Panel
        {
            Width = 278, Height = 13, BackColor = Background,
            Margin = new Padding(0, 8, 0, 8)
        };
        header.Controls.Add(new Label
        {
            Text = "ЦВЕТ", Font = new Font("Segoe UI Semibold", 8), ForeColor = Muted,
            AutoSize = true, Location = new Point(0, 0)
        });
        header.Controls.Add(new Label
        {
            Text = "ПОСЛЕДНИЕ", Font = new Font("Segoe UI", 7), ForeColor = Muted,
            AutoSize = false, Size = new Size(100, 13), Location = new Point(178, 0),
            TextAlign = ContentAlignment.MiddleRight
        });
        return header;
    }

    private void BuildColorPalette()
    {
        for (var i = 0; i < 8; i++)
        {
            var swatch = new Button
            {
                Width = 25, Height = 25, FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false, Margin = new Padding(0, 0, 9, 7),
                Cursor = Cursors.Hand
            };
            swatch.Click += (_, _) => ChooseColor((string)swatch.Tag!);
            palette.Controls.Add(swatch);
        }
        UpdateColorPalette();
    }

    private Control BuildColorPickerRow()
    {
        var button = new Button
        {
            Text = "Выбрать цвет", AccessibleName = "Выбрать произвольный цвет",
            AccessibleDescription = "Открывает системный диалог выбора цвета",
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, BackColor = Panel,
            ForeColor = Color.White, Width = 112, Height = 28, Margin = new Padding(0, 0, 12, 5)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(65, 68, 76);
        button.Click += ChooseCustomColor;

        hex.Text = settings.Color.ToUpperInvariant();
        var row = new FlowLayoutPanel
        {
            Width = 278, Height = 34, WrapContents = false,
            BackColor = Background, Margin = new Padding(0)
        };
        row.Controls.Add(button);
        row.Controls.Add(hex);
        return row;
    }

    private Control BuildBrightnessSection()
    {
        var section = new Panel { Width = 278, Height = 54, BackColor = Background, Margin = new Padding(0, 11, 0, 0) };
        var heading = new FlowLayoutPanel
        {
            Width = 278, Height = 22, WrapContents = false,
            Margin = new Padding(0, 11, 0, 0), BackColor = Background
        };
        heading.Controls.Add(new Label { Text = "Яркость", AutoSize = true, ForeColor = Color.White });
        brightnessValue.Margin = new Padding(170, 0, 0, 0);
        heading.Controls.Add(brightnessValue);
        section.Controls.Add(heading);
        brightness.Location = new Point(0, 22);
        section.Controls.Add(brightness);
        return section;
    }

    private Control BuildSpeedSection()
    {
        var heading = new FlowLayoutPanel
        {
            Width = 278, Height = 20, WrapContents = false,
            Margin = new Padding(0), BackColor = Background
        };
        heading.Controls.Add(new Label { Text = "Скорость эффекта", AutoSize = true, ForeColor = Color.White });
        effectSpeedValue.Margin = new Padding(119, 0, 0, 0);
        heading.Controls.Add(effectSpeedValue);
        speedSection.Controls.Add(heading);
        effectSpeed.Location = new Point(0, 21);
        effectSpeed.AccessibleName = "Скорость эффекта";
        speedSection.Controls.Add(effectSpeed);
        return speedSection;
    }

    private Control BuildGradientSection()
    {
        gradientSection.Controls.Add(new Label
        {
            Text = "ГРАДИЕНТ · порядок светодиодов SDK", AutoSize = true,
            ForeColor = Muted, Font = new Font("Segoe UI Semibold", 8)
        });
        var colors = new FlowLayoutPanel
        {
            Width = 278, Height = 30, WrapContents = false,
            Margin = new Padding(0, 3, 0, 0), BackColor = Background
        };
        gradientStartButton.Text = "Начало";
        gradientEndButton.Text = "Конец";
        gradientStartButton.Width = 82;
        gradientEndButton.Width = 82;
        foreach (var button in new[] { gradientStartButton, gradientEndButton, editLedsButton })
        {
            button.BackColor = Panel;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = Color.FromArgb(65, 68, 76);
            button.Margin = new Padding(0, 0, 5, 0);
        }
        gradientStartButton.Click += (_, _) => ChooseGradientEndpoint(start: true);
        gradientEndButton.Click += (_, _) => ChooseGradientEndpoint(start: false);
        editLedsButton.Click += (_, _) => EditLedColors();
        colors.Controls.Add(gradientStartButton);
        colors.Controls.Add(gradientEndButton);
        colors.Controls.Add(editLedsButton);
        gradientSection.Controls.Add(colors);
        gradientInfo.Margin = new Padding(0, 2, 0, 0);
        gradientSection.Controls.Add(gradientInfo);
        return gradientSection;
    }

    private Control BuildPresetSection()
    {
        var section = new FlowLayoutPanel
        {
            Width = 278, Height = 59, WrapContents = false,
            FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 8, 0, 0),
            BackColor = Background
        };
        var heading = new Panel { Width = 278, Height = 13, BackColor = Background };
        heading.Controls.Add(new Label
        {
            Text = "ПРЕСЕТЫ", Font = new Font("Segoe UI Semibold", 8), ForeColor = Muted,
            AutoSize = true, Location = new Point(0, 0)
        });
        heading.Controls.Add(new Label
        {
            Text = "ПКМ — сохранить или изменить", Font = new Font("Segoe UI", 7), ForeColor = Muted,
            AutoSize = false, Size = new Size(172, 13), Location = new Point(106, 0),
            TextAlign = ContentAlignment.MiddleRight
        });
        section.Controls.Add(heading);

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            var preset = new ColorPresetButton
            {
                Title = $"Цвет {i + 1}", AccessibleName = $"Пресет цвета {i + 1}",
                AccessibleDescription = "ЛКМ применяет режим, цвет и параметры пресета; контекстное меню сохраняет или изменяет его",
                SwatchColor = SafeColor(settings.Presets[i]), Tag = i,
                Font = new Font("Segoe UI", 7.5f), Margin = new Padding(0, 0, 8, 0)
            };
            preset.Clicked += (_, _) => _ = ApplyPresetAsync(index);
            preset.ContextMenuStrip = CreatePresetMenu(index);
            presetToolTip.SetToolTip(preset, "ЛКМ — применить · ПКМ — сохранить или изменить");
            presetRow.Controls.Add(preset);
        }
        section.Controls.Add(presetRow);
        return section;
    }

    private ContextMenuStrip CreatePresetMenu(int index)
    {
        var menu = new ContextMenuStrip { BackColor = Panel, ForeColor = Color.White, ShowImageMargin = false };
        menu.Items.Add("Сохранить текущий цвет", null, (_, _) => SavePreset(index));
        menu.Items.Add("Задать цвет пресета…", null, (_, _) => ChoosePresetColor(index));
        return menu;
    }

    private void InitializeControlValues()
    {
        loading = true;
        brightness.Value = Math.Clamp(settings.Brightness, 0, 100);
        effectSpeed.Value = Math.Clamp(settings.SpeedPercent, 0, 100);
        power.SetChecked(settings.Enabled);
        pendingSpeedPercent = settings.SpeedPercent;
        loading = false;
        UpdateBrightnessLabel();
        UpdateSpeedLabel();
        UpdatePresetSelection();
        UpdateModeSections();
    }

    private void WireControlEvents()
    {
        brightnessTimer.Tick += (_, _) =>
        {
            brightnessTimer.Stop();
            if (settings.Enabled) Apply();
        };
        settingsSaveTimer.Tick += (_, _) =>
        {
            settingsSaveTimer.Stop();
            Save();
        };
        brightness.ValueChanged += (_, _) =>
        {
            if (loading) return;
            settings.Brightness = brightness.Value;
            UpdateBrightnessLabel();
            ScheduleSave();
            brightnessTimer.Stop();
            brightnessTimer.Start();
        };
        power.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            settings.Enabled = power.Checked;
            ScheduleSave(immediate: true);
            Apply();
        };
        modePicker.SelectionChangeCommitted += async (_, _) => await ApplySelectedModeAsync();
        modePicker.DropDown += (_, _) =>
        {
            if (!capabilitiesLoaded)
            {
                modePicker.DroppedDown = false;
                _ = LoadCapabilitiesAsync();
            }
        };
        effectSpeed.ValueChanged += (_, _) =>
        {
            if (loading) return;
            pendingSpeedPercent = effectSpeed.Value;
            UpdateSpeedLabel();
            speedTimer.Stop();
            speedTimer.Start();
        };
        speedTimer.Tick += async (_, _) =>
        {
            speedTimer.Stop();
            await ApplyEffectSpeedAsync();
        };
    }

    private void CreateTrayIcon()
    {
        trayIcon = new Icon(Path.Combine(AppContext.BaseDirectory, "Luma.ico"));
        tray = new NotifyIcon { Text = "Luma — управление подсветкой", Icon = trayIcon, Visible = true };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) TogglePanel();
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowPanel());
        menu.Items.Add("Выход", null, (_, _) =>
        {
            exiting = true;
            Close();
        });
        tray.ContextMenuStrip = menu;

        try
        {
            trayWheel = new TrayWheelListener(tray, WriteLog);
            trayWheel.Wheel += ChangeBrightnessFromTray;
        }
        catch (Exception ex)
        {
            WriteLog("Tray wheel initialization failed: " + ex);
        }
    }

    private void ChangeBrightnessFromTray(int delta)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((Action)(() =>
            {
                if (!IsDisposed) brightness.Value = Math.Clamp(brightness.Value + delta * 10, 0, 100);
            }));
        }
        catch (InvalidOperationException ex)
        {
            WriteLog("Tray wheel callback: " + ex.Message);
        }
    }

    private void ChooseCustomColor(object? sender, EventArgs e)
    {
        using var dialog = new ColorDialog
        {
            Color = SafeColor(settings.Color),
            FullOpen = true,
            CustomColors = settings.CustomColors.ToArray()
        };
        var result = dialog.ShowDialog(this);
        settings.CustomColors = dialog.CustomColors.Length == 16 ? dialog.CustomColors.ToArray() : new int[16];
        if (result == DialogResult.OK) ChooseColor(ToHex(dialog.Color));
        else Save();
    }

    private void ChooseColor(string code)
    {
        var normalized = ToHex(SafeColor(code));
        var selectedMode = modePicker.SelectedItem as ModeOption;
        var targetMode = selectedMode?.Mode ?? settings.Mode;
        if (targetMode == LightingModes.Effect)
        {
            var candidate = CreateSettingsSnapshot();
            candidate.Mode = LightingModes.Effect;
            candidate.EffectId = selectedMode?.EffectId ?? settings.EffectId;
            candidate.SpeedPercent = pendingSpeedPercent;
            candidate.RememberColor(normalized);
            _ = ApplyEffectColorCandidateAsync(candidate);
            return;
        }

        if (targetMode == LightingModes.Static && settings.Mode != LightingModes.Static)
        {
            var candidate = CreateSettingsSnapshot();
            candidate.Mode = LightingModes.Static;
            candidate.EffectId = string.Empty;
            candidate.RememberColor(normalized);
            _ = ApplyCandidateAsync(candidate, commitAll: false);
            return;
        }

        settings.RememberColor(normalized);
        Save();
        UpdateColorControls();
        UpdatePresetSelection();
        if (settings.Enabled) Apply();
    }

    private void SavePreset(int index)
    {
        if (index < 0 || settings.LightingPresets is null || index >= settings.LightingPresets.Length) return;
        settings.LightingPresets[index] = ToLightingPreset(settings);
        settings.Presets[index] = settings.LightingPresets[index].Color;
        Save();
        UpdatePresetSelection();
    }

    private void ChoosePresetColor(int index)
    {
        if (index < 0 || settings.LightingPresets is null || index >= settings.LightingPresets.Length) return;
        using var dialog = new ColorDialog
        {
            Color = SafeColor(settings.LightingPresets[index].Color),
            FullOpen = true,
            CustomColors = settings.CustomColors.ToArray()
        };
        var result = dialog.ShowDialog(this);
        settings.CustomColors = dialog.CustomColors.Length == 16 ? dialog.CustomColors.ToArray() : new int[16];
        if (result == DialogResult.OK)
        {
            var preset = settings.LightingPresets[index].Copy();
            preset.Color = ToHex(dialog.Color);
            settings.LightingPresets[index] = preset;
            settings.Presets[index] = preset.Color;
            Save();
            UpdatePresetSelection();
        }
        else Save();
    }

    private async Task LoadCapabilitiesAsync()
    {
        if (capabilitiesLoaded || IsDisposed) return;
        if (capabilityLoadTask is null)
        {
            SetConnectionStatus("Чтение режимов устройства…", connected: false);
            capabilityLoadTask = Task.Run(MysticLightController.LoadCapabilities);
        }

        try
        {
            capabilities = await capabilityLoadTask;
            if (IsDisposed) return;
            capabilitiesLoaded = true;
            foreach (var warning in capabilities.Warnings) WriteLog("Возможности Mystic Light: " + warning);
            foreach (var zone in capabilities.Zones)
                foreach (var warning in zone.Warnings)
                    WriteLog($"{zone.Name}: {warning}");

            PopulateModePicker();
            if (capabilities.Zones.Count == 0)
            {
                SetConnectionStatus("Режимы недоступны · статический цвет", connected: false);
                return;
            }

            SetConnectionStatus($"Подключено · {capabilities.Zones.Count} зон", connected: true);
        }
        catch (Exception ex)
        {
            capabilityLoadTask = null;
            WriteLog("Не удалось получить режимы Mystic Light: " + ex);
            SetConnectionStatus("Не удалось получить режимы", connected: false);
        }
    }

    private void PopulateModePicker()
    {
        loading = true;
        modePicker.Items.Clear();
        modePicker.Items.Add(new ModeOption(LightingModes.Static, "Статический цвет"));
        if (capabilities is not null)
        {
            foreach (var effect in capabilities.SharedEffects)
                modePicker.Items.Add(new ModeOption(LightingModes.Effect, effect.Name, effect.Id));
            if (capabilities.SupportsDirectLighting)
                modePicker.Items.Add(new ModeOption(LightingModes.DirectLighting, "Direct Lighting Control"));
        }

        var selected = FindModeOption(settings.Mode, settings.EffectId);
        effectColorAvailable = settings.EffectColorControlAvailable;
        if (selected is null && settings.Mode != LightingModes.Static)
        {
            var result = MessageBox.Show(this,
                $"Сохранённый режим «{settings.EffectId}» недоступен на подключённом устройстве. Переключить Luma на статический цвет?",
                "Luma", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result == DialogResult.Yes)
            {
                settings.Mode = LightingModes.Static;
                settings.EffectId = string.Empty;
                Save();
            }
            selected = FindModeOption(LightingModes.Static, null);
        }

        modePicker.SelectedItem = selected ?? modePicker.Items[0];
        loading = false;
        UpdateModeSections();
    }

    private ModeOption? FindModeOption(string mode, string? effectId) =>
        modePicker.Items.Cast<ModeOption>().FirstOrDefault(option =>
            option.Mode == mode && (mode != LightingModes.Effect ||
                string.Equals(option.EffectId, effectId, StringComparison.OrdinalIgnoreCase)));

    private void UpdateModeSections()
    {
        var option = modePicker.SelectedItem as ModeOption;
        var isStatic = option?.Mode == LightingModes.Static;
        var isEffect = option?.Mode == LightingModes.Effect;
        var isDirect = option?.Mode == LightingModes.DirectLighting;
        colorSection.Visible = isStatic || (isEffect && effectColorAvailable);
        speedSection.Visible = isEffect && capabilities?.FindEffect(option?.EffectId)?.SupportsSpeed == true;
        gradientSection.Visible = isDirect;
        if (capabilities is not null)
        {
            var ledCount = capabilities.Zones.Where(zone => zone.SupportsDirectLighting).Sum(zone => zone.Leds.Count);
            gradientInfo.Text = $"{ledCount} LED · порядок SDK; геометрия неизвестна";
        }
        gradientStartButton.Text = $"Начало  {settings.GradientStart}";
        gradientEndButton.Text = $"Конец  {settings.GradientEnd}";
        gradientStartButton.BackColor = SafeColor(settings.GradientStart);
        gradientEndButton.BackColor = SafeColor(settings.GradientEnd);
        gradientStartButton.ForeColor = ContrastText(SafeColor(settings.GradientStart));
        gradientEndButton.ForeColor = ContrastText(SafeColor(settings.GradientEnd));
    }

    private void UpdateColorControls()
    {
        hex.Text = settings.Color.ToUpperInvariant();
        UpdateColorPalette();
    }

    private void UpdateSpeedLabel()
    {
        effectSpeedValue.Text = $"{effectSpeed.Value}%";
        effectSpeed.AccessibleDescription = $"Скорость эффекта {effectSpeed.Value} процентов от максимального уровня SDK.";
    }

    private async Task ApplySelectedModeAsync()
    {
        if (loading || modePicker.SelectedItem is not ModeOption option) return;
        var candidate = CreateSettingsSnapshot();
        candidate.Mode = option.Mode;
        candidate.EffectId = option.Mode == LightingModes.Effect ? option.EffectId ?? string.Empty : string.Empty;
        candidate.SpeedPercent = option.Mode == LightingModes.Effect && settings.Mode != LightingModes.Effect
            ? capabilities?.InitialSpeedPercent ?? settings.SpeedPercent
            : pendingSpeedPercent;
        await ApplyCandidateAsync(candidate, commitAll: false);
    }

    private async Task ApplyEffectColorCandidateAsync(LumaSettings candidate)
    {
        var result = await ApplyCandidateToDeviceAsync(candidate);
        if (result is null) return;
        if (!result.ModeApplied)
        {
            SelectModeFromSettings();
            UpdateModeSections();
            ReportApplyResult(result, "Эффект не удалось применить.");
            return;
        }

        settings.Mode = LightingModes.Effect;
        settings.EffectId = candidate.EffectId;
        if (!result.ColorControlAvailable)
        {
            effectColorAvailable = false;
            settings.EffectColorControlAvailable = false;
            Save();
            SelectModeFromSettings();
            UpdateModeSections();
            ReportApplyResult(result, "Цветовой параметр этого эффекта недоступен.");
            return;
        }

        settings.Color = candidate.Color;
        settings.RecentColors = candidate.RecentColors;
        effectColorAvailable = true;
        settings.EffectColorControlAvailable = true;
        Save();
        SelectModeFromSettings();
        UpdateColorControls();
        UpdatePresetSelection();
        UpdateModeSections();
        ReportApplyResult(result, "Цвет эффекта применён.");
    }

    private async Task ApplyCandidateAsync(LumaSettings candidate, bool commitAll)
    {
        var result = await ApplyCandidateToDeviceAsync(candidate);
        if (result is null) return;
        if (result.SpeedControlRejected) DisableSpeedForEffect(candidate.EffectId);
        if (!result.ModeApplied)
        {
            SelectModeFromSettings();
            UpdateModeSections();
            ReportApplyResult(result, "Новый режим не применён; предыдущее состояние сохранено.");
            return;
        }

        if (commitAll) CopyPresetToSettings(candidate);
        else
        {
            settings.Mode = candidate.Mode;
            settings.EffectId = candidate.EffectId;
            settings.GradientStart = candidate.GradientStart;
            settings.GradientEnd = candidate.GradientEnd;
            if (candidate.Mode == LightingModes.DirectLighting)
                settings.LedColors = new Dictionary<string, string>(candidate.LedColors, StringComparer.OrdinalIgnoreCase);
            if (result.SpeedChanged == result.ZonesFound && result.ZonesFound > 0)
                settings.SpeedPercent = candidate.SpeedPercent;
        }
        effectColorAvailable = candidate.Mode == LightingModes.Effect && result.ColorControlAvailable;
        settings.EffectColorControlAvailable = effectColorAvailable;
        pendingSpeedPercent = settings.SpeedPercent;
        Save();
        loading = true;
        brightness.Value = settings.Brightness;
        power.SetChecked(settings.Enabled);
        effectSpeed.Value = settings.SpeedPercent;
        SelectModeFromSettings();
        loading = false;
        UpdateColorControls();
        UpdateBrightnessLabel();
        UpdateSpeedLabel();
        UpdatePresetSelection();
        UpdateModeSections();
        ReportApplyResult(result, "Режим применён.");
    }

    private async Task<SdkApplyResult?> ApplyCandidateToDeviceAsync(LumaSettings candidate)
    {
        if (candidate.Mode != LightingModes.Static && !capabilitiesLoaded)
            await LoadCapabilitiesAsync();
        try
        {
            return await Task.Run(() => MysticLightController.ApplySettings(candidate, capabilities));
        }
        catch (Exception ex)
        {
            WriteLog("Ошибка применения режима: " + ex);
            if (ex is SdkBusyException)
                SetConnectionStatus("SDK занят — повторите действие", connected: false);
            else
                SetConnectionStatus("Ошибка связи", connected: false);
            SelectModeFromSettings();
            UpdateModeSections();
            return null;
        }
    }

    private async Task ApplyEffectSpeedAsync()
    {
        if (settings.Mode != LightingModes.Effect || capabilities is null ||
            capabilities.FindEffect(settings.EffectId)?.SupportsSpeed != true) return;
        var requested = pendingSpeedPercent;
        try
        {
            var result = await Task.Run(() => MysticLightController.ApplyEffectSpeed(capabilities, requested));
            if (IsDisposed) return;
            if (result.SpeedControlRejected) DisableSpeedForEffect(settings.EffectId);
            if (result.HasErrors || result.SpeedChanged != result.ZonesFound)
            {
                loading = true;
                effectSpeed.Value = settings.SpeedPercent;
                loading = false;
                pendingSpeedPercent = settings.SpeedPercent;
                ReportApplyResult(result, "Не удалось изменить скорость эффекта.");
                return;
            }
            settings.SpeedPercent = requested;
            Save();
            SetConnectionStatus("Скорость применена", connected: true);
        }
        catch (Exception ex)
        {
            loading = true;
            effectSpeed.Value = settings.SpeedPercent;
            loading = false;
            pendingSpeedPercent = settings.SpeedPercent;
            WriteLog("Ошибка установки скорости эффекта: " + ex);
            SetConnectionStatus("Не удалось изменить скорость", connected: false);
        }
    }

    private async Task ApplyPresetAsync(int index)
    {
        if (settings.LightingPresets is null || index < 0 || index >= settings.LightingPresets.Length) return;
        var preset = settings.LightingPresets[index].Copy();
        if (preset.Mode != LightingModes.Static && !capabilitiesLoaded)
            await LoadCapabilitiesAsync();
        if (preset.Mode == LightingModes.Effect)
        {
            if (capabilities?.FindEffect(preset.EffectId) is null)
            {
                var useStatic = MessageBox.Show(this,
                    $"Эффект «{preset.EffectId}» из пресета недоступен. Применить пресет как статический цвет?",
                    "Luma", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (useStatic != DialogResult.Yes) return;
                preset.Mode = LightingModes.Static;
                preset.EffectId = string.Empty;
            }
        }
        else if (preset.Mode == LightingModes.DirectLighting && capabilities?.SupportsDirectLighting != true)
        {
            MessageBox.Show(this, "Direct Lighting Control недоступен для этого устройства. Пресет не применён.",
                "Luma", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var candidate = CreateSettingsSnapshot();
        candidate.Color = preset.Color;
        candidate.Brightness = preset.Brightness;
        candidate.Enabled = preset.Enabled;
        candidate.Mode = preset.Mode;
        candidate.EffectId = preset.EffectId;
        candidate.SpeedPercent = preset.SpeedPercent;
        candidate.GradientStart = preset.GradientStart;
        candidate.GradientEnd = preset.GradientEnd;
        candidate.LedColors = new Dictionary<string, string>(preset.LedColors, StringComparer.OrdinalIgnoreCase);
        candidate.RememberColor(preset.Color);
        await ApplyCandidateAsync(candidate, commitAll: true);
    }

    private void ChooseGradientEndpoint(bool start)
    {
        var oldColor = start ? settings.GradientStart : settings.GradientEnd;
        using var dialog = new ColorDialog { Color = SafeColor(oldColor), FullOpen = true, CustomColors = settings.CustomColors.ToArray() };
        var result = dialog.ShowDialog(this);
        settings.CustomColors = dialog.CustomColors.Length == 16 ? dialog.CustomColors.ToArray() : new int[16];
        if (result != DialogResult.OK) { Save(); return; }
        var candidate = CreateSettingsSnapshot();
        candidate.LedColors.Clear();
        if (start) candidate.GradientStart = ToHex(dialog.Color);
        else candidate.GradientEnd = ToHex(dialog.Color);
        _ = ApplyCandidateAsync(candidate, commitAll: false);
    }

    private void EditLedColors()
    {
        if (capabilities?.SupportsDirectLighting != true) return;
        var candidate = CreateSettingsSnapshot();
        using var editor = new Form
        {
            Text = "Цвета светодиодов",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(390, 430),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Background,
            ForeColor = Color.White
        };
        var list = new FlowLayoutPanel
        {
            Location = new Point(12, 12), Size = new Size(366, 350),
            AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Background
        };
        editor.Controls.Add(list);
        foreach (var zone in capabilities.Zones)
        {
            list.Controls.Add(new Label
            {
                Text = zone.Name, Width = 340, Height = 24,
                ForeColor = Color.FromArgb(190, 196, 207),
                Font = new Font("Segoe UI Semibold", 9), Margin = new Padding(0, 6, 0, 2)
            });
            var defaults = MysticLightController.BuildGradient(SafeColor(settings.GradientStart), SafeColor(settings.GradientEnd), zone.Leds.Count);
            for (var i = 0; i < zone.Leds.Count; i++)
            {
                var led = zone.Leds[i];
                var key = LedColorKey(zone, led);
                var selected = candidate.LedColors.TryGetValue(key, out var saved) ? SafeColor(saved) : defaults[i];
                var row = new Panel { Width = 342, Height = 32, BackColor = Background, Margin = new Padding(0, 0, 0, 2) };
                row.Controls.Add(new Label
                {
                    Text = led.Name, Location = new Point(0, 3), Size = new Size(230, 24),
                    ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                });
                var colorButton = new Button
                {
                    Text = ToHex(selected), Tag = key, Location = new Point(238, 2), Size = new Size(100, 27),
                    BackColor = selected, ForeColor = ContrastText(selected), FlatStyle = FlatStyle.Flat,
                    UseVisualStyleBackColor = false
                };
                colorButton.FlatAppearance.BorderColor = Color.FromArgb(65, 68, 76);
                colorButton.Click += (_, _) =>
                {
                    using var picker = new ColorDialog { Color = colorButton.BackColor, FullOpen = true, CustomColors = settings.CustomColors.ToArray() };
                    if (picker.ShowDialog(editor) != DialogResult.OK) return;
                    settings.CustomColors = picker.CustomColors.Length == 16 ? picker.CustomColors.ToArray() : new int[16];
                    var hexColor = ToHex(picker.Color);
                    candidate.LedColors[(string)colorButton.Tag!] = hexColor;
                    colorButton.Text = hexColor;
                    colorButton.BackColor = picker.Color;
                    colorButton.ForeColor = ContrastText(picker.Color);
                };
                list.Controls.Add(row);
                row.Controls.Add(colorButton);
            }
        }

        var apply = new Button { Text = "Применить", DialogResult = DialogResult.OK, Location = new Point(274, 384), Size = new Size(104, 32), FlatStyle = FlatStyle.Flat };
        apply.BackColor = Color.FromArgb(42, 91, 160);
        apply.ForeColor = Color.White;
        editor.Controls.Add(apply);
        editor.AcceptButton = apply;
        if (editor.ShowDialog(this) != DialogResult.OK) { Save(); return; }
        _ = ApplyCandidateAsync(candidate, commitAll: false);
    }

    private static string LedColorKey(ZoneCapability zone, LedInfo led) =>
        $"{zone.DeviceType}|{zone.Index}|{led.AreaIndex}|{led.Name}";

    private void CopyPresetToSettings(LumaSettings source)
    {
        settings.Color = source.Color;
        settings.Brightness = source.Brightness;
        settings.Enabled = source.Enabled;
        settings.Mode = source.Mode;
        settings.EffectId = source.EffectId;
        settings.EffectColorControlAvailable = source.EffectColorControlAvailable;
        settings.SpeedPercent = source.SpeedPercent;
        settings.GradientStart = source.GradientStart;
        settings.GradientEnd = source.GradientEnd;
        settings.LedColors = new Dictionary<string, string>(source.LedColors, StringComparer.OrdinalIgnoreCase);
        settings.RecentColors = source.RecentColors.ToArray();
        settings.RememberColor(source.Color);
    }

    private static LightingPreset ToLightingPreset(LumaSettings source) => new()
    {
        Color = source.Color,
        Brightness = source.Brightness,
        Enabled = source.Enabled,
        Mode = source.Mode,
        EffectId = source.EffectId,
        SpeedPercent = source.SpeedPercent,
        GradientStart = source.GradientStart,
        GradientEnd = source.GradientEnd,
        LedColors = new Dictionary<string, string>(source.LedColors, StringComparer.OrdinalIgnoreCase)
    };

    private void SelectModeFromSettings()
    {
        var item = FindModeOption(settings.Mode, settings.EffectId);
        if (item is not null) modePicker.SelectedItem = item;
    }

    private void ReportApplyResult(SdkApplyResult result, string success)
    {
        if (result.Changes == 0 || !result.ModeApplied)
        {
            var details = string.Join(Environment.NewLine, result.Errors);
            SetConnectionStatus("Режим не применён", connected: false);
            WriteLog($"{success} Не применено: {details}");
            if (details.Length > 0 && !result.Errors.Any(error => error.Contains("SDK занят", StringComparison.OrdinalIgnoreCase)))
                MessageBox.Show(this, $"{success}\n\n{details}", "Luma", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (result.HasErrors)
        {
            SetConnectionStatus($"Частично · {result.Errors.Count} ошибок", connected: true);
            WriteLog($"{success} {string.Join(Environment.NewLine, result.Errors)}");
        }
        else SetConnectionStatus(success, connected: true);
    }

    private void DisableSpeedForEffect(string effectId)
    {
        if (capabilities is null) return;
        capabilities = capabilities with
        {
            SharedEffects = capabilities.SharedEffects
                .Select(effect => effect.Id.Equals(effectId, StringComparison.OrdinalIgnoreCase)
                    ? effect with { SupportsSpeed = false }
                    : effect)
                .ToArray()
        };
        UpdateModeSections();
        WriteLog($"Скорость отключена для эффекта «{effectId}»: SDK ответил NotSupported.");
    }

    private static Color ContrastText(Color color) =>
        (color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 150 ? Color.Black : Color.White;

    private void UpdateColorPalette()
    {
        for (var i = 0; i < palette.Controls.Count; i++)
        {
            if (palette.Controls[i] is not Button swatch) continue;
            if (i >= settings.RecentColors.Length)
            {
                swatch.Visible = false;
                continue;
            }

            var code = settings.RecentColors[i];
            var selected = string.Equals(code, settings.Color, StringComparison.OrdinalIgnoreCase);
            swatch.Visible = true;
            swatch.Tag = code;
            swatch.AccessibleName = $"Выбрать цвет {code}";
            swatch.AccessibleDescription = selected ? "Текущий цвет" : "Недавно выбранный цвет";
            swatch.BackColor = SafeColor(code);
            swatch.FlatAppearance.BorderColor = selected ? Color.White : Color.FromArgb(60, 65, 74);
            swatch.FlatAppearance.BorderSize = selected ? 2 : 1;
        }
    }

    private void UpdatePresetSelection()
    {
        foreach (ColorPresetButton button in presetRow.Controls)
        {
            var index = (int)(button.Tag ?? -1);
            if (index < 0 || index >= settings.Presets.Length) continue;
            button.SwatchColor = SafeColor(settings.Presets[index]);
            button.Selected = string.Equals(
                ToHex(SafeColor(settings.Presets[index])), ToHex(SafeColor(settings.Color)),
                StringComparison.OrdinalIgnoreCase);
            button.Invalidate();
        }
    }

    private void UpdateBrightnessLabel()
    {
        brightnessValue.Text = $"{brightness.Value}%";
        brightnessValue.AccessibleName = $"Яркость {brightness.Value} процентов";
        brightnessValue.AccessibleDescription = "Текущее значение яркости";
        brightness.AccessibleDescription = $"Текущая яркость {brightness.Value} процентов. Используйте стрелки, Home и End для изменения.";
    }

    private void TogglePanel()
    {
        if (Visible) Hide();
        else ShowPanel();
    }

    private void ShowPanel()
    {
        var cursor = Cursor.Position;
        var workArea = Screen.FromPoint(cursor).WorkingArea;
        Location = new Point(
            Math.Clamp(cursor.X - Width / 2, workArea.Left, workArea.Right - Width),
            Math.Clamp(cursor.Y - Height - 12, workArea.Top, workArea.Bottom - Height));
        Show();
        Activate();
        Invalidate(true);
        if (settings.Enabled && !applyRunning) _ = SyncBrightnessFromDevice();
    }

    private void ScheduleSave(bool immediate = false)
    {
        if (!canSaveSettings)
        {
            SetConnectionStatus("Настройки новой версии — только чтение", connected: false);
            return;
        }
        settingsSaveTimer.Stop();
        if (immediate) Save();
        else settingsSaveTimer.Start();
    }

    private void Save()
    {
        if (!canSaveSettings) return;
        var result = settingsStore.Save(settings);
        if (result.Success) return;
        WriteLog("Не удалось сохранить настройки: " + result.Error);
        SetConnectionStatus("Ошибка сохранения", connected: false);
    }

    private void OnWindowShown(object? sender, EventArgs e)
    {
        if (!preview) Hide();
    }

    private async Task SyncBrightnessFromDevice()
    {
        if (!settings.Enabled || brightnessSyncRunning || DateTime.UtcNow - lastBrightnessSyncUtc < TimeSpan.FromSeconds(5)) return;
        brightnessSyncRunning = true;
        lastBrightnessSyncUtc = DateTime.UtcNow;
        try
        {
            var actual = await Task.Run(MysticLightController.ReadBrightnessPercent);
            if (actual is null || IsDisposed) return;
            loading = true;
            settings.Brightness = actual.Value;
            brightness.Value = actual.Value;
            loading = false;
            UpdateBrightnessLabel();
            ScheduleSave(immediate: true);
            SetConnectionStatus("Подключено", connected: true);
        }
        catch (Exception ex)
        {
            loading = false;
            WriteLog("Не удалось прочитать яркость устройства: " + ex);
            if (ex is SdkBusyException) SetConnectionStatus("Устройство занято", connected: false);
        }
        finally
        {
            brightnessSyncRunning = false;
        }
    }

    private void WriteLog(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(settingsPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "errors.log"), $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException ex) { System.Diagnostics.Trace.WriteLine(ex); }
        catch (UnauthorizedAccessException ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }

    private void SetConnectionStatus(string text, bool connected)
    {
        connection.Text = "●  " + text;
        connection.ForeColor = connected ? Color.FromArgb(97, 190, 142) : Color.FromArgb(205, 150, 110);
    }

    private async void Apply()
    {
        if (applyRunning)
        {
            applyAgain = true;
            return;
        }

        applyRunning = true;
        try
        {
            if (settings.Mode != LightingModes.Static && !capabilitiesLoaded)
                await LoadCapabilitiesAsync();
            var snapshot = CreateSettingsSnapshot();
            SdkApplyResult result;
            try
            {
                result = await Task.Run(() => MysticLightController.ApplySettings(snapshot, capabilities));
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                WriteLog("Не удалось применить подсветку: " + ex);
                if (ex is SdkBusyException)
                {
                    SetConnectionStatus("SDK занят — повторите действие", connected: false);
                    return;
                }
                SetConnectionStatus("Ошибка связи", connected: false);
                MessageBox.Show(this,
                    "Не удалось применить подсветку.\n\n" + ex.Message + "\n\nПодробности сохранены в %LOCALAPPDATA%\\MysticLightControl\\errors.log",
                    "Luma", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (IsDisposed) return;
            if (result.SpeedControlRejected) DisableSpeedForEffect(settings.EffectId);
            if (settings.Mode == LightingModes.Effect && result.ModeApplied)
            {
                effectColorAvailable = result.ColorControlAvailable;
                settings.EffectColorControlAvailable = effectColorAvailable;
                Save();
                UpdateModeSections();
            }
            if (result.Changes == 0)
            {
                SetConnectionStatus("Нет изменений", connected: false);
                var details = string.Join(Environment.NewLine, result.Errors);
                WriteLog("Подсветка не применена: " + details);
                MessageBox.Show(this,
                    "SDK не изменил ни одной зоны.\n\n" + details + "\n\nПодробности сохранены в %LOCALAPPDATA%\\MysticLightControl\\errors.log",
                    "Luma", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (result.HasErrors)
            {
                SetConnectionStatus($"Частично · {result.Errors.Count} ошибок", connected: true);
                WriteLog($"Частичное применение: зоны={result.ZonesFound}, цвет={result.ColorChanged}, яркость={result.BrightnessChanged}, режим={result.StyleChanged}.{Environment.NewLine}{string.Join(Environment.NewLine, result.Errors)}");
            }
            else SetConnectionStatus("Подключено", connected: true);
        }
        finally
        {
            applyRunning = false;
            if (applyAgain && !IsDisposed)
            {
                applyAgain = false;
                Apply();
            }
        }
    }

    private LumaSettings CreateSettingsSnapshot() => new()
    {
        Color = settings.Color,
        Brightness = settings.Brightness,
        Enabled = settings.Enabled,
        Presets = settings.Presets.ToArray(),
        CustomColors = settings.CustomColors.ToArray(),
        RecentColors = settings.RecentColors.ToArray(),
        LightingPresets = settings.LightingPresets?.Select(preset => preset.Copy()).ToArray(),
        Mode = settings.Mode,
        EffectId = settings.EffectId,
        EffectColorControlAvailable = settings.EffectColorControlAvailable,
        SpeedPercent = settings.SpeedPercent,
        GradientStart = settings.GradientStart,
        GradientEnd = settings.GradientEnd,
        LedColors = new Dictionary<string, string>(settings.LedColors, StringComparer.OrdinalIgnoreCase),
        SchemaVersion = settings.SchemaVersion
    };

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
    }

    private void ApplyDarkTitleBar()
    {
        try
        {
            var dark = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
            var caption = (uint)(Background.R | (Background.G << 8) | (Background.B << 16));
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(uint));
            uint foreground = 0x00FFFFFF;
            DwmSetWindowAttribute(Handle, 36, ref foreground, sizeof(uint));
        }
        catch (Exception ex)
        {
            WriteLog("Не удалось настроить тёмный заголовок окна: " + ex.Message);
        }
    }

    private static Color SafeColor(string? code)
    {
        try { return ColorTranslator.FromHtml(string.IsNullOrWhiteSpace(code) ? "#4F8CFF" : code); }
        catch (ArgumentException) { return ColorTranslator.FromHtml("#4F8CFF"); }
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private sealed record ModeOption(string Mode, string Text, string? EffectId = null);

    private void OnWindowClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !exiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        settingsSaveTimer.Stop();
        Save();
        trayWheel?.Dispose();
        tray.Dispose();
        trayIcon.Dispose();
        brightnessTimer.Dispose();
        speedTimer.Dispose();
        settingsSaveTimer.Dispose();
        presetToolTip.Dispose();
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll", PreserveSig = true, EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
}
