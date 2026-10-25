using Azure;
using Azure.AI.Translation.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TextImageTranslator.Models.Dto;

namespace TextImageTranslator.Platform;

public interface ITexTranslation
{
   Task<TranslationResult> TranslateAsync (IEnumerable<string> textChunks, string targetLanguage, CancellationToken cancellationToken);
}

public class TextTranslation : ITexTranslation
{
    private readonly ILogger<TextTranslation> _logger;
    private readonly IConfiguration _configuration;

    public TextTranslation (ILogger<TextTranslation> logger, IConfiguration configuration)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TranslationResult> TranslateAsync (IEnumerable<string> textChunks, string targetLanguage, CancellationToken cancellationToken) 
    {
        _logger.LogInformation ($"Translating {textChunks.Count ()} text chunks to {targetLanguage}");

        var translatorClient = new TextTranslationClient (
            new AzureKeyCredential (_configuration["TranslatorKey"]),
            new Uri (_configuration["TranslatorEndpoint"])
        );
        _logger.LogInformation ("Translator client created...");

        _logger.LogInformation ("Calling TranslateAsync...");
        var response = await translatorClient.TranslateAsync (
            targetLanguage, 
            textChunks,
            cancellationToken: cancellationToken
        );
        _logger.LogInformation ("TranslateAsync called...");

        List<Translation> translations = [];
        if (response == null || response.Value == null)
        {
            return new TranslationResult (translations);
        }

        _logger.LogInformation ("Processing translation response..." + response.Value.Count);
        for (int i = 0; i < response.Value.Count; i++)
        {
            TranslatedTextItem translatedItem = response.Value [i];
            string detectedLanguage = translatedItem.DetectedLanguage.Language;
            
            foreach (var translation in translatedItem.Translations)
            {
                translations.Add (
                    new Translation (
                        textChunks.ElementAt (i), 
                        translation.Text, 
                        detectedLanguage
                    )
                );
            }
        }
 
        return new TranslationResult (translations);
    }
}

