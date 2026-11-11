using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace NorthspanPolicy;

/// <summary>Reads plain text out of an uploaded policy. Works in memory only; nothing is written to disk.</summary>
public static class TextExtractor
{
    public const long MaxUploadBytes = 2 * 1024 * 1024; // 2 MB
    public const int MaxChars = 60_000;                 // roughly 30 pages; keeps AI cost bounded

    public static async Task<string> ExtractAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        string text = ext switch
        {
            ".docx" => ExtractDocx(stream),
            ".txt" or ".md" => await ExtractTextAsync(stream, ct),
            _ => throw new UnsupportedFileException("Upload a .docx or .txt file, or paste the text instead."),
        };
        return Clean(text);
    }

    internal static string ExtractDocx(Stream stream)
    {
        // OpenXml needs a seekable stream.
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        try
        {
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) return string.Empty;
            var sb = new StringBuilder();
            foreach (var p in body.Descendants<Paragraph>())
            {
                var line = p.InnerText;
                if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine(line);
            }
            return sb.ToString();
        }
        catch (Exception)
        {
            throw new UnsupportedFileException("That Word file could not be read. Save it again as .docx, or paste the text instead.");
        }
    }

    private static async Task<string> ExtractTextAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    internal static string Clean(string text)
    {
        text = text.Replace("\r\n", "\n").Trim();
        return text.Length > MaxChars ? text[..MaxChars] : text;
    }
}

public sealed class UnsupportedFileException(string message) : Exception(message);
