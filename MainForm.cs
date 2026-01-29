using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using GenerativeAI;
using GenerativeAI.Live;

namespace SipAiGateway;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly Label _statusLabel;
    private readonly TextBox _logBox;
    private SipGateway? _gateway;
    private bool _consoleReady;

    public MainForm(AppSettings settings)
    {
        _settings = settings;
        _settings.Changed += (_, _) => UpdateStatus();

        Text = "ENVOY.Gemlink-SIP";
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var statusPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = SystemColors.ControlLight,
            Padding = new Padding(12, 8, 12, 8)
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            AutoSize = false
        };
        statusPanel.Controls.Add(_statusLabel);

        var leftPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 180,
            BackColor = SystemColors.ControlLight
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 12, 12, 12),
            AutoScroll = true
        };

        var testApiButton = new Button
        {
            Text = "TEST API",
            Width = 140,
            Height = 36
        };
        testApiButton.Click += async (_, _) => await RunApiTestAsync(testApiButton);
        buttonsPanel.Controls.Add(testApiButton);

        for (int i = 2; i <= 6; i++)
        {
            var button = new Button
            {
                Text = $"Placeholder {i}",
                Width = 140,
                Height = 36,
                Enabled = false
            };
            buttonsPanel.Controls.Add(button);
        }

        var settingsButton = new Button
        {
            Text = "Settings",
            Width = 140,
            Height = 36
        };
        settingsButton.Click += (_, _) => ShowSettings();
        buttonsPanel.Controls.Add(settingsButton);

        leftPanel.Controls.Add(buttonsPanel);

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
            BackColor = Color.Black,
            ForeColor = Color.LightGreen
        };

        Controls.Add(_logBox);
        Controls.Add(leftPanel);
        Controls.Add(statusPanel);

        Load += async (_, _) => await InitializeGatewayAsync();
        FormClosing += (_, _) => _gateway?.Dispose();

        UpdateStatus();
    }

    private async Task InitializeGatewayAsync()
    {
        if (_settings.IsFirstLaunch)
        {
            var result = MessageBox.Show(
                this,
                "It looks like this is your first launch. Run the setup wizard now?",
                "First Launch",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                using var wizard = new SetupWizardForm(_settings);
                var wizardResult = wizard.ShowDialog(this);
                if (wizardResult == DialogResult.OK)
                {
                    _settings.MarkFirstLaunchComplete();
                }
            }
        }

        Console.SetOut(new TextBoxWriter(_logBox));
        Console.SetError(new TextBoxWriter(_logBox));
        _consoleReady = true;

        Console.WriteLine("Initializing SIP gateway...");
        Console.WriteLine($"SIP listening on {Config.LocalIp}:{Config.SipPort}");
        Console.WriteLine($"SIP contact: sip:{Config.ContactUser}@{Config.LocalIp}:{Config.SipPort}");
        LogStatus();

        _gateway = new SipGateway(_settings);
        _gateway.Start();
    }

    private async Task RunApiTestAsync(Button sourceButton)
    {
        sourceButton.Enabled = false;
        try
        {
            Console.WriteLine("Running Gemini API test...");

            GoogleAi googleAi = string.IsNullOrWhiteSpace(_settings.GeminiApiKey)
                ? new GoogleAi()
                : new GoogleAi(_settings.GeminiApiKey);

            var modelName = GeminiModelCatalog.Normalize(_settings.GeminiModel);
            if (GeminiModelCatalog.IsNativeAudioModel(modelName))
            {
                await RunLiveAudioTestAsync(modelName, _settings.GeminiApiKey, _settings.GeminiSystemPrompt);
            }
            else
            {
                var model = googleAi.CreateGenerativeModel(modelName);
                var response = await model.GenerateContentAsync(
                    "Reply with a short readiness confirmation for the SIP gateway."
                );
                var text = response.Text();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine($"Gemini API test succeeded: {text.Trim()}");
                }
                else
                {
                    Console.WriteLine("Gemini API test succeeded, but no text was returned.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Gemini API test failed: {ex.Message}");
        }
        finally
        {
            sourceButton.Enabled = true;
        }
    }

    private static async Task RunLiveAudioTestAsync(string modelName, string apiKey, string systemPrompt)
    {
        try
        {
            Console.WriteLine("Gemini Live test: attempting to connect...");
            var resolvedKey = string.IsNullOrWhiteSpace(apiKey)
                ? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                : apiKey;
            if (string.IsNullOrWhiteSpace(resolvedKey))
            {
                Console.WriteLine("Gemini Live test failed: API key is not set.");
                return;
            }

            var resolvedPrompt = string.IsNullOrWhiteSpace(systemPrompt)
                ? AppSettings.DefaultGeminiSystemPrompt
                : systemPrompt.Trim();
            var liveClient = new MultiModalLiveClient(
                platformAdapter: new GoogleAIPlatformAdapter(resolvedKey),
                modelName: modelName,
                systemInstruction: resolvedPrompt
            );

            await liveClient.ConnectAsync();
            Console.WriteLine("Gemini Live test succeeded: connected.");
            await liveClient.DisconnectAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Gemini Live test failed: {ex.Message}");
        }
    }

    private void UpdateStatus()
    {
        var mode = _settings.TestMode ? "TEST" : "LIVE";
        var modeDetail = _settings.TestMode
            ? "accepts any SIP caller"
            : "expected SIP endpoints only";

        _statusLabel.Text = $"Status: Ready\nMode: {mode} ({modeDetail})\nListening: {Config.LocalIp}:{Config.SipPort}";
        LogStatus();
    }

    private void LogStatus()
    {
        if (!_consoleReady)
        {
            return;
        }

        var mode = _settings.TestMode ? "TEST" : "LIVE";
        var modeDetail = _settings.TestMode
            ? "accepts any SIP caller"
            : "expected SIP endpoints only";

        Console.WriteLine($"Mode: {mode} ({modeDetail})");
    }

    private void ShowSettings()
    {
        using var window = new SettingsForm(_settings);
        window.ShowDialog(this);
    }
}
