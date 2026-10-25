using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using TextImageTranslator.Functions;
using TextImageTranslator.Models.Dto;
using TextImageTranslator.Platform;

namespace TextImageTranslator.Tests;

public class ReadTextTests
{
    private const long MaxFileSizeBytes = 12 * 1024 * 1024;

    [Fact]
    public async Task Run_WithValidImage_ReturnsRecognitionResult()
    {
        var expected = new RecognitionResult([
            new RecognizedText([new Polygon(1, 2), new Polygon(3, 4)], "recognized line")
        ]);
        var recognition = new Mock<IImageRecognition>();
        var detection = new Mock<ISourceLanguageDetection>();
        recognition
            .Setup(service => service.RecognizeAsync(
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        detection
            .Setup(service => service.DetectAsync(
                It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { "recognized line" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("en");
        var function = CreateFunction(recognition, detection);
        var request = CreateRequest(CreateFile());

        var result = await function.Run(request, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var recognitionResult = Assert.IsType<RecognitionResult>(response.Value);
        Assert.Equal("en", recognitionResult.SourceLanguage);
        Assert.Equal(expected.RecognizedText, recognitionResult.RecognizedText);
        recognition.Verify(service => service.RecognizeAsync(
            It.Is<byte[]>(bytes => bytes.Length == 4),
            CancellationToken.None), Times.Once);
        detection.Verify(service => service.DetectAsync(
            It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { "recognized line" })),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Run_WithPngImage_ReturnsRecognitionResult()
    {
        var recognition = new Mock<IImageRecognition>();
        var detection = new Mock<ISourceLanguageDetection>();
        recognition
            .Setup(service => service.RecognizeAsync(
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecognitionResult([]));
        var function = CreateFunction(recognition, detection);

        var result = await function.Run(
            CreateRequest(CreateFile("image/png", [0x89, 0x50, 0x4e, 0x47])),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        detection.Verify(service => service.DetectAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_WhenLanguageDetectionFails_ReturnsOcrResultWithoutLanguage()
    {
        var expected = new RecognitionResult([
            new RecognizedText([new Polygon(1, 2)], "recognized line")
        ]);
        var recognition = new Mock<IImageRecognition>();
        recognition
            .Setup(service => service.RecognizeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var detection = new Mock<ISourceLanguageDetection>();
        detection
            .Setup(service => service.DetectAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("detector unavailable"));
        var function = CreateFunction(recognition, detection);

        var result = await function.Run(CreateRequest(CreateFile()), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result);
        var recognitionResult = Assert.IsType<RecognitionResult>(response.Value);
        Assert.Null(recognitionResult.SourceLanguage);
        Assert.Equal(expected.RecognizedText, recognitionResult.RecognizedText);
    }

    [Fact]
    public async Task Run_WithoutFile_ReturnsBadRequest()
    {
        var recognition = new Mock<IImageRecognition>();
        var function = CreateFunction(recognition);

        var result = await function.Run(CreateRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        recognition.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_WithOversizedFile_ReturnsBadRequest()
    {
        var recognition = new Mock<IImageRecognition>();
        var oversizedFile = new FormFile(new MemoryStream(), 0, MaxFileSizeBytes + 1, "file", "large.jpg")
        {
            Headers = new HeaderDictionary { ["Content-Type"] = "image/jpeg" }
        };
        var function = CreateFunction(recognition);

        var result = await function.Run(CreateRequest(oversizedFile), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        recognition.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_WithUnsupportedContentType_ReturnsBadRequest()
    {
        var recognition = new Mock<IImageRecognition>();
        var file = CreateFile();
        file.Headers["Content-Type"] = "application/octet-stream";
        var function = CreateFunction(recognition);

        var result = await function.Run(CreateRequest(file), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        recognition.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_WhenRecognitionIsCancelled_ReturnsRequestTimeout()
    {
        var recognition = new Mock<IImageRecognition>();
        recognition
            .Setup(service => service.RecognizeAsync(
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var function = CreateFunction(recognition);

        var result = await function.Run(CreateRequest(CreateFile()), CancellationToken.None);

        Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status408RequestTimeout, ((StatusCodeResult)result).StatusCode);
    }

    [Fact]
    public async Task Run_WhenRecognitionFails_ReturnsInternalServerError()
    {
        var recognition = new Mock<IImageRecognition>();
        recognition
            .Setup(service => service.RecognizeAsync(
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("service unavailable"));
        var function = CreateFunction(recognition);

        var result = await function.Run(CreateRequest(CreateFile()), CancellationToken.None);

        Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, ((StatusCodeResult)result).StatusCode);
    }

    private static ReadText CreateFunction(
        Mock<IImageRecognition> recognition,
        Mock<ISourceLanguageDetection>? detection = null)
    {
        if (detection is null)
        {
            detection = new Mock<ISourceLanguageDetection>();
            detection
                .Setup(service => service.DetectAsync(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
        }

        return new ReadText(NullLogger<ReadText>.Instance, recognition.Object, detection.Object);
    }

    private static FormFile CreateFile(string contentType = "image/jpeg", byte[]? contents = null)
    {
        var stream = new MemoryStream(contents ?? [0xff, 0xd8, 0xff, 0xe0]);
        var fileName = contentType == "image/png" ? "image.png" : "image.jpg";
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary { ["Content-Type"] = contentType }
        };
    }

    private static HttpRequest CreateRequest(IFormFile? file = null)
    {
        var context = new DefaultHttpContext();
        var files = new FormFileCollection();
        if (file is not null)
        {
            files.Add(file);
        }

        context.Request.ContentType = "multipart/form-data";
        context.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>(),
            files);
        return context.Request;
    }
}
