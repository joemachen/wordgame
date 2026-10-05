# Wordgame Rules

Balatro-style crossword roguelike deckbuilder (working title "wordgame"). Target engine: **Godot 4 (C#, .NET 8)**.

## Core Principles
1. Core game logic must remain in `src/Crossword.Core` with zero game engine imports and no NuGet packages (BCL only). Enforced by `tests/Crossword.Tests/Architecture/CoreDependencyTests.cs`.
2. All state transitions must be pure and return new state instances.
3. **Determinism:** all randomness flows through the immutable `Crossword.Core.Random.Rng` (returns value + next state). Never use `System.Random`, `Guid.NewGuid`, `DateTime.Now`, or engine RNG in Core. The RNG state lives in `RunState` so any run is reproducible from its seed.
4. Every new Desk Item or mechanics feature MUST include unit tests in `tests/Crossword.Tests`.

## Layout
- `src/Crossword.Core` — domain (`Domain/`), pure transitions (`Rules/`), Desk Item pipeline (`Effects/`), RNG (`Random/`)
- `src/Crossword.Cli` — developer QA console; references Core only
- `tests/Crossword.Tests` — xUnit tests, mirroring Core folder structure
- `run_local_qa.bat` — build → test → launch CLI (double-click; `--ci` skips pauses). Keep it working as the project evolves.

## Commands
- Full QA loop: `run_local_qa.bat`
- Run all tests: `dotnet test`
- Run scoring tests only: `dotnet test --filter Category=Scoring`
- Play a specific seed: `dotnet run --project src/Crossword.Cli -- <seed>`

## Code Conventions
- Use immutable C# `record` types for state objects; collections are `ImmutableArray`/`ImmutableList`. Note `ImmutableArray` compares by reference inside records — compare contents explicitly in tests.
- Desk Items implement `IDeskItem.Apply(ScoreContext) -> ScoreContext` as pure functions and are applied strictly in slot order by `EffectPipeline`. Record each firing via `ScoreContext.Record(EffectEvent)`; the engine layer animates from that log (no observers/events in Core).
- Tag tests with `[Trait("Category", "...")]` (e.g. `Scoring`, `Determinism`, `Architecture`).
- Warnings are errors (`Directory.Build.props`). Nullable reference types are on.

## Licensing (commercial project)
- Avoid dependencies with commercial-use restrictions (e.g. FluentAssertions v8+). Use xUnit's built-in `Assert`.
- Word lists: TWL/Collins (SOWPODS) require a license. Use public-domain lists (e.g. ENABLE) unless a license is obtained.
