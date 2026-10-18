using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hak.Domain.Common;

namespace Hak.Application.Exchange;

/// <summary>
/// Canonical JSON per RFC 8785 (JCS) restricted to the subset HAK contracts allow:
/// object keys sorted by UTF-16 code units, no insignificant whitespace, JCS string escaping,
/// and integer numbers only. Money and rates travel as decimal strings (docs/04 §2), so a
/// non-integer JSON number is rejected rather than risking a non-JCS float serialisation.
/// </summary>
public static class JsonCanonicalizer
{
    public static string Canonicalize(JsonNode? node)
    {
        var sb = new StringBuilder();
        Write(node, sb);
        return sb.ToString();
    }

    public static string Canonicalize(string json) => Canonicalize(JsonNode.Parse(json));

    private static void Write(JsonNode? node, StringBuilder sb)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                sb.Append('{');
                var first = true;
                foreach (var kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(kv.Key, sb);
                    sb.Append(':');
                    Write(kv.Value, sb);
                }
                sb.Append('}');
                break;
            case JsonArray arr:
                sb.Append('[');
                for (var i = 0; i < arr.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(arr[i], sb);
                }
                sb.Append(']');
                break;
            case JsonValue v:
                var el = v.TryGetValue<JsonElement>(out var raw) ? raw : JsonSerializer.SerializeToElement(v);
                switch (el.ValueKind)
                {
                    case JsonValueKind.String: WriteString(el.GetString()!, sb); break;
                    case JsonValueKind.True: sb.Append("true"); break;
                    case JsonValueKind.False: sb.Append("false"); break;
                    case JsonValueKind.Null: sb.Append("null"); break;
                    case JsonValueKind.Number:
                        if (!el.TryGetInt64(out var l) || Math.Abs(l) > 9007199254740991L)
                            throw new DomainException(ErrorCodes.PackageSchemaInvalid,
                                $"Số JSON '{el.GetRawText()}' không được phép: số tiền/tỷ giá phải truyền dạng chuỗi thập phân, số nguyên phải trong phạm vi an toàn của JavaScript.");
                        sb.Append(l.ToString(CultureInfo.InvariantCulture));
                        break;
                    default: throw new InvalidOperationException($"Unexpected JSON kind {el.ValueKind}");
                }
                break;
        }
    }

    private static void WriteString(string s, StringBuilder sb)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
