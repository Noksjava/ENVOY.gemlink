using System;
using System.IO;
using System.Text.Json;

namespace SipAiGateway;

public sealed class AppSettings
{
    public const string DefaultGeminiSystemPrompt = "You are a helpful assistant for SIP voice calls."; // overridden by prompt in the field? 
    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "settings.json");
    private static readonly string SetupMarkerPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "setup.complete");

    private bool _testMode = true;     // echo mode only during test
    private string _geminiApiKey = string.Empty;
    private string _geminiEndpoint = "wss://generativelanguage.googleapis.com/v1beta/live:connect";
    private string _geminiModel = GeminiModelCatalog.DefaultModel;
    private string _geminiSystemPrompt = DefaultGeminiSystemPrompt;
    private string _geminiVoice = GeminiVoiceCatalog.DefaultVoice;
    private int _sipPort = Config.SipPort;
    private bool _isFirstLaunch = true;
    private bool _suspendSave;

    public event EventHandler? Changed;

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        settings.LoadFromDisk();
        return settings;
    }

    public bool IsFirstLaunch => _isFirstLaunch;

    public bool TestMode
    {
        get => _testMode;
        set
        {
            if (_testMode == value) return;
            _testMode = value;
            OnChanged();
        }
    }

    public string GeminiApiKey
    {
        get => _geminiApiKey;
        set
        {
            value ??= string.Empty;
            if (_geminiApiKey == value) return;
            _geminiApiKey = value;
            OnChanged();
        }
    }

    public string GeminiEndpoint
    {
        get => _geminiEndpoint;
        set
        {
            value ??= string.Empty;
            if (_geminiEndpoint == value) return;
            _geminiEndpoint = value;
            OnChanged();
        }
    }

    public string GeminiModel
    {
        get => _geminiModel;
        set
        {
            value ??= string.Empty;
            if (_geminiModel == value) return;
            _geminiModel = value;
            OnChanged();
        }
    }

    public string GeminiSystemPrompt
    {
        get => _geminiSystemPrompt;
        set
        {
            value ??= string.Empty;
            if (_geminiSystemPrompt == value) return;
            _geminiSystemPrompt = value;
            OnChanged();
        }
    }

    public string GeminiVoice
    {
        get => _geminiVoice;
        set
        {
            value ??= string.Empty;
            var normalized = GeminiVoiceCatalog.GetVoice(value).Name;
            if (_geminiVoice == normalized) return;
            _geminiVoice = normalized;
            OnChanged();
        }
    }

    public int SipPort
    {
        get => _sipPort;
        set
        {
            var normalized = value;
            if (normalized is < 1 or > 65535)
            {
                normalized = Config.SipPort;
            }

            if (_sipPort == normalized) return;
            _sipPort = normalized;
            OnChanged();
        }
    }

    public void MarkFirstLaunchComplete()
    {
        if (!_isFirstLaunch) return;
        TryWriteSetupMarker();
        _isFirstLaunch = false;
        Save();
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        Save();
    }

    private void LoadFromDisk()
    {
        _isFirstLaunch = !File.Exists(SetupMarkerPath);

        if (!File.Exists(SettingsPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var state = JsonSerializer.Deserialize<SettingsState>(json);
            if (state == null)
            {
                _isFirstLaunch = !File.Exists(SetupMarkerPath);
                return;
            }

            _suspendSave = true;
            _testMode = state.TestMode;
            _geminiApiKey = state.GeminiApiKey ?? string.Empty;
            _geminiEndpoint = state.GeminiEndpoint ?? _geminiEndpoint;
            _geminiModel = state.GeminiModel ?? _geminiModel;
            _geminiSystemPrompt = state.GeminiSystemPrompt ?? _geminiSystemPrompt;
            _geminiVoice = GeminiVoiceCatalog.GetVoice(state.GeminiVoice ?? _geminiVoice).Name;
            if (state.SipPort is > 0 and <= 65535)
            {
                _sipPort = state.SipPort.Value;
            }
            _suspendSave = false;
        }
        catch
        {
            _isFirstLaunch = !File.Exists(SetupMarkerPath);
        }
    }

    private void Save()
    {
        if (_suspendSave)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var state = new SettingsState
            {
                TestMode = _testMode,
                GeminiApiKey = _geminiApiKey,
                GeminiEndpoint = _geminiEndpoint,
                GeminiModel = _geminiModel,
                GeminiSystemPrompt = _geminiSystemPrompt,
                GeminiVoice = _geminiVoice,
                SipPort = _sipPort
            };
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Swallow persistence failures to avoid crashing the app.
        }
    }

    private void TryWriteSetupMarker()
    {
        try
        {
            File.WriteAllText(SetupMarkerPath, DateTime.UtcNow.ToString("O"));
        }
        catch
        {
            // Ignore marker creation failures.
        }
    }

    private sealed class SettingsState
    {
        public bool TestMode { get; set; }
        public string? GeminiApiKey { get; set; }
        public string? GeminiEndpoint { get; set; }
        public string? GeminiModel { get; set; }
        public string? GeminiSystemPrompt { get; set; }
        public string? GeminiVoice { get; set; }
        public int? SipPort { get; set; }
    }
}
