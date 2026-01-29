using System;
using System.Collections.Generic;
using System.Linq;

namespace SipAiGateway;

public static class GeminiModelCatalog
{
    public const string DefaultModel = "models/gemini-2.5-flash-native-audio-latest";

    private static readonly string[] Suggested = {
        "models/gemini-2.5-flash-native-audio-latest",
        "models/gemini-live-2.5-flash",
        "models/gemini-live-2.5-pro",
        "models/gemini-live-2.0-flash",
        "models/gemini-live-2.0-flash-exp",
        "models/gemini-live-2.0-flash-preview",
        "models/gemini-live-2.0-pro-exp",
        "models/gemini-live-1.5-flash",
        "models/gemini-live-1.5-flash-8b",
        "models/gemini-live-1.5-pro",
        "models/gemini-3-flash-live-preview",
        "models/gemini-3-flash-preview",
        "models/gemini-2.5-pro",
        "models/gemini-2.5-flash",
        "models/gemini-2.5-flash-lite",
        "models/gemini-2.0-pro",
        "models/gemini-2.0-pro-exp",
        "models/gemini-2.0-flash",
        "models/gemini-2.0-flash-exp",
        "models/gemini-2.0-flash-lite",
        "models/gemini-1.5-pro",
        "models/gemini-1.5-flash",
        "models/gemini-1.5-flash-8b",
        "models/gemini-1.0-pro",
        "models/gemini-1.0-pro-vision",
        "models/gemini-pro",
        "models/gemini-pro-vision"
    };

    public static IReadOnlyList<string> SuggestedModels =>
        Suggested.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

    public static bool IsNativeAudioModel(string modelName) =>
        modelName.Contains("native-audio", StringComparison.OrdinalIgnoreCase) ||
        modelName.Contains("live", StringComparison.OrdinalIgnoreCase) ||
        modelName.Contains("audio", StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string modelName)
    {
        var normalized = string.IsNullOrWhiteSpace(modelName)
            ? DefaultModel
            : modelName.Trim();

        if (normalized.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("projects/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
        {
            return $"models/{normalized}";
        }

        return normalized;
    }

    public static string NormalizeForLive(string modelName)
    {
        var normalized = Normalize(modelName);
        return normalized.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? normalized["models/".Length..]
            : normalized;
    }
}
