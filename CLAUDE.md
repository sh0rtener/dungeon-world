# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

Два связанных проекта в одном solution:

1. **MicroEcs** — самописный archetype-based Entity Component System фреймворк на чистом C# 14 / .NET 10. Без сторонних зависимостей, без `unsafe`, без source-генераторов.

2. **MicroEcs.Dungeon** — консольная roguelike-игра на этом фреймворке. Порт [sh0rtener/dungeon-world](https://github.com/sh0rtener/dungeon-world) с процедурной генерацией карты, пещерами через клеточный автомат и тупиковыми ответвлениями.

---

## Команды

```bash
# Запуск игры
dotnet run -c Release --project MicroEcs/samples/MicroEcs.Dungeon
# Стрелки / WASD / HJKL — движение. Q / Esc — выход. > — цель.

# Тесты
dotnet test MicroEcs/MicroEcs.sln

# Бенчмарки
dotnet run -c Release --project MicroEcs/src/MicroEcs.Benchmarks -- --filter "*"
```

---

## Структура solution

```
MicroEcs/
├── src/
│   ├── MicroEcs/                  ← сам фреймворк
│   │   ├── Entity.cs              ← struct {Id, Version}
│   │   ├── IComponent.cs          ← маркер-интерфейсы IComponent, ITag
│   │   ├── ComponentType.cs       ← глобальный реестр типов + TypeCache<T>
│   │   ├── BitSet.cs              ← битсет для сигнатур архетипов
│   │   ├── Archetype.cs           ← один уникальный набор компонентов + chunks
│   │   ├── Chunk.cs               ← SoA-хранилище 256 entities (колонки per type)
│   │   ├── QueryDescription.cs    ← WithAll / WithAny / WithNone фильтры
│   │   ├── Query.cs               ← ForEach<T1..T4>, ForEachWithEntity, ForEachChunk
│   │   ├── World.cs               ← Create/Destroy/Add/Remove/Set/GetRef + MatchingArchetypes
│   │   ├── CommandBuffer.cs       ← отложенные мутации для безопасной итерации
│   │   └── Systems.cs             ← ISystem, SystemBase, SystemGroup, UpdateContext
│   └── MicroEcs.Benchmarks/
├── tests/
│   └── MicroEcs.Tests/            ← xUnit
└── samples/
    ├── MicroEcs.Sample/           ← headless demo, 10k entities
    └── MicroEcs.Dungeon/          ← игра
```

Также есть `DungeonWorld.sln` в корне — устаревшая .NET 8 OOP-версия без ECS, не в активной разработке.

---

## Архитектура фреймворка

### Хранение данных (archetype + chunks)

```
World
 ├── Archetype (Position, Velocity)
 │     ├── Chunk #0  [256 entities]
 │     │     ├── column<Position>[256]   ← SoA: каждый тип в своём массиве
 │     │     └── column<Velocity>[256]
 │     └── Chunk #1  ...
 └── Archetype (Position, Velocity, Health)
       └── Chunk #0  ...
```

- Каждый архетип = один уникальный набор компонентов. Сигнатура в `BitSet`.
- Добавление/удаление компонента = перемещение entity в другой архетип (swap-back из старого chunk).
- Переходы кешируются в `Archetype.AddEdges / RemoveEdges` → O(1) после первого раза.
- `ComponentRegistry.Of<T>()` — stable int id через `TypeCache<T>`, inlined static field load, без рефлексии.

### Запросы

```csharp
// QueryDescription держи в поле системы, не создавай каждый кадр — иначе аллоцирует BitSet.
private readonly QueryDescription _q = new QueryDescription()
    .WithAll<Position, Velocity>()
    .WithNone<Frozen>();

// ref напрямую в chunk, без копий
world.Query(_q).ForEach<Position, Velocity>((ref Position p, ref Velocity v) =>
{
    p.X += v.Dx;
    p.Y += v.Dy;
});

// С entity handle:
world.Query(_q).ForEachWithEntity<Health>((Entity e, ref Health h) =>
{
    if (h.Current <= 0) commandBuffer.Destroy(e);
});

// Chunk-уровень для ручных оптимизаций:
world.Query(_q).ForEachChunk(chunk => {
    var pos = chunk.GetSpan<Position>();
    var vel = chunk.GetSpan<Velocity>();
});
```

### CommandBuffer — структурные изменения во время итерации

`ForEach` итерирует по `Span<T>` напрямую. `Add/Remove/Destroy` меняет архетипы и переставляет данные — это инвалидирует span. **Нельзя мутировать world внутри ForEach.** Всегда откладывай через `CommandBuffer`:

```csharp
var cb = new CommandBuffer(world);
world.Query(q).ForEachWithEntity<Health>((Entity e, ref Health h) =>
{
    if (h.Current <= 0) cb.Destroy(e);
});
cb.Playback();
```

### Теги

```csharp
public struct PlayerTag : ITag { }  // struct без полей, только в сигнатуре архетипа
```

`chunk.GetSpan<T>()` бросает exception для тегов — у них нет column-данных. Теги используй только в `QueryDescription` фильтрах.

### Системы

```csharp
public sealed class MovementSystem : SystemBase
{
    private readonly QueryDescription _q = new QueryDescription().WithAll<Position, Velocity>();

    public override void OnUpdate(in UpdateContext ctx)
    {
        ctx.World.Query(_q).ForEach<Position, Velocity>((ref Position p, ref Velocity v) =>
        {
            p.X += v.Dx;
            v = default; // сброс intent после применения
        });
    }
}

var group = new SystemGroup("Main");
group.Add(new MovementSystem());
group.Update(world, deltaTime);
```

---

## Игра: MicroEcs.Dungeon

### Компоненты

| Компонент | Тип | Описание |
|-----------|-----|----------|
| `Position` | record struct | Координаты на карте |
| `Velocity` | record struct | Намерение движения за тик (сбрасывается в 0) |
| `Renderable` | record struct | Символ + цвет для консольного рендера |
| `Layer` | record struct | Z-порядок: 0=пол, 1=стены, 5=цель, 10=игрок |
| `PlayerTag` | tag | Метка игрока |
| `Blocker` | tag | Блокирует движение |
| `Goal` | tag | Плитка выхода |
| `Floor` | tag | Декоративный пол |

### Системы

| Система | Что делает |
|---------|------------|
| `InputSystem` | Читает из thread-safe очереди ключей → пишет `Velocity` на игрока |
| `MovementSystem` | Применяет `Velocity` → `Position`, проверяя `Blocker` |
| `GoalSystem` | Если `Position` игрока == `Position` цели → победа |
| `RenderSystem` | Рисует все `Position+Renderable+Layer`, камера на игроке, diff-рендер (без `Console.Clear`) |

### Генератор карты (5 проходов)

1. **Размещение комнат** — N прямоугольников без перекрытий; ~40% помечаются cave (крупнее).
2. **Клеточный автомат** — для cave-комнат: шум 45% → 4 итерации правила 4-5.
3. **Коридоры** — L-образные туннели; `_isCorridor[][]` отмечает ячейки.
4. **Тупиковые ответвления** — от corridor-ячеек с вероятностью 12%, перпендикулярно оси, 3-7 тайлов; останавливается на встреченном floor.
5. **Починка** — `EnsureCaveCentersReachable` (3×3 вокруг центра cave), `PruneDisconnectedFloor` (BFS), затем материализация entity.

---

## Технологии

- **.NET 10 / C# 14** — collection expressions, primary constructors, `record struct`
- **xUnit** — тесты (`MicroEcs/tests/MicroEcs.Tests/`)
- **BenchmarkDotNet** — бенчмарки
- Никаких NuGet-зависимостей в самом фреймворке
