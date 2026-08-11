using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace HomeDutiesAssistant.Infrastructure;

public sealed class PdfTextExtractor
{
    public async Task<string> ExtractAsync(Stream pdf, CancellationToken ct = default)
    {
        await using (pdf)
        {
            using var buffer = new MemoryStream();
            await pdf.CopyToAsync(buffer, ct);
            using var document = PdfDocument.Open(buffer.ToArray());
            var builder = new StringBuilder();
            foreach (var page in document.GetPages())
                builder.AppendLine(ContentOrderTextExtractor.GetText(page));
            return builder.ToString();
        }
    }
}