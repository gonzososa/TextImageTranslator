using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TextImageTranslator.Functions;
using TextImageTranslator.Models.Dto;
using TextImageTranslator.Platform;

namespace TextImageTranslator.Tests;

public class TranslateTests
{
    [Fact]
    public async Task Run_WithValidRequest_ReturnsTranslationResult()
    {
        var request = new TranslationRequest(["hello"], "es");
        var expected = new TranslationResult([new Translation("hello", "hola", "en")]);
        var translation = new Mock<ITexTranslation>();
        translation
            .Setup(service => service.TranslateAsync(
                It.IsAny<IEnumerable<string>>(),
                "es",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var function = CreateFunction(translation);

        var result = await function.Run(CreateRequest(JsonSerializer.Serialize(request)), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, response.Value);
        translation.Verify(service => service.TranslateAsync(
            It.Is<IEnumerable<string>>(chunks => chunks.SequenceEqual(new[] { "hello" })),
            "es",
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Run_WithEmptyBody_ReturnsBadRequest()
    {
        var translation = new Mock<ITexTranslation>();
        var function = CreateFunction(translation);

        var result = await function.Run(CreateRequest(string.Empty), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        translation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_WithMalformedJson_ReturnsBadRequest()
    {
        var translation = new Mock<ITexTranslation>();
        var function = CreateFunction(translation);

        var result = await function.Run(CreateRequest("{"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        translation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_WithMissingTextChunks_ReturnsBadRequest()
    {
        var translation = new Mock<ITexTranslation>();
        var function = CreateFunction(translation);

        var result = await function.Run(CreateRequest("""{"targetLanguage":"en"}"""), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        translation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_WithInvalidLanguageCode_ReturnsBadRequest()
    {
        var translation = new Mock<ITexTranslation>();
        var function = CreateFunction(translation);

        var result = await function.Run(
            CreateRequest("""{"textChunks":["hello"],"targetLanguage":"eng"}"""),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        translation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_WhenTranslationIsCancelled_ReturnsRequestTimeout()
    {
        var translation = new Mock<ITexTranslation>();
        translation
            .Setup(service => service.TranslateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var function = CreateFunction(translation);

        var result = await function.Run(
            CreateRequest("""{"textChunks":["hello"],"targetLanguage":"en"}"""),
            CancellationToken.None);

        Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status408RequestTimeout, ((StatusCodeResult)result).StatusCode);
    }

    [Fact]
    public async Task Run_WhenTranslationFails_ReturnsInternalServerError()
    {
        var translation = new Mock<ITexTranslation>();
        translation
            .Setup(service => service.TranslateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("service unavailable"));
        var function = CreateFunction(translation);

        var result = await function.Run(
            CreateRequest("""{"textChunks":["hello"],"targetLanguage":"en"}"""),
            CancellationToken.None);

        Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, ((StatusCodeResult)result).StatusCode);
    }

    private static Translate CreateFunction(Mock<ITexTranslation> translation) =>
        new(NullLogger<Translate>.Instance, translation.Object);

    private static HttpRequest CreateRequest(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        return context.Request;
    }
}
