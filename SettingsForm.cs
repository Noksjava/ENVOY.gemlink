using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SipAiGateway;

public sealed class SettingsForm : Form
{
    private readonly CheckBox _testModeCheckBox;
    private readonly TextBox _geminiEndpointTextBox;
    private readonly TextBox _geminiApiKeyTextBox;
    private readonly ComboBox _geminiModelComboBox;
    private readonly ComboBox _geminiVoiceComboBox;
    private readonly TextBox _geminiSystemPromptTextBox;
    private readonly Button _fetchModelsButton;
    private readonly NumericUpDown _sipPortInput;
    private readonly CheckBox _autoLaunchCheckBox;
    private readonly CheckBox _launchInTrayCheckBox;
    private readonly Button _saveButton;
    private readonly Label _statusLabel;
    private readonly AppSettings _settings;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "Settings";
        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
        {
            Icon = appIcon;
        }
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 480);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 11,
            Padding = new Padding(16),
            AutoScroll = true
        };

        var geminiLabel = new Label
        {
            Text = "Gemini Live",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        var geminiPanel = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 5,
            AutoSize = true,
            Dock = DockStyle.Top
        };
        geminiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        geminiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var endpointLabel = new Label
        {
            Text = "Endpoint",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _geminiEndpointTextBox = new TextBox
        {
            Width = 360,
            Text = _settings.GeminiEndpoint,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        var apiKeyLabel = new Label
        {
            Text = "API Key",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _geminiApiKeyTextBox = new TextBox
        {
            Width = 360,
            Text = _settings.GeminiApiKey,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            UseSystemPasswordChar = true
        };

        var modelLabel = new Label
        {
            Text = "Model",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _geminiModelComboBox = new ComboBox
        {
            Width = 360,
            Text = _settings.GeminiModel,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDown
        };

        var voiceLabel = new Label
        {
            Text = "Voice",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _geminiVoiceComboBox = new ComboBox
        {
            Width = 360,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _geminiVoiceComboBox.Items.AddRange(GeminiVoiceCatalog.Voices.ToArray());
        var selectedVoice = GeminiVoiceCatalog.GetVoice(_settings.GeminiVoice);
        _geminiVoiceComboBox.SelectedItem = GeminiVoiceCatalog.Voices
            .FirstOrDefault(voice => voice.Name == selectedVoice.Name)
            ?? GeminiVoiceCatalog.Voices.FirstOrDefault();

        var promptLabel = new Label
        {
            Text = "System prompt",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _geminiSystemPromptTextBox = new TextBox
        {
            Width = 360,
            Text = _settings.GeminiSystemPrompt,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        _fetchModelsButton = new Button
        {
            Text = "Fetch Models",
            Width = 120
        };
        _fetchModelsButton.Click += async (_, _) => await LoadModelsAsync();

        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray
        };

        geminiPanel.Controls.Add(endpointLabel, 0, 0);
        geminiPanel.Controls.Add(_geminiEndpointTextBox, 1, 0);
        geminiPanel.Controls.Add(apiKeyLabel, 0, 1);
        geminiPanel.Controls.Add(_geminiApiKeyTextBox, 1, 1);
        geminiPanel.Controls.Add(modelLabel, 0, 2);
        geminiPanel.Controls.Add(_geminiModelComboBox, 1, 2);
        geminiPanel.Controls.Add(voiceLabel, 0, 3);
        geminiPanel.Controls.Add(_geminiVoiceComboBox, 1, 3);
        geminiPanel.Controls.Add(promptLabel, 0, 4);
        geminiPanel.Controls.Add(_geminiSystemPromptTextBox, 1, 4);
        geminiPanel.Controls.Add(_fetchModelsButton, 1, 5);
        geminiPanel.Controls.Add(_statusLabel, 0, 6);
        geminiPanel.SetColumnSpan(_statusLabel, 2);
        geminiPanel.RowCount = 7;

        var sipLabel = new Label
        {
            Text = "SIP",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        var sipPanel = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Dock = DockStyle.Top
        };
        sipPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sipPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var sipPortLabel = new Label
        {
            Text = "Port",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _sipPortInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 65535,
            Value = _settings.SipPort,
            Width = 120,
            Anchor = AnchorStyles.Left
        };

        sipPanel.Controls.Add(sipPortLabel, 0, 0);
        sipPanel.Controls.Add(_sipPortInput, 1, 0);

        var startupLabel = new Label
        {
            Text = "Startup",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        var startupPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            Dock = DockStyle.Top
        };

        _autoLaunchCheckBox = new CheckBox
        {
            Text = "Launch at Windows startup",
            Checked = _settings.AutoLaunchEnabled,
            AutoSize = true
        };

        _launchInTrayCheckBox = new CheckBox
        {
            Text = "Launch in system tray",
            Checked = _settings.LaunchInTray,
            AutoSize = true
        };

        startupPanel.Controls.Add(_autoLaunchCheckBox, 0, 0);
        startupPanel.Controls.Add(_launchInTrayCheckBox, 0, 1);

        _testModeCheckBox = new CheckBox
        {
            Text = "Test mode",
            Checked = _settings.TestMode,
            AutoSize = true
        };
        _testModeCheckBox.CheckedChanged += (_, _) => UpdateToggleState();

        _saveButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Width = 100
        };
        _saveButton.Click += (_, _) => SaveSettings();

        var closeButton = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.Cancel,
            Width = 100
        };

        var footerPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 40
        };
        footerPanel.Controls.Add(closeButton);
        footerPanel.Controls.Add(_saveButton);

        layout.Controls.Add(geminiLabel, 0, 0);
        layout.Controls.Add(geminiPanel, 0, 1);
        layout.Controls.Add(new Label { Text = "", Height = 12 }, 0, 2);
        layout.Controls.Add(sipLabel, 0, 3);
        layout.Controls.Add(sipPanel, 0, 4);
        layout.Controls.Add(new Label { Text = "", Height = 12 }, 0, 5);
        layout.Controls.Add(startupLabel, 0, 6);
        layout.Controls.Add(startupPanel, 0, 7);
        layout.Controls.Add(new Label { Text = "", Height = 12 }, 0, 8);
        layout.Controls.Add(_testModeCheckBox, 0, 9);
        layout.Controls.Add(footerPanel, 0, 10);

        Controls.Add(layout);

        UpdateToggleState();
    }

    private void UpdateToggleState()
    {
        bool testMode = _testModeCheckBox.Checked;
        bool canEditGemini = !testMode;
        _geminiEndpointTextBox.Enabled = canEditGemini;
        _geminiApiKeyTextBox.Enabled = canEditGemini;
        _geminiModelComboBox.Enabled = canEditGemini;
        _geminiVoiceComboBox.Enabled = canEditGemini;
        _geminiSystemPromptTextBox.Enabled = canEditGemini;
        _fetchModelsButton.Enabled = canEditGemini;
        _statusLabel.Enabled = canEditGemini;
        if (canEditGemini)
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

        _fetchModelsButton.Enabled = false;
        _statusLabel.Text = "Fetching models...";

        try
        {
            var modelNames = (await GeminiModelFetcher.FetchAsync(_geminiApiKeyTextBox.Text.Trim()))
                .ToList();

            modelNames = modelNames.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToList();
            _geminiModelComboBox.BeginUpdate();
            _geminiModelComboBox.Items.Clear();
            _geminiModelComboBox.Items.AddRange(modelNames.ToArray());
            _geminiModelComboBox.EndUpdate();

            if (modelNames.Count > 0)
            {
                var current = _geminiModelComboBox.Text;
                if (!string.IsNullOrWhiteSpace(current) && modelNames.Contains(current))
                {
                    _geminiModelComboBox.SelectedItem = current;
                }
                else
                {
                    _geminiModelComboBox.SelectedIndex = 0;
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
            _fetchModelsButton.Enabled = !_testModeCheckBox.Checked;
        }
    }

    private void SaveSettings()
    {
        _settings.TestMode = _testModeCheckBox.Checked;
        _settings.GeminiEndpoint = _geminiEndpointTextBox.Text;
        _settings.GeminiApiKey = _geminiApiKeyTextBox.Text;
        _settings.GeminiModel = _geminiModelComboBox.Text.Trim();
        if (_geminiVoiceComboBox.SelectedItem is VoiceInfo selectedVoice)
        {
            _settings.GeminiVoice = selectedVoice.Name;
        }
        _settings.GeminiSystemPrompt = _geminiSystemPromptTextBox.Text.Trim();
        _settings.SipPort = (int)_sipPortInput.Value;
        _settings.AutoLaunchEnabled = _autoLaunchCheckBox.Checked;
        _settings.LaunchInTray = _launchInTrayCheckBox.Checked;
    }
}
