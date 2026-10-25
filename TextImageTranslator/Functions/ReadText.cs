using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TextImageTranslator.Platform;

namespace TextImageTranslator.Functions
{
    public class ReadText
    {
        private const long MaxFileSizeBytes = 12 * 1024 * 1024;
        private readonly ILogger<ReadText> _logger;
        private readonly IImageRecognition _imageRecognition;
        private readonly ISourceLanguageDetection _sourceLanguageDetection;

        public ReadText(
            ILogger<ReadText> logger,
            IImageRecognition imageRecognition,
            ISourceLanguageDetection sourceLanguageDetection)
        {
            _logger = logger;
            _imageRecognition = imageRecognition;
            _sourceLanguageDetection = sourceLanguageDetection;
        }

        [Function("ReadText")]
        public async Task<IActionResult> Run (
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
            CancellationToken cancellationToken
        )
        {
            _logger.LogInformation("ReadText called...");

            try {
                var form = await req.ReadFormAsync (cancellationToken);
                if (form == null) {
                    return new BadRequestObjectResult ("No form data received.");
                }

                if (!form.Files.Any () || form.Files ["file"] == null) {
                    return new BadRequestObjectResult ("No file was provided in the request.");
                }

                var file = form.Files ["file"];
                if (file == null || file.Length == 0) {
                    return new BadRequestObjectResult ("Empty file received.");
                }

                if (file.Length > MaxFileSizeBytes) {
                    return new BadRequestObjectResult ("File size is too large. Max file size is 12 MiB.");
                }

                var contentType = file.ContentType?.Split(';', 2)[0].Trim();

                if (!string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
                {
                    return new BadRequestObjectResult ("Only jpeg and png files are supported.");
                }

                byte[]? fileBytes = null;
                using var fileStream = file.OpenReadStream ();
                using var memoryStream = new MemoryStream ();
                await fileStream.CopyToAsync (memoryStream, cancellationToken);
                fileBytes = memoryStream.ToArray ();
                
                if (fileBytes == null || fileBytes.Length == 0) {
                    return new BadRequestObjectResult ("Failed to read file content.");
                }

                var result = await _imageRecognition.RecognizeAsync (fileBytes, cancellationToken);
                if (result == null) {
                    return new StatusCodeResult (StatusCodes.Status500InternalServerError);
                }

                string? sourceLanguage = null;
                try
                {
                    sourceLanguage = await _sourceLanguageDetection.DetectAsync(
                        result.RecognizedText.Select(line => line.Text),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Source-language detection failed; returning OCR results without a detected language");
                }

                result = result with { SourceLanguage = sourceLanguage };
                return new OkObjectResult (result);
            }
            catch (OperationCanceledException) {
                _logger.LogWarning ("Operation was cancelled");
                return new StatusCodeResult (StatusCodes.Status408RequestTimeout);
            }
            catch (Exception ex) {
                _logger.LogError (ex, "Error processing image file");
                return new StatusCodeResult (StatusCodes.Status500InternalServerError);
            }
        }
    }
}
