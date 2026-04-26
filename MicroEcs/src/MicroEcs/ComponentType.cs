using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MicroEcs;

/// <summary>
/// Runtime metadata for a registered component type: a stable integer id, the size in bytes,
/// and whether it's a zero-sized tag.
/// </summary>
public sealed class ComponentType
{
    /// <summary>A monotonically-increasing id, unique per <see cref="Type"/> within the process.</summary>
    public int Id { get; }

    /// <summary>The CLR type of the component.</summary>
    public Type Type { get; }

    /// <summary>Size in bytes of the component struct. 0 for tags (no instance fields).</summary>
    public int Size { get; }

    /// <summary>True when <see cref="Size"/> is 0 — i.e., the struct has no instance fields.</summary>
    public bool IsTag => Size == 0;

    internal ComponentType(int id, Type type, int size)
    {
        Id = id;
        Type = type;
        Size = size;
    }

    public override string ToString() => $"{Type.Name}#{Id}";
}

/// <summary>
/// Static registry that hands out a unique <see cref="ComponentType"/> per CLR type.
/// Uses the generic-class-as-cache trick for zero-cost lookup on the hot path.
/// </summary>
public static class ComponentRegistry
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<Type, ComponentType> _byType = new();
    private static readonly List<ComponentType> _byId = new();
    private static readonly object _registerLock = new();

    /// <summary>Total number of registered component types.</summary>
    public static int Count
    {
        get
        {
            lock (_registerLock) { return _byId.Count; }
        }
    }

    /// <summary>Get or register a <see cref="ComponentType"/> for <typeparamref name="T"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ComponentType Of<T>() where T : struct => TypeCache<T>.Type;

    /// <summary>Get or register a <see cref="ComponentType"/> for the given runtime type.</summary>
    public static ComponentType Of(Type t)
    {
        if (_byType.TryGetValue(t, out var existing)) return existing;

        if (!t.IsValueType)
            throw new ArgumentException($"Component type '{t}' must be a struct.", nameof(t));

        return _byType.GetOrAdd(t, type =>
        {
            // A struct with no instance fields is treated as a tag (zero storage).
            // We can't use Marshal.SizeOf on an empty struct (it returns 1 for layout reasons),
            // so we count instance fields directly.
            bool isEmpty = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length == 0;
            int size = isEmpty ? 0 : SizeOfHelper.SizeOf(type);

            ComponentType ct;
            lock (_registerLock)
            {
                int id = _nextId++;
                ct = new ComponentType(id, type, size);
                _byId.Add(ct);
            }
            return ct;
        });
    }

    /// <summary>Get a previously-registered component type by its integer id.</summary>
    public static ComponentType ById(int id)
    {
        lock (_registerLock)
        {
            return _byId[id];
        }
    }

    private static class TypeCache<T> where T : struct
    {
        public static readonly ComponentType Type = Of(typeof(T));
    }

    /// <summary>
    /// Reflective wrapper around <see cref="Unsafe.SizeOf{T}"/>. We use Unsafe.SizeOf rather than
    /// <c>Marshal.SizeOf</c> because the latter computes unmanaged-marshalling size (which can
    /// differ from the managed in-memory size) and refuses some valid struct shapes.
    /// </summary>
    private static class SizeOfHelper
    {
        private static readonly MethodInfo _unsafeSizeOf =
            typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf), BindingFlags.Public | BindingFlags.Static)!;

        public static int SizeOf(Type t)
        {
            var generic = _unsafeSizeOf.MakeGenericMethod(t);
            return (int)generic.Invoke(null, null)!;
        }
    }
}
