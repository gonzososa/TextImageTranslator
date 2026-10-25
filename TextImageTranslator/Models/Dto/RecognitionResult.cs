namespace TextImageTranslator.Models.Dto;

public record RecognitionResult (List<RecognizedText> RecognizedText, string? SourceLanguage = null);
