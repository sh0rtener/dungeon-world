namespace MicroEcs;

/// <summary>
/// Marker interface for all components. Components must be value types (struct).
/// You typically don't need to implement this — components are detected by the <see cref="ComponentType"/> registry —
/// but implementing it makes intent explicit and lets analyzers verify shape.
/// </summary>
public interface IComponent { }

/// <summary>
/// Marker interface for tag components (zero-sized).
/// Tags are fully supported as ordinary components but the framework can short-circuit
/// data movement for them, since there's nothing to copy.
/// </summary>
public interface ITag : IComponent { }
