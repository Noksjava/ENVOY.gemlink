using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SipAiGateway;

public sealed class SetupWizardForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _apiKeyTextBox;
    private readonly ComboBox _modelComboBox;
    private readonly ComboBox _voiceComboBox;
    private readonly TextBox _systemPromptTextBox;
    private readonly CheckBox _testModeCheckBox;
    private readonly CheckBox _autoLaunchCheckBox;
    private readonly CheckBox _launchInTrayCheckBox;
    private readonly Label _statusLabel;
    private readonly Button _fetchButton;
    private readonly Button _saveButton;

    public SetupWizardForm(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Text = "Setup Wizard";
        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
        {
            Icon = appIcon;
        }
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 400);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 11,
            Padding = new Padding(16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var introLabel = new Label
        {
            Text = "Configure Gemini access / test mode",
            AutoSize = true
        };
        layout.Controls.Add(introLabel, 0, 0);
        layout.SetColumnSpan(introLabel, 2);

        _testModeCheckBox = new CheckBox
        {
            Text = "Use TEST mode (no API calls required)",
            Checked = _settings.TestMode,
            AutoSize = true
        };
        _testModeCheckBox.CheckedChanged += (_, _) => UpdateToggleState();
        layout.Controls.Add(_testModeCheckBox, 0, 1);
        layout.SetColumnSpan(_testModeCheckBox, 2);

        var apiKeyLabel = new Label
        {
            Text = "API Key",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _apiKeyTextBox = new TextBox
        {
            Width = 360,
            Text = _settings.GeminiApiKey,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            UseSystemPasswordChar = true
        };
        layout.Controls.Add(apiKeyLabel, 0, 2);
        layout.Controls.Add(_apiKeyTextBox, 1, 2);

        var modelLabel = new Label
        {
            Text = "Model",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _modelComboBox = new ComboBox
        {
            Width = 360,
            Text = _settings.GeminiModel,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDown
        };
        layout.Controls.Add(modelLabel, 0, 3);
        layout.Controls.Add(_modelComboBox, 1, 3);

        var promptLabel = new Label
        {
            Text = "System prompt",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _systemPromptTextBox = new TextBox
        {
            Width = 360,
            Text = _settings.GeminiSystemPrompt,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        layout.Controls.Add(promptLabel, 0, 4);
        layout.Controls.Add(_systemPromptTextBox, 1, 4);

        var voiceLabel = new Label
        {
            Text = "Voice",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _voiceComboBox = new ComboBox
        {
            Width = 360,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _voiceComboBox.Items.AddRange(GeminiVoiceCatalog.Voices.ToArray());
        var selectedVoice = GeminiVoiceCatalog.GetVoice(_settings.GeminiVoice);
        _voiceComboBox.SelectedItem = GeminiVoiceCatalog.Voices
            .FirstOrDefault(voice => voice.Name == selectedVoice.Name)
            ?? GeminiVoiceCatalog.Voices.FirstOrDefault();
        layout.Controls.Add(voiceLabel, 0, 5);
        layout.Controls.Add(_voiceComboBox, 1, 5);

        var startupLabel = new Label
        {
            Text = "Startup",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };
        layout.Controls.Add(startupLabel, 0, 6);
        layout.SetColumnSpan(startupLabel, 2);

        _autoLaunchCheckBox = new CheckBox
        {
            Text = "Launch at Windows startup",
            Checked = _settings.AutoLaunchEnabled,
            AutoSize = true
        };
        layout.Controls.Add(_autoLaunchCheckBox, 0, 7);
        layout.SetColumnSpan(_autoLaunchCheckBox, 2);

        _launchInTrayCheckBox = new CheckBox
        {
            Text = "Launch in system tray",
            Checked = _settings.LaunchInTray,
            AutoSize = true
        };
        layout.Controls.Add(_launchInTrayCheckBox, 0, 8);
        layout.SetColumnSpan(_launchInTrayCheckBox, 2);

        _fetchButton = new Button
        {
            Text = "Fetch Models",
            Width = 120
        };
        _fetchButton.Click += async (_, _) => await LoadModelsAsync();
        layout.Controls.Add(_fetchButton, 1, 9);

        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray
        };
        layout.Controls.Add(_statusLabel, 0, 10);
        layout.SetColumnSpan(_statusLabel, 2);

        var footerPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(8)
        };

        _saveButton = new Button
        {
            Text = "Save",
            Width = 100,
            DialogResult = DialogResult.OK
        };
        _saveButton.Click += (_, _) => SaveSettings();

        var cancelButton = new Button
        {
            Text = "Skip",
            Width = 100,
            DialogResult = DialogResult.Cancel
        };

        footerPanel.Controls.Add(_saveButton);
        footerPanel.Controls.Add(cancelButton);

        Controls.Add(layout);
        Controls.Add(footerPanel);

        Shown += async (_, _) => await LoadModelsAsync();

        UpdateToggleState();
    }

    private void UpdateToggleState()
    {
        bool testMode = _testModeCheckBox.Checked;
        _apiKeyTextBox.Enabled = !testMode;
        _modelComboBox.Enabled = !testMode;
        _voiceComboBox.Enabled = !testMode;
        _systemPromptTextBox.Enabled = !testMode;
        _fetchButton.Enabled = !testMode;
        if (!testMode)
        {
            _statusLabel.Text = string.Empty;
        }
    }

    private async Task LoadModelsAsync()
    {
        if (_testModeCheckBox.Checked)
        {
            _statusLabel.Text = "Test mode enabled; model fetch skipped.";
            return;
        }

        _fetchButton.Enabled = false;
        _statusLabel.Text = "Fetching models...";

        try
        {
            var modelNames = (await GeminiModelFetcher.FetchAsync(_apiKeyTextBox.Text.Trim()))
                .ToList();

            modelNames = modelNames.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToList();
            _modelComboBox.BeginUpdate();
            _modelComboBox.Items.Clear();
            _modelComboBox.Items.AddRange(modelNames.ToArray());
            _modelComboBox.EndUpdate();

            if (modelNames.Count > 0)
            {
                var current = _modelComboBox.Text;
                if (!string.IsNullOrWhiteSpace(current) && modelNames.Contains(current))
                {
                    _modelComboBox.SelectedItem = current;
                }
                else
                {
                    _modelComboBox.SelectedIndex = 0;
                }
            }

            _statusLabel.Text = modelNames.Count > 0
                ? $"Loaded {modelNames.Count} models."
                : "No models returned. Check credentials and try again.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Failed to fetch models: {ex.Message}";
        }
        finally
        {
            _fetchButton.Enabled = !_testModeCheckBox.Checked;
        }
    }

    private void SaveSettings()
    {
        _settings.TestMode = _testModeCheckBox.Checked;
        _settings.GeminiApiKey = _apiKeyTextBox.Text;
        _settings.GeminiModel = _modelComboBox.Text.Trim();
        if (_voiceComboBox.SelectedItem is VoiceInfo selectedVoice)
        {
            _settings.GeminiVoice = selectedVoice.Name;
        }
        _settings.GeminiSystemPrompt = _systemPromptTextBox.Text.Trim();
        _settings.AutoLaunchEnabled = _autoLaunchCheckBox.Checked;
        _settings.LaunchInTray = _launchInTrayCheckBox.Checked;
    }
}
