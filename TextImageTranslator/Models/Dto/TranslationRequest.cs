namespace TextImageTranslator.Models.Dto;

public record TranslationRequest (IEnumerable<string> TextChunks, string TargetLanguage);

