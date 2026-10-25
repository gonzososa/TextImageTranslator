namespace TextImageTranslator.Models.Dto;

public record RecognizedText (IEnumerable<Polygon> Polygon, string Text);
