using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MicroEcs;

namespace MicroEcs.Benchmarks;

public record struct Position(float X, float Y);
public record struct Velocity(float X, float Y);

[MemoryDiagnoser]
[ShortRunJob]
public class IterationBenchmarks
{
    [Params(1_000, 100_000, 1_000_000)]
    public int EntityCount;

    private World _world = null!;
    private QueryDescription _movement = null!;
    private const float Dt = 0.016f;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        for (int i = 0; i < EntityCount; i++)
            _world.Create(new Position(i, i), new Velocity(1, 1));
        _movement = new QueryDescription().WithAll<Position, Velocity>();
    }

    /// <summary>The headline number: how fast can we sweep N entities and update Position from Velocity?</summary>
    [Benchmark]
    public void Movement_ForEach()
    {
        _world.Query(_movement).ForEach<Position, Velocity>((ref Position p, ref Velocity v) =>
        {
            p.X += v.X * Dt;
            p.Y += v.Y * Dt;
        });
    }

    /// <summary>Same workload but with a hand-written chunk loop — usually a bit faster because the JIT has fewer indirection layers to reason about.</summary>
    [Benchmark]
    public void Movement_ChunkLoop()
    {
        _world.Query(_movement).ForEachChunk(chunk =>
        {
            var pos = chunk.GetSpan<Position>();
            var vel = chunk.GetSpan<Velocity>();
            int n = pos.Length;
            for (int i = 0; i < n; i++)
            {
                pos[i].X += vel[i].X * Dt;
                pos[i].Y += vel[i].Y * Dt;
            }
        });
    }
}

[MemoryDiagnoser]
[ShortRunJob]
public class CreationBenchmarks
{
    [Params(10_000, 100_000)]
    public int EntityCount;

    [Benchmark]
    public World CreateEntitiesWithTwoComponents()
    {
        var world = new World();
        for (int i = 0; i < EntityCount; i++)
            world.Create(new Position(i, i), new Velocity(1, 1));
        return world;
    }
}

internal static class Program
{
    private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
