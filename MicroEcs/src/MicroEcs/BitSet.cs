using System.Numerics;
using System.Runtime.CompilerServices;

namespace MicroEcs;

/// <summary>
/// A compact, growable bit set used to identify which component types a given archetype contains.
/// Backed by an array of <see cref="ulong"/> "words"; supports fast equality, subset, and overlap checks
/// via bitwise operations and <see cref="BitOperations"/>.
/// </summary>
public sealed class BitSet : IEquatable<BitSet>
{
    private ulong[] _bits;

    public BitSet() { _bits = []; }

    public BitSet(int initialBitCapacity)
    {
        int wordCount = (initialBitCapacity + 63) >>> 6;
        _bits = wordCount == 0 ? [] : new ulong[wordCount];
    }

    /// <summary>Number of 64-bit words currently allocated.</summary>
    public int WordCount => _bits.Length;

    /// <summary>Direct read-only view of the underlying words. Useful for hashing.</summary>
    public ReadOnlySpan<ulong> AsSpan() => _bits;

    public void Set(int bit)
    {
        int word = bit >>> 6;
        EnsureWord(word);
        _bits[word] |= 1UL << (bit & 63);
    }

    public void Clear(int bit)
    {
        int word = bit >>> 6;
        if (word >= _bits.Length) return;
        _bits[word] &= ~(1UL << (bit & 63));
    }

    public bool IsSet(int bit)
    {
        int word = bit >>> 6;
        if (word >= _bits.Length) return false;
        return (_bits[word] & (1UL << (bit & 63))) != 0;
    }

    public void ClearAll() => Array.Clear(_bits);

    /// <summary>Total population count (number of set bits).</summary>
    public int PopCount()
    {
        int total = 0;
        foreach (var w in _bits) total += BitOperations.PopCount(w);
        return total;
    }

    /// <summary>True if every bit set in <paramref name="other"/> is also set here.</summary>
    public bool ContainsAll(BitSet other)
    {
        var a = _bits;
        var b = other._bits;
        int common = Math.Min(a.Length, b.Length);
        for (int i = 0; i < common; i++)
            if ((a[i] & b[i]) != b[i]) return false;
        // any extra words in `other` must be zero
        for (int i = common; i < b.Length; i++)
            if (b[i] != 0) return false;
        return true;
    }

    /// <summary>True if this set and <paramref name="other"/> share at least one set bit.</summary>
    public bool OverlapsAny(BitSet other)
    {
        var a = _bits;
        var b = other._bits;
        int common = Math.Min(a.Length, b.Length);
        for (int i = 0; i < common; i++)
            if ((a[i] & b[i]) != 0) return true;
        return false;
    }

    /// <summary>True if this set and <paramref name="other"/> share no set bits.</summary>
    public bool DisjointWith(BitSet other) => !OverlapsAny(other);

    public BitSet Clone()
    {
        var c = new BitSet { _bits = (ulong[])_bits.Clone() };
        return c;
    }

    public bool Equals(BitSet? other)
    {
        if (other is null) return false;
        var a = _bits;
        var b = other._bits;
        int common = Math.Min(a.Length, b.Length);
        for (int i = 0; i < common; i++)
            if (a[i] != b[i]) return false;
        for (int i = common; i < a.Length; i++) if (a[i] != 0) return false;
        for (int i = common; i < b.Length; i++) if (b[i] != 0) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is BitSet bs && Equals(bs);

    public override int GetHashCode()
    {
        // FNV-1a over the meaningful (non-trailing-zero) words.
        int last = _bits.Length - 1;
        while (last >= 0 && _bits[last] == 0) last--;
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i <= last; i++)
            {
                hash ^= _bits[i];
                hash *= 1099511628211UL;
            }
            return (int)(hash ^ (hash >> 32));
        }
    }

    /// <summary>Iterate the indices of all set bits in ascending order.</summary>
    public IEnumerable<int> EnumerateSetBits()
    {
        for (int w = 0; w < _bits.Length; w++)
        {
            ulong word = _bits[w];
            while (word != 0)
            {
                int bit = BitOperations.TrailingZeroCount(word);
                yield return (w << 6) + bit;
                word &= word - 1;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureWord(int wordIndex)
    {
        if (wordIndex < _bits.Length) return;
        int newSize = Math.Max(_bits.Length * 2, wordIndex + 1);
        if (newSize < 4) newSize = 4;
        Array.Resize(ref _bits, newSize);
    }
}
