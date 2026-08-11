using System.Text;
using System.Text.Json;
using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Models;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntimeGenAI;
using Task = System.Threading.Tasks.Task;

namespace HomeDutiesAssistant.Infrastructure;

// Runs (ONNX model) locally to turn extracted bill text into a Duty.
// The model is multi-GB, so it is loaded once (singleton);
// generation is serialized because a Generator is not thread-safe.
public sealed class BillExtractor : IDisposable
{
    private const string SystemPrompt = """
                                        You extract a household bill into JSON. Return ONLY a JSON object with the keys:
                                        category, title, provider, amount, currency, dueDate, frequency, notes.
                                        amount is the TOTAL amount due for the whole bill (the grand total / "do zapłaty" /
                                        "razem" / "suma"), NOT a single line item, sub-charge, or per-unit price.
                                        category and title describe the ENTIRE bill (its type and billing period), NOT one
                                        line item from it.
                                        currency is the 3-letter ISO 4217 code — normalize any symbol or local spelling to it
                                        (e.g. "zł"/"Zł" -> "PLN", "€" -> "EUR", "$" -> "USD").
                                        dueDate is YYYY-MM-DD; use null when a value is unknown.
                                        """;

    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly GeneratorParams _generatorParams;

    private readonly JsonSerializerOptions _serializerOptions = new()
        { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public BillExtractor(IOptions<OnnxOptions> options)
    {
        _model = new Model(ResolveModelPath(options.Value.ModelPath));
        _tokenizer = new Tokenizer(_model);
        _generatorParams = new GeneratorParams(_model);
        _generatorParams.SetSearchOption("max_length", options.Value.MaxLength);
        _generatorParams.SetSearchOption("temperature", 0.0);
    }

    public async Task<Duty> ExtractAsync(string billText, CancellationToken ct = default)
    {
        var output = await GenerateAsync(billText, ct);
        return ParseFields(output);
    }

    private async Task<string> GenerateAsync(string billText, CancellationToken ct)
    {
        var prompt = $"<|system|>\n{SystemPrompt}<|end|>\n<|user|>\n{billText}<|end|>\n<|assistant|>\n";

        return await Task.Run(() =>
        {
            using var sequences = _tokenizer.Encode(prompt);

            using var generator = new Generator(_model, _generatorParams);
            generator.AppendTokenSequences(sequences);

            using var stream = _tokenizer.CreateStream();
            var builder = new StringBuilder();
            while (!generator.IsDone())
            {
                ct.ThrowIfCancellationRequested();
                generator.GenerateNextToken();
                builder.Append(stream.Decode(generator.GetSequence(0)[^1]));
            }

            return builder.ToString();
        }, ct);
    }

    private static string ResolveModelPath(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Onnx:ModelPath is not configured.");
        if (Path.IsPathRooted(configured))
            return configured;

        var root = FindSolutionRoot() ?? AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(root, configured));
    }

    // Walk up from the assembly location (stable regardless of CWD) to the
    // nearest directory containing a .sln file.
    private static string? FindSolutionRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (dir.EnumerateFiles("*.sln").Any())
                return dir.FullName;
        return null;
    }

    private Duty ParseFields(string output)
    {
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("The model did not return JSON.");

        var json = output[start..end];
        return JsonSerializer.Deserialize<Duty>(json, _serializerOptions )
               ?? new Duty();
    }

    public void Dispose()
    {
        _tokenizer.Dispose();
        _model.Dispose();
        _generatorParams.Dispose();
    }
}