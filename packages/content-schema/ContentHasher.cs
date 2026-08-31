using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Divinity.ContentSchema;

public static class ContentHasher
{
    public static string ComputeHash(IEnumerable<ContentFile> files)
    {
        using var buffer = new MemoryStream();
        foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            WriteUtf8(buffer, file.RelativePath);
            buffer.WriteByte(0);
            buffer.Write(Canonicalize(file.Json));
            buffer.WriteByte((byte)'\n');
        }

        var hash = SHA256.HashData(buffer.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var output = new MemoryStream();
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });

        WriteCanonicalElement(document.RootElement, writer);
        writer.Flush();
        return output.ToArray();
    }

    private static void WriteCanonicalElement(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(item, writer);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void WriteUtf8(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes);
    }
}
