using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SipAiGateway;

public static class GeminiModelFetcher
{
    private static readonly HttpClient HttpClient = new();
    private const string ModelsEndpoint = "https://generativelanguage.googleapis.com/v1beta/models";

    public static async Task<IReadOnlyList<string>> FetchAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        var resolvedApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
            : apiKey;

        if (string.IsNullOrWhiteSpace(resolvedApiKey))
        {
            throw new InvalidOperationException("API key is required to fetch models.");
        }

        var models = new List<string>();
        string? pageToken = null;

        do
        {
            var url = BuildUrl(resolvedApiKey, pageToken);
            using var response = await HttpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.TryGetProperty("models", out var modelsElement) &&
                modelsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var modelElement in modelsElement.EnumerateArray())
                {
                    if (modelElement.TryGetProperty("name", out var nameElement))
                    {
                        var name = nameElement.GetString();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        models.Add(name);
                    }
                }
            }

            pageToken = null;
            if (document.RootElement.TryGetProperty("nextPageToken", out var tokenElement))
            {
                pageToken = tokenElement.GetString();
            }
        } while (!string.IsNullOrWhiteSpace(pageToken));

        return models;
    }

    private static string BuildUrl(string apiKey, string? pageToken)
    {
        var url = $"{ModelsEndpoint}?key={Uri.EscapeDataString(apiKey)}";
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
        }

        return url;
    }
}
