using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeDutiesAssistant.Services.Converters;

// Accepts "YYYY-MM-DD"; anything else (or a non-string) becomes null.
internal sealed class DueDateJsonConverter : JsonConverter<DateOnly?>
{
    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }
        return DateOnly.TryParseExact(reader.GetString(), "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date : null;
    }

    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }
}