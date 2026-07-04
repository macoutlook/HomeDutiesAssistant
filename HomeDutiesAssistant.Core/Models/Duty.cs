using System.Text;
using YamlDotNet.Serialization;

namespace HomeDutiesAssistant.Models;

// One home-duty fact. Created in the management UI or seeded from the YAML
// files in /data. All fields except Category/Title are optional so each record
// can be sparse.
public sealed class Duty
{
    // Database-generated surrogate key (bigint). 0 means "not yet saved" (a new
    // duty). Ignored when reading YAML — seed records get their id on insert.
    [YamlIgnore]
    public long Id { get; set; }
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Provider { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? DueDate { get; set; }
    public string? Frequency { get; set; }
    public string? Notes { get; set; }

    // A single human-readable sentence describing this record.
    // This exact text is what we (a) turn into an embedding and (b) hand to the
    // chat model as a "fact". Keeping both based on the same text means the
    // retrieval and the answer always talk about the same thing.
    public string ToContext()
    {
        var sb = new StringBuilder();
        sb.Append($"[{Category}] {Title}.");
        if (Provider is not null) sb.Append($" Provider: {Provider}.");
        if (Amount is not null) sb.Append($" Amount: {Amount} {Currency}.");
        if (DueDate is not null) sb.Append($" Due date: {DueDate}.");
        if (Frequency is not null) sb.Append($" Frequency: {Frequency}.");
        if (!string.IsNullOrWhiteSpace(Notes)) sb.Append($" Notes: {Notes}");
        return sb.ToString();
    }
}