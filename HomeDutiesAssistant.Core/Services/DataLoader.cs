using System.Globalization;
using HomeDutiesAssistant.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;
using YamlDotNet.Serialization.NamingConventions;

namespace HomeDutiesAssistant.Services;

// Reads every *.yaml file under a directory into a flat list of DutyRecord.
public sealed class DataLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new DateOnlyConverter(CultureInfo.InvariantCulture, false, "yyyy-MM-dd"))
        .IgnoreUnmatchedProperties()
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