using Xunit;

namespace MicroEcs.Tests;

// Test components
public record struct Pos(float X, float Y);
public record struct Vel(float X, float Y);
public record struct Hp(int Value);
public struct Frozen : ITag { }

public class WorldTests
{
    [Fact]
    public void Create_and_destroy_changes_entity_count()
    {
        using var world = new World();
        var e = world.Create();
        Assert.Equal(1, world.EntityCount);
        Assert.True(world.IsAlive(e));

        world.Destroy(e);
        Assert.Equal(0, world.EntityCount);
        Assert.False(world.IsAlive(e));
    }

    [Fact]
    public void Destroyed_entity_handle_does_not_survive_recycle()
    {
        using var world = new World();
        var first = world.Create();
        world.Destroy(first);

        var second = world.Create();
        // The slot was recycled, but the version was bumped, so the old handle is now stale.
        Assert.NotEqual(first.Version, second.Version);
        Assert.False(world.IsAlive(first));
        Assert.True(world.IsAlive(second));
    }

    [Fact]
    public void Add_then_get_returns_the_value_set()
    {
        using var world = new World();
        var e = world.Create();
        world.Add(e, new Pos(1, 2));
        var p = world.GetRef<Pos>(e);
        Assert.Equal(new Pos(1, 2), p);
    }

    [Fact]
    public void GetRef_allows_mutation_in_place()
    {
        using var world = new World();
        var e = world.Create(new Pos(0, 0));
        world.GetRef<Pos>(e).X = 42f;
        Assert.Equal(42f, world.GetRef<Pos>(e).X);
    }

    [Fact]
    public void Adding_existing_component_throws()
    {
        using var world = new World();
        var e = world.Create(new Pos(0, 0));
        Assert.Throws<InvalidOperationException>(() => world.Add(e, new Pos(1, 1)));
    }

    [Fact]
    public void Set_creates_or_overwrites()
    {
        using var world = new World();
        var e = world.Create();
        world.Set(e, new Pos(1, 1));         // create
        world.Set(e, new Pos(2, 2));         // overwrite
        Assert.Equal(new Pos(2, 2), world.GetRef<Pos>(e));
    }

    [Fact]
    public void Remove_drops_the_component()
    {
        using var world = new World();
        var e = world.Create(new Pos(1, 1), new Vel(1, 1));
        world.Remove<Vel>(e);
        Assert.True(world.Has<Pos>(e));
        Assert.False(world.Has<Vel>(e));
    }

    [Fact]
    public void Component_data_survives_archetype_changes()
    {
        using var world = new World();
        var e = world.Create(new Pos(7, 9));
        world.Add(e, new Vel(1, 1));   // archetype change: Pos -> Pos+Vel
        Assert.Equal(new Pos(7, 9), world.GetRef<Pos>(e));
    }

    [Fact]
    public void Tag_components_have_zero_size_and_filter_correctly()
    {
        var ct = ComponentRegistry.Of<Frozen>();
        Assert.True(ct.IsTag);
        Assert.Equal(0, ct.Size);

        using var world = new World();
        var a = world.Create(new Pos(0, 0));
        var b = world.Create(new Pos(0, 0));
        world.Add(b, new Frozen());

        var frozenQuery = new QueryDescription().WithAll<Pos, Frozen>();
        Assert.Equal(1, world.Query(frozenQuery).Count());

        var notFrozen = new QueryDescription().WithAll<Pos>().WithNone<Frozen>();
        Assert.Equal(1, world.Query(notFrozen).Count());

        // suppress unused-variable warnings
        _ = a;
    }

    [Fact]
    public void Query_iterates_all_matching_entities_across_archetypes()
    {
        using var world = new World();
        for (int i = 0; i < 100; i++) world.Create(new Pos(i, i), new Vel(1, 0));
        for (int i = 0; i < 50; i++) world.Create(new Pos(i, i), new Vel(1, 0), new Hp(10));

        var q = new QueryDescription().WithAll<Pos, Vel>();
        int count = 0;
        world.Query(q).ForEach<Pos, Vel>((ref Pos p, ref Vel v) =>
        {
            p.X += v.X;
            count++;
        });
        Assert.Equal(150, count);
    }

    [Fact]
    public void CommandBuffer_defers_destruction_until_playback()
    {
        using var world = new World();
        for (int i = 0; i < 10; i++) world.Create(new Hp(i));

        var cb = new CommandBuffer(world);
        var q = new QueryDescription().WithAll<Hp>();
        world.Query(q).ForEachWithEntity<Hp>((Entity e, ref Hp h) =>
        {
            if (h.Value < 5) cb.Destroy(e);
        });
        Assert.Equal(10, world.EntityCount); // not yet
        cb.Playback();
        Assert.Equal(5, world.EntityCount);
    }

    [Fact]
    public void Many_chunks_are_created_when_count_exceeds_capacity()
    {
        using var world = new World(defaultChunkCapacity: 64);
        for (int i = 0; i < 200; i++) world.Create(new Pos(i, i));

        var arch = world.Archetypes.First(a => a.Has(ComponentRegistry.Of<Pos>().Id) &&
                                               a.ComponentTypes.Length == 1);
        Assert.True(arch.Chunks.Count >= 4); // 200 / 64 -> at least 4 chunks
        Assert.Equal(200, arch.EntityCount);
    }

    [Fact]
    public void System_group_runs_systems_in_registration_order()
    {
        using var world = new World();
        var log = new List<string>();

        var group = new SystemGroup();
        group.Add(new LambdaSystem("a", log));
        group.Add(new LambdaSystem("b", log));
        group.Add(new LambdaSystem("c", log));
        group.Update(world, 0.016f);

        Assert.Equal(["a", "b", "c"], log);
    }

    private sealed class LambdaSystem(string name, List<string> log) : SystemBase
    {
        public override void OnUpdate(in UpdateContext ctx) => log.Add(name);
    }
}
