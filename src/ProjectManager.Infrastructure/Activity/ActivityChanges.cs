using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectManager.Infrastructure.Activity;

/// <summary>
/// Alterações campo a campo de um evento, pelos nomes do contrato da API (ADR-014). Textos longos registram só
/// que mudaram, sem o conteúdo; listas registram o que entrou e o que saiu.
/// </summary>
internal sealed class ActivityChanges
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SortedDictionary<string, Dictionary<string, object?>> _fields = new(StringComparer.Ordinal);

    public bool IsEmpty => _fields.Count == 0;

    public ActivityChanges Set(string field, object? from, object? to)
    {
        if (!Equals(from, to))
        {
            _fields[field] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["from"] = from, ["to"] = to };
        }

        return this;
    }

    /// <summary>O campo mudou, mas o conteúdo não vai para a auditoria (descrições, corpos de texto).</summary>
    public ActivityChanges ContentChanged(string field)
    {
        _fields[field] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["changed"] = true };
        return this;
    }

    public ActivityChanges List(string field, IEnumerable<string> before, IEnumerable<string> after)
    {
        var old = before.ToHashSet(StringComparer.Ordinal);
        var current = after.ToHashSet(StringComparer.Ordinal);
        var added = current.Except(old).Order(StringComparer.Ordinal).ToList();
        var removed = old.Except(current).Order(StringComparer.Ordinal).ToList();
        if (added.Count > 0 || removed.Count > 0)
        {
            _fields[field] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["added"] = added, ["removed"] = removed };
        }

        return this;
    }

    public string? ToJson() => IsEmpty ? null : JsonSerializer.Serialize(_fields, SerializerOptions);
}
