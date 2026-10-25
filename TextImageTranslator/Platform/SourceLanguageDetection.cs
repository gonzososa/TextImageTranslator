using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace TextImageTranslator.Platform;

public interface ISourceLanguageDetection
{
    Task<string?> DetectAsync(IEnumerable<string> textChunks, CancellationToken cancellationToken);
}

public class SourceLanguageDetection : ISourceLanguageDetection
{
    private const int MaxBatchItems = 100;
    private const int MaxBatchCharacters = 10_000;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public SourceLanguageDetection(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<string?> DetectAsync(IEnumerable<string> textChunks, CancellationToken cancellationToken)
    {
        var candidates = textChunks
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => new DetectionInput(text, CountLetters(text)))
            .Where(input => input.LetterCount > 0)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var endpoint = _configuration["TranslatorEndpoint"];
        var key = _configuration["TranslatorKey"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Translator endpoint and key are required for source-language detection.");
        }

        var languageWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in CreateBatches(candidates))
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                CreateDetectionUri(endpoint));
            request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", key);

            var region = _configuration["TranslatorRegion"];
            if (!string.IsNullOrWhiteSpace(region))
            {
                request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Region", region);
            }

            request.Content = JsonContent.Create(batch.Select(input => new DetectRequest(input.Text)));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var results = await response.Content.ReadFromJsonAsync<List<DetectResponse>>(cancellationToken);
            if (results is null || results.Count != batch.Count)
            {
                throw new InvalidOperationException("Language detection response did not match the submitted text.");
            }

            for (var index = 0; index < results.Count; index++)
            {
                var detected = results[index];
                if (!string.IsNullOrWhiteSpace(detected.Language) && detected.Score >= 0.5)
                {
                    languageWeights.TryGetValue(detected.Language, out var weight);
                    languageWeights[detected.Language] = weight + batch[index].LetterCount;
                }
            }
        }

        if (languageWeights.Count == 0)
        {
            return null;
        }

        var ranked = languageWeights.OrderByDescending(pair => pair.Value).ToArray();
        var totalLetters = candidates.Sum(input => input.LetterCount);
        if ((double)ranked[0].Value / totalLetters < 0.5)
        {
            return null;
        }

        if (ranked.Length > 1 && (double)ranked[1].Value / totalLetters >= 0.2)
        {
            return "Mixed";
        }

        return ranked[0].Key;
    }

    private static IEnumerable<List<DetectionInput>> CreateBatches(List<DetectionInput> inputs)
    {
        var batch = new List<DetectionInput>();
        var characters = 0;

        foreach (var input in inputs)
        {
            if (batch.Count > 0 &&
                (batch.Count == MaxBatchItems || characters + input.Text.Length > MaxBatchCharacters))
            {
                yield return batch;
                batch = [];
                characters = 0;
            }

            var text = input.Text.Length > MaxBatchCharacters
                ? input.Text[..MaxBatchCharacters]
                : input.Text;
            var truncated = input with { Text = text };
            batch.Add(truncated);
            characters += text.Length;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private static int CountLetters(string text) =>
        text.EnumerateRunes().Count(System.Text.Rune.IsLetter);

    private static Uri CreateDetectionUri(string endpoint)
    {
        var endpointUri = new Uri(endpoint.EndsWith('/') ? endpoint : $"{endpoint}/");
        var path = endpointUri.Host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase)
            ? "translator/text/v3.0/detect?api-version=3.0"
            : "detect?api-version=3.0";
        return new Uri(endpointUri, path);
    }

    private sealed record DetectionInput(string Text, int LetterCount);

    private sealed record DetectRequest([property: JsonPropertyName("Text")] string Text);

    private sealed record DetectResponse(
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("score")] double Score);
}
