namespace HomeDutiesAssistant.Configuration;

// Bound from the "Onnx" section. When the section is absent, BillExtractor is not
// registered and PDF import falls back to dumping the raw text into Notes.
// ModelPath points at a local Phi-4-mini-instruct ONNX model directory.
public sealed class OnnxOptions
{
    public const string SectionName = "Onnx";

    public string ModelPath { get; set; } = "";
    public int MaxLength { get; set; } = 4096;
}
