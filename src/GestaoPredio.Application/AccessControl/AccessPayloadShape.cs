using System.Text;
using System.Text.Json;

namespace GestaoPredio.Application.AccessControl;

/// <summary>
/// Describes the structure of a payload without keeping any of it.
///
/// The point is to learn the wire format of a device whose integration manual we do not have, while the
/// payload may carry an access photo, a face template, a card number or a QR code. Key names and value
/// kinds are what a normalizer needs; the values are what must not be stored. So every scalar becomes its
/// kind and length — <c>"UserID": "string(8)"</c> — and nothing that identifies a person survives.
/// </summary>
public static class AccessPayloadShape
{
    public const int MaxLength = 4000;
    private const int MaxDepth = 6;
    private const int MaxArraySample = 1;

    public static string Describe(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "empty";
        try
        {
            using var document = JsonDocument.Parse(body);
            var builder = new StringBuilder();
            Write(document.RootElement, builder, 0);
            var shape = builder.ToString();
            return shape.Length > MaxLength ? shape[..MaxLength] : shape;
        }
        catch (JsonException)
        {
            // Not JSON. Record only that, plus how big it was — form-encoded and XML are both plausible
            // and the raw bytes are exactly what must not be persisted.
            return $"non-json(length={body.Length})";
        }
    }

    private static void Write(JsonElement element, StringBuilder builder, int depth)
    {
        if (depth > MaxDepth) { builder.Append("..."); return; }
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject())
                {
                    if (!first) builder.Append(',');
                    first = false;
                    builder.Append('"').Append(property.Name).Append("\":");
                    Write(property.Value, builder, depth + 1);
                }
                builder.Append('}');
                break;

            case JsonValueKind.Array:
                var count = element.GetArrayLength();
                builder.Append("[count=").Append(count);
                if (count > 0)
                {
                    builder.Append(',');
                    foreach (var item in element.EnumerateArray().Take(MaxArraySample))
                        Write(item, builder, depth + 1);
                }
                builder.Append(']');
                break;

            case JsonValueKind.String:
                builder.Append("\"string(").Append(element.GetString()?.Length ?? 0).Append(")\"");
                break;

            case JsonValueKind.Number:
                builder.Append("number(").Append(element.GetRawText().Length).Append(')');
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                builder.Append("bool");
                break;

            default:
                builder.Append("null");
                break;
        }
    }
}
