using CloudScout.Core.Taxonomy;

namespace CloudScout.Core.Inference;

/// <summary>
/// Constructs classification prompts for the Gemma model. The prompt is designed for
/// deterministic, structured output: a single-line JSON object containing the category id,
/// confidence, and reason. The taxonomy's leaf categories are injected as the valid enum so
/// the model can only output known category ids.
///
/// Prompt engineering notes:
///   - Short system preamble keeps the instruction budget tight for the small E2B model.
///   - Explicit JSON format example reduces hallucination of free-form text.
///   - Temperature 0 in LlmOptions produces greedy decoding for consistency.
///   - Anti-prompts ("\n\n", "```") in LlamaSharpInference stop generation after the JSON line.
/// </summary>
public static class GemmaPromptBuilder
{
    /// <summary>Maximum characters of extracted text to include in the prompt. Longer text wastes
    /// context window on content that rarely adds classification signal past the first pages.</summary>
    private const int MaxTextChars = 2000;

    public static string BuildClassificationPrompt(
        string fileName,
        string folderPath,
        string? mimeType,
        string? extractedText,
        TaxonomyDefinition taxonomy)
    {
        var categories = string.Join("\n", taxonomy.Categories
            .Where(c => c.ParentId is not null) // only leaf categories
            .Select(c => $"  - {c.Id}: {c.DisplayName}"));

        var textSection = string.IsNullOrWhiteSpace(extractedText)
            ? "(no text could be extracted from this file)"
            : Truncate(extractedText, MaxTextChars);

        return $$"""
            You are a document classifier. Given file metadata and extracted text, classify the file into exactly one category from the list below. If no category fits, respond with "unclassified".

            Categories:
            {{categories}}

            File:
            - Name: {{fileName}}
            - Folder: {{folderPath}}
            - Type: {{mimeType ?? "unknown"}}

            Extracted text:
            {{textSection}}

            Respond with ONLY a JSON object on a single line, no markdown:
            {"categoryId": "<id from list above or unclassified>", "confidence": 0.0-1.0, "reason": "<brief explanation>"}
            """;
    }

    private static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + "...";
}
