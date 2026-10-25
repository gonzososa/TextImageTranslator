using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TextImageTranslator.Models.Dto;

namespace TextImageTranslator.Platform;

public interface IImageRecognition
{
    Task<RecognitionResult> RecognizeAsync (byte[]? imageBytes, CancellationToken cancellationToken);
}

public class ImageRecognition : IImageRecognition
{
    private readonly ILogger<ImageRecognition> _logger;
    private readonly IConfiguration _configuration;

    public ImageRecognition (ILogger<ImageRecognition> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<RecognitionResult> RecognizeAsync (byte[]? imageBytes, CancellationToken cancellationToken)
    {
        _logger.LogInformation ("Recognizing image...");

        if (imageBytes == null)
        {
            return new RecognitionResult ([]);
        }

        var visionClient = new ImageAnalysisClient (
            new Uri (_configuration["VisionEndpoint"]),
            new AzureKeyCredential (_configuration["VisionKey"])
        );

        var binaryData = new BinaryData (imageBytes);
        var recognizeResult = await visionClient.AnalyzeAsync (binaryData, VisualFeatures.Read, cancellationToken: cancellationToken);

        List<RecognizedText> recognizedText = [];

        foreach (var block in recognizeResult.Value.Read.Blocks) 
        {
            foreach (var line in block.Lines)
            {
                var polygon = line.BoundingPolygon.Select (p => new Polygon (p.X, p.Y));
                recognizedText.Add (new RecognizedText (polygon, line.Text));
            }
        }

        return new RecognitionResult (recognizedText);
    }
}


