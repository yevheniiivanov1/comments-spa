namespace Comments.Application;

/// <summary>Field errors keyed by the form field name the client uses (camelCase).</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsEmpty => _errors.Count == 0;

    public void Add(string field, string? error)
    {
        if (error is null)
        {
            return;
        }

        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(error);
    }

    public void Add(string field, IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            Add(field, error);
        }
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);
}
