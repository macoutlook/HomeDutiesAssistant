using HomeDutiesAssistant.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HomeDutiesAssistant.Services;

// Reads every *.yaml file under a directory into a flat list of DutyRecord.
public sealed class DataLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance) // dueDate -> DueDate
        .IgnoreUnmatchedProperties()                              // ignore extra YAML keys
        .Build();

    public IReadOnlyList<Duty> LoadFromDirectory(string directory)
    {
        var records = new List<Duty>();
        if (!Directory.Exists(directory))
            return records;

        foreach (var file in Directory.EnumerateFiles(directory, "*.yaml", SearchOption.AllDirectories))
        {
            var yaml = File.ReadAllText(file);
            var items = _deserializer.Deserialize<List<Duty>>(yaml);
            if (items.Any())
                records.AddRange(items);
        }
        return records;
    }
}