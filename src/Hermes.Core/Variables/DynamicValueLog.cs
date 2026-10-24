namespace Hermes.Core.Variables;

/// <summary>
/// Dynamic values generated while preparing a request, in order. A replay hands the same values out again, so the
/// masked copy of a request shows what was actually sent.
/// </summary>
public sealed class DynamicValueLog
{
    private readonly Dictionary<string, List<string>> _values;
    private readonly Dictionary<string, int>? _replayed;

    public DynamicValueLog() => _values = new(StringComparer.Ordinal);

    private DynamicValueLog(Dictionary<string, List<string>> values)
    {
        _values = values;
        _replayed = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// A log that returns this one's values in the order they were generated (and fresh ones after that). It takes a
    /// copy, so values this log records later are not handed out by the replay.
    /// </summary>
    public DynamicValueLog Replay() =>
        new(_values.ToDictionary(entry => entry.Key, entry => new List<string>(entry.Value), StringComparer.Ordinal));

    internal bool TryResolve(string name, out string value)
    {
        if (_replayed is not null)
        {
            var next = _replayed.GetValueOrDefault(name);
            if (_values.TryGetValue(name, out var recorded) && next < recorded.Count)
            {
                _replayed[name] = next + 1;
                value = recorded[next];
                return true;
            }
            return DynamicVariables.TryResolve(name, out value);
        }
        if (!DynamicVariables.TryResolve(name, out value))
            return false;
        (_values.TryGetValue(name, out var list) ? list : _values[name] = []).Add(value);
        return true;
    }
}
