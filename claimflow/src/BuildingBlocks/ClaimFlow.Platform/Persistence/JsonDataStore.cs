using System.Text.Json;
using ClaimFlow.Platform.Hosting;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Platform.Persistence;

public interface IDocument
{
    string Id { get; }
}

internal interface ICollectionState
{
    void WriteDocuments(Utf8JsonWriter writer);
}

public sealed class Counter : IDocument
{
    public string Id { get; set; } = "";
    public long Value { get; set; }
}

/// <summary>
/// A tiny embedded, durable document store (one JSON file per service) — the "database per service" without installing a DB.
///
/// Guarantees:
///  * Atomic multi-collection transactions: all-or-nothing, rolled back in memory if the work or the flush throws.
///  * Durability: every committed transaction is flushed with write-to-temp + atomic replace.
///  * Isolation: callers always receive private copies, so mutating a loaded document never leaks into the store.
///  * Single-process, single-writer. Serialised by one lock — fine for a demo, deliberately not a scale story.
///
/// Repositories sit on top of <see cref="DocumentCollection{T}"/>, so swapping in EF Core / PostgreSQL later is a
/// per-repository change; the transactional outbox pattern used on top stays identical.
/// </summary>
public sealed class JsonDataStore
{
    private readonly object _gate = new();
    private readonly string? _path;
    private readonly ILogger? _log;
    private readonly Dictionary<string, ICollectionState> _collections = new();
    private readonly Dictionary<string, List<string>> _raw = new();
    private readonly DocumentCollection<Counter> _counters;
    private int _depth;
    private bool _dirty;
    private List<Action>? _undo;
    private List<Action> _afterCommit = new();

    public JsonDataStore(string? path, ILogger? log = null)
    {
        _path = path;
        _log = log;
        Load();
        _counters = Collection<Counter>("_counters");
    }

    public static JsonDataStore InMemory() => new(null);

    public string? FilePath => _path;
    internal object Gate => _gate;

    public DocumentCollection<T> Collection<T>(string name) where T : class, IDocument
    {
        lock (_gate)
        {
            if (_collections.TryGetValue(name, out var existing)) return (DocumentCollection<T>)existing;
            var col = new DocumentCollection<T>(this, name);
            if (_raw.Remove(name, out var docs))
                foreach (var json in docs) col.LoadRaw(json);
            _collections[name] = col;
            return col;
        }
    }

    public long NextSequence(string name) => Transaction(() =>
    {
        var c = _counters.Get(name) ?? new Counter { Id = name };
        c.Value++;
        _counters.Upsert(c);
        return c.Value;
    });

    public void Transaction(Action work) => Transaction<object?>(() => { work(); return null; });

    public T Transaction<T>(Func<T> work)
    {
        List<Action>? post = null;
        T result;
        lock (_gate)
        {
            var outermost = _depth == 0;
            if (outermost) { _undo = new List<Action>(); _dirty = false; }
            _depth++;
            try
            {
                result = work();
                if (outermost)
                {
                    if (_dirty) Flush();
                    post = _afterCommit;
                    _afterCommit = new List<Action>();
                }
            }
            catch
            {
                if (outermost) Rollback();
                throw;
            }
            finally
            {
                _depth--;
                if (outermost) { _undo = null; _dirty = false; }
            }
        }

        if (post is not null)
            foreach (var action in post)
            {
                try { action(); } catch (Exception ex) { _log?.LogWarning(ex, "After-commit hook failed"); }
            }
        return result;
    }

    /// <summary>Runs <paramref name="action"/> once the surrounding transaction commits (immediately if there is none).</summary>
    public void AfterCommit(Action action)
    {
        lock (_gate)
        {
            if (_depth > 0) { _afterCommit.Add(action); return; }
        }
        action();
    }

    internal void Mutate(Func<Action> change)
    {
        lock (_gate)
        {
            var undo = change();
            if (_depth > 0)
            {
                _undo!.Add(undo);
                _dirty = true;
                return;
            }

            try { Flush(); }
            catch { undo(); throw; }
        }
    }

    private void Rollback()
    {
        if (_undo is null) return;
        for (var i = _undo.Count - 1; i >= 0; i--) _undo[i]();
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(_path));
            foreach (var prop in doc.RootElement.EnumerateObject())
                _raw[prop.Name] = prop.Value.EnumerateArray().Select(e => e.GetRawText()).ToList();
        }
        catch (JsonException ex)
        {
            var backup = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Move(_path, backup);
            _raw.Clear();
            _log?.LogError(ex, "Store file was unreadable; moved to {Backup} and starting empty", backup);
        }
    }

    private void Flush()
    {
        if (_path is null) return;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var w = new Utf8JsonWriter(fs))
        {
            w.WriteStartObject();
            foreach (var (name, col) in _collections.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                w.WritePropertyName(name);
                w.WriteStartArray();
                col.WriteDocuments(w);
                w.WriteEndArray();
            }
            foreach (var (name, docs) in _raw)
            {
                w.WritePropertyName(name);
                w.WriteStartArray();
                foreach (var d in docs) w.WriteRawValue(d);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>A keyed set of documents of one type, with optional secondary indexes for efficient look-ups.</summary>
public sealed class DocumentCollection<T> : ICollectionState where T : class, IDocument
{
    private sealed class Entry
    {
        public string Json = "";
        public T Value = null!;
        public string?[] Keys = [];
    }

    private sealed class IndexDef
    {
        public string Name = "";
        public Func<T, string?> Key = null!;
        public Dictionary<string, SortedSet<string>> Map = new(StringComparer.Ordinal);
    }

    private readonly JsonDataStore _store;
    private readonly SortedDictionary<string, Entry> _items = new(StringComparer.Ordinal);
    private readonly List<IndexDef> _indexes = new();

    internal DocumentCollection(JsonDataStore store, string name)
    {
        _store = store;
        Name = name;
    }

    public string Name { get; }

    /// <summary>Registers a secondary index (e.g. claims by assigned officer). Call once at start-up.</summary>
    public DocumentCollection<T> AddIndex(string name, Func<T, string?> key)
    {
        lock (_store.Gate)
        {
            if (_indexes.Any(i => i.Name == name)) return this;
            var def = new IndexDef { Name = name, Key = key };
            _indexes.Add(def);
            foreach (var (id, entry) in _items)
            {
                var k = key(entry.Value);
                if (k is not null) Add(def, k, id);
            }
            RecomputeKeys();
        }
        return this;
    }

    public T? Get(string id)
    {
        lock (_store.Gate)
            return _items.TryGetValue(id, out var e) ? Clone(e.Json) : null;
    }

    public bool Exists(string id)
    {
        lock (_store.Gate) return _items.ContainsKey(id);
    }

    /// <summary>Returns private copies of matching documents in ascending id order. The predicate must not mutate its argument.</summary>
    public IReadOnlyList<T> Query(Func<T, bool>? predicate = null)
    {
        lock (_store.Gate)
        {
            var result = new List<T>();
            foreach (var e in _items.Values)
                if (predicate is null || predicate(e.Value)) result.Add(Clone(e.Json));
            return result;
        }
    }

    public int Count(Func<T, bool>? predicate = null)
    {
        lock (_store.Gate)
            return predicate is null ? _items.Count : _items.Values.Count(e => predicate(e.Value));
    }

    /// <summary>O(matches) look-up through a secondary index.</summary>
    public IReadOnlyList<T> ByIndex(string index, string key)
    {
        lock (_store.Gate)
        {
            var def = _indexes.FirstOrDefault(i => i.Name == index)
                      ?? throw new InvalidOperationException($"Unknown index '{index}' on '{Name}'");
            if (!def.Map.TryGetValue(key, out var ids)) return Array.Empty<T>();
            return ids.Select(id => Clone(_items[id].Json)).ToList();
        }
    }

    public void Upsert(T doc)
    {
        var json = JsonSerializer.Serialize(doc, JsonDefaults.Options);
        var copy = JsonSerializer.Deserialize<T>(json, JsonDefaults.Options)!;
        _store.Mutate(() =>
        {
            _items.TryGetValue(doc.Id, out var previous);
            if (previous is not null) RemoveFromIndexes(doc.Id, previous);
            var entry = new Entry { Json = json, Value = copy };
            entry.Keys = _indexes.Select(i => i.Key(copy)).ToArray();
            _items[doc.Id] = entry;
            AddToIndexes(doc.Id, entry);
            return () =>
            {
                RemoveFromIndexes(doc.Id, entry);
                if (previous is null) _items.Remove(doc.Id);
                else { _items[doc.Id] = previous; AddToIndexes(doc.Id, previous); }
            };
        });
    }

    public bool Delete(string id)
    {
        var removed = false;
        _store.Mutate(() =>
        {
            if (!_items.TryGetValue(id, out var previous)) return () => { };
            RemoveFromIndexes(id, previous);
            _items.Remove(id);
            removed = true;
            return () => { _items[id] = previous; AddToIndexes(id, previous); };
        });
        return removed;
    }

    internal void LoadRaw(string json)
    {
        var doc = JsonSerializer.Deserialize<T>(json, JsonDefaults.Options)!;
        var entry = new Entry { Json = json, Value = doc, Keys = _indexes.Select(i => i.Key(doc)).ToArray() };
        _items[doc.Id] = entry;
        AddToIndexes(doc.Id, entry);
    }

    void ICollectionState.WriteDocuments(Utf8JsonWriter writer)
    {
        foreach (var e in _items.Values) writer.WriteRawValue(e.Json);
    }

    private static T Clone(string json) => JsonSerializer.Deserialize<T>(json, JsonDefaults.Options)!;

    private void RecomputeKeys()
    {
        foreach (var e in _items.Values) e.Keys = _indexes.Select(i => i.Key(e.Value)).ToArray();
    }

    private void AddToIndexes(string id, Entry e)
    {
        for (var i = 0; i < _indexes.Count && i < e.Keys.Length; i++)
            if (e.Keys[i] is { } k) Add(_indexes[i], k, id);
    }

    private void RemoveFromIndexes(string id, Entry e)
    {
        for (var i = 0; i < _indexes.Count && i < e.Keys.Length; i++)
        {
            if (e.Keys[i] is not { } k) continue;
            if (_indexes[i].Map.TryGetValue(k, out var set))
            {
                set.Remove(id);
                if (set.Count == 0) _indexes[i].Map.Remove(k);
            }
        }
    }

    private static void Add(IndexDef def, string key, string id)
    {
        if (!def.Map.TryGetValue(key, out var set)) def.Map[key] = set = new SortedSet<string>(StringComparer.Ordinal);
        set.Add(id);
    }
}
