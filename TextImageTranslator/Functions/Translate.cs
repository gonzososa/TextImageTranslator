using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TextImageTranslator.Platform;
using System.Text.Json;
using TextImageTranslator.Models.Dto;

namespace TextImageTranslator.Functions
{
    public class Translate
    {
        private readonly ILogger<Translate> _logger;
        private readonly ITexTranslation _textTranslation;
        private static readonly JsonSerializerOptions DefaultSerializerOptions = new ()
        {
            PropertyNameCaseInsensitive = true
        };

        public Translate(ILogger<Translate> logger, ITexTranslation textTranslation)
        {
            _logger = logger;
            _textTranslation = textTranslation;
        }

        [Function("Translate")]
        public async Task<IActionResult> Run (
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
            CancellationToken cancellationToken
        )
        {
            _logger.LogInformation("Translate text called...");
            
            try 
            {
                if (req.Body == null || req.ContentLength == 0)
                {
                    return new BadRequestObjectResult("Request body is empty");
                }

                string json;
                using (var reader = new StreamReader(req.Body))
                {
                    json = await reader.ReadToEndAsync(cancellationToken);
                }

                if (string.IsNullOrEmpty(json))
                {
                    return new BadRequestObjectResult("Request body is empty");
                }

                _logger.LogInformation($"Input data: {json}");

                TranslationRequest? request;
                try 
                {
                    request = JsonSerializer.Deserialize<TranslationRequest>(
                        json, 
                        DefaultSerializerOptions
                    );
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize request");
                    return new BadRequestObjectResult("Invalid JSON format");
                }

                if (request == null)
                {
                    return new BadRequestObjectResult("Invalid request format");
                }

                if (!request.TextChunks?.Any () ?? true)
                {
                    return new BadRequestObjectResult("Texts to translate are missing");
                }

                if (string.IsNullOrEmpty(request.TargetLanguage))
                {
                    return new BadRequestObjectResult("TargetLanguage field is required");
                }

                if (request.TargetLanguage.Length != 2)
                {
                    return new BadRequestObjectResult("Invalid language code format. Use ISO 639-1 format (e.g., 'en', 'es')");
                }

                var translatedText = await _textTranslation.TranslateAsync (
                    request.TextChunks, 
                    request.TargetLanguage, 
                    cancellationToken
                );

                if (translatedText == null)
                {
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }

                return new OkObjectResult(translatedText);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Request was cancelled");
                return new StatusCodeResult(StatusCodes.Status408RequestTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing translation request");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}

