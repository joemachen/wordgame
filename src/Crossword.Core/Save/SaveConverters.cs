using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crossword.Core.Domain;

namespace Crossword.Core.Save;

/// <summary>Writes a <see cref="Letter"/> as a one-character string.</summary>
internal sealed class LetterConverter : JsonConverter<Letter>
{
    public override Letter Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (text is not { Length: 1 } || char.ToUpperInvariant(text[0]) is < 'A' or > 'Z')
            throw new JsonException($"Not a letter: '{text}'.");
        return Letter.From(text[0]);
    }

    public override void Write(Utf8JsonWriter writer, Letter value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Char.ToString());
}

/// <summary>
/// Writes a polymorphic value as its concrete record plus a tag property naming the type, e.g.
/// <c>{"id":"word-count","chipsPerTile":2,"chips":14}</c>, and reads it back into that type. Every constructor
/// parameter is stored, so scaling state and tuned parameters survive.
/// </summary>
internal sealed class TaggedConverter<TBase> : JsonConverter<TBase> where TBase : class
{
    private readonly string _tag;
    private readonly Func<TBase, string> _tagOf;
    private readonly Func<string, Type?> _typeOf;

    public TaggedConverter(string tag, Func<TBase, string> tagOf, Func<string, Type?> typeOf)
    {
        _tag = tag;
        _tagOf = tagOf;
        _typeOf = typeOf;
    }

    // Exact match only: the concrete type is serialized with the default converter (no recursion).
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(TBase);

    public override TBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(_tag, out var tag) || tag.ValueKind != JsonValueKind.String)
            throw new JsonException($"{typeof(TBase).Name} without a \"{_tag}\".");
        string name = tag.GetString()!;
        var type = _typeOf(name) ?? throw new JsonException($"Unknown {typeof(TBase).Name} \"{name}\".");
        return (TBase?)root.Deserialize(type, options) ?? throw new JsonException($"Empty {typeof(TBase).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
    {
        var element = JsonSerializer.SerializeToElement(value, value.GetType(), options);
        writer.WriteStartObject();
        writer.WriteString(_tag, _tagOf(value));
        foreach (var property in element.EnumerateObject())
            property.WriteTo(writer);
        writer.WriteEndObject();
    }
}

/// <summary>
/// Writes a string set sorted: string hashes differ between processes, so hash order would make the same session
/// serialize differently from one launch to the next.
/// </summary>
internal sealed class SortedStringSetConverter : JsonConverter<ImmutableHashSet<string>>
{
    public override ImmutableHashSet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        (JsonSerializer.Deserialize<string[]>(ref reader, options) ?? []).ToImmutableHashSet();

    public override void Write(Utf8JsonWriter writer, ImmutableHashSet<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string item in value.Order(StringComparer.Ordinal))
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
