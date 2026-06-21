using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ManusReader.Services;

public static class WordReader
{
    public static string ReadText(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "";

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<Paragraph>())
        {
            var text = para.InnerText.Trim();
            if (!string.IsNullOrEmpty(text))
                sb.AppendLine(text);
        }
        return sb.ToString();
    }
}
