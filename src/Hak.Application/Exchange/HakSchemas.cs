using System.Collections.Concurrent;
using System.Reflection;
using Json.Schema;

namespace Hak.Application.Exchange;

/// <summary>Loads the JSON Schemas embedded from /schemas. Each build uses a private registry so $id values never clash.</summary>
public static class HakSchemas
{
    private static readonly ConcurrentDictionary<string, JsonSchema> Cache = new();

    public static JsonSchema Get(string fileName) => Cache.GetOrAdd(fileName, static name =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Hak.Schemas.{name}")
                      ?? throw new InvalidOperationException($"Embedded schema {name} missing.");
        using var r = new StreamReader(s);
        return FromText(r.ReadToEnd());
    });

    public static JsonSchema FromText(string text)
        => JsonSchema.FromText(text, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
}
