using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TextImageTranslator.Platform;

namespace TextImageTranslator.Tests;

public class SourceLanguageDetectionTests
{
    [Fact]
    public async Task DetectAsync_ReturnsDominantLanguageAndSendsTranslatorAuthentication()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var client = CreateClient(async (request, _) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse("""[{"language":"en","score":0.99},{"language":"zh-Hans","score":0.98}]""");
        });
        var detector = CreateDetector(client);

        var result = await detector.DetectAsync(["Hello there", "你好"], CancellationToken.None);

        Assert.Equal("en", result);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal(
            "https://translator.cognitiveservices.azure.com/translator/text/v3.0/detect?api-version=3.0",
            capturedRequest.RequestUri!.ToString());
        Assert.Equal("secret", capturedRequest.Headers.GetValues("Ocp-Apim-Subscription-Key").Single());
        using var requestBody = JsonDocument.Parse(capturedBody!);
        Assert.Equal("Hello there", requestBody.RootElement[0].GetProperty("Text").GetString());
    }

    [Fact]
    public async Task DetectAsync_ReturnsMixedWhenTwoLanguagesHaveSignificantText()
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse("""[{"language":"en","score":0.99},{"language":"ja","score":0.99}]""")));
        var detector = CreateDetector(client);

        var result = await detector.DetectAsync(["Hello", "こんにちは"], CancellationToken.None);

        Assert.Equal("Mixed", result);
    }

    [Fact]
    public async Task DetectAsync_ReturnsUnknownWhenConfidenceIsLow()
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse("""[{"language":"en","score":0.3}]""")));
        var detector = CreateDetector(client);

        var result = await detector.DetectAsync(["Hi"], CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task DetectAsync_WithNoLettersDoesNotCallTranslator()
    {
        var handlerCalled = false;
        var client = CreateClient((_, _) =>
        {
            handlerCalled = true;
            return Task.FromResult(JsonResponse("[]"));
        });
        var detector = CreateDetector(client);

        var result = await detector.DetectAsync(["123 ？！"], CancellationToken.None);

        Assert.Null(result);
        Assert.False(handlerCalled);
    }

    private static SourceLanguageDetection CreateDetector(HttpClient client)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TranslatorEndpoint"] = "https://translator.cognitiveservices.azure.com/",
                ["TranslatorKey"] = "secret"
            })
            .Build();
        return new SourceLanguageDetection(client, configuration);
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) =>
        new(new StubHttpMessageHandler(sendAsync));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            sendAsync(request, cancellationToken);
    }
}
