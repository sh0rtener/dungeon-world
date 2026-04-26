using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MicroEcs;

/// <summary>
/// A lightweight, blittable identifier for an entity inside a <see cref="World"/>.
/// Combines a 32-bit slot index with a 32-bit generation/version, so stale handles
/// to recycled slots can be detected.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Entity(int Id, int Version) : IComparable<Entity>
{
    /// <summary>An invalid sentinel entity (Id = -1, Version = 0).</summary>
    public static readonly Entity Null = new(-1, 0);

    /// <summary>True if this entity is not the <see cref="Null"/> sentinel.</summary>
    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Id >= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(Entity other) => Id.CompareTo(other.Id);

    public override string ToString() => $"Entity({Id}, v{Version})";
}
