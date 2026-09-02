using System;
using System.Globalization;
using FileHelpers;
using Newtonsoft.Json;


namespace Dan.Plugin.Bits.Models;

// Deserializes an apiResponseStatus string against the ApiResponseStatus enum.
// Unrecognized value falls back to ApiResponseStatus.Unknown
public sealed class ApiResponseStatusConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(ApiResponseStatus) || objectType == typeof(ApiResponseStatus?);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var text = reader.Value?.ToString();

        if (string.IsNullOrWhiteSpace(text))
            return null;

        return Enum.TryParse<ApiResponseStatus>(text, ignoreCase: true, out var status) ? status : ApiResponseStatus.Unknown;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteValue(value.ToString());
        }
    }
}

public sealed class DateTimeOffsetConverter : ConverterBase
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd'T'HH:mm:ss.fffffffK", // covers Z or +hh:mm with up to 7 frac secs
        "yyyy-MM-dd'T'HH:mm:ss.fffK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd HH:mm:ss.fffffffK",
        "yyyy-MM-dd HH:mm:ss.fffK",
        "yyyy-MM-dd HH:mm:ssK"
    };

    public override object StringToField(string from)
    {
        if (string.IsNullOrWhiteSpace(from))
            return default(DateTimeOffset); // or throw; depends on your needs

        // Some systems emit malformed "Z+02:00" — normalize if you need to
        if (from.Contains("Z+") || from.Contains("Z-"))
            from = from.Replace("Z+", "+").Replace("Z-", "-");

        if (DateTimeOffset.TryParseExact(
                from,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
            return dto;

        throw new ConvertException(from, typeof(DateTimeOffset), "Invalid DateTimeOffset format.");
    }

    public override string FieldToString(object fieldValue)
    {
        var dto = (DateTimeOffset)fieldValue;
        return dto.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture);
    }
}

// Nullable variant
public sealed class NullableDateTimeOffsetConverter : ConverterBase
{
    private readonly DateTimeOffsetConverter _inner = new();

    public override object StringToField(string from)
        => string.IsNullOrWhiteSpace(from) ? (DateTimeOffset?)null : (DateTimeOffset)_inner.StringToField(from);

    public override string FieldToString(object fieldValue)
        => fieldValue is DateTimeOffset dto
            ? dto.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture)
            : string.Empty; // how you want nulls emitted
}
