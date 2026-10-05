# Wordgame Rules

Balatro-style crossword roguelike deckbuilder (working title "wordgame"). Target engine: **Godot 4 (C#, .NET 8)**.

## Core Principles
1. Core game logic must remain in `src/Crossword.Core` with zero game engine imports and no NuGet packages (BCL only). Enforced by `tests/Crossword.Tests/Architecture/CoreDependencyTests.cs`.
2. All state transitions must be pure and return new state instances.
3. **Determinism:** all randomness flows through the immutable `Crossword.Core.Random.Rng` (returns value + next state). Never use `System.Random`, `Guid.NewGuid`, `DateTime.Now`, or engine RNG in Core. The RNG state lives in `RunState`/`RoundState` so any run is reproducible from its seed. Each round gets its own RNG stream derived from the run RNG (`RoundRules.Start`).
4. Every new Desk Item or mechanics feature MUST include unit tests in `tests/Crossword.Tests`.

## Run Structure (implemented)
- **5 Weeks × 3 rounds:** Daily (target ×1, $3) → Saturday Stumper (×1.3, $4) → **Sunday Edition** boss (×1.6, $5 + a `BossModifier`). Week targets 150/320/500/700/950 (`RunConfig.Default`); endless mode after victory (×2/week).
- **Bosses** (`Core/Run/BossModifiers.cs`): Black Squares (symmetric blocked cells), Pocket Edition (5×5), Strict Editor (3+ letter words), Vowel Tax (vowels 0 chips), Tight Deadline (3 submissions). The week's boss is derived from seed + week (no RNG consumed) so it can be previewed.
- **Economy:** start $4; paycheck = base + $1/unused submission + overkill ($1 per full 50% over target, cap 3) + interest ($1 per $5 held, cap 5). Gilded tiles pay during play.
- **Shop** (after each won round): 2 Desk Items (Common $4 / Uncommon $6, sell for half), 2 deck edits (add tile, enhance a chosen tile, strike up to 2 tiles; deck min 30), reroll $5 +$1 each.
- **Tile enhancements:** Bold +10 Chips, Italic +2 Mult, Gilded +$1 — trigger once per formed word containing the tile, including tiles already on the board.
- **State:** `GameSession` (config, `RunState`, phase, current `RoundState`, `ShopState`, last `Payout`); transitions in `RunRules` / `ShopRules`; round mechanics stay in `RoundRules`.

## Game Rules (implemented)
- **Run → Rounds (blinds).** Each round: fresh board (default 7×7) with seeded, rotationally symmetric premium squares; full deck shuffled into the bag; hand of 7.
- **Play (submission):** 1..N hand tiles in one row/column; gaps only if filled by existing tiles; must touch existing tiles (first play may go anywhere); must form ≥1 word of 2+ letters; every word formed (main + cross) must be in the lexicon. Placed tiles are consumed for the round; hand refills.
- **Discard:** costs one discard; removes chosen tiles for the round and refills.
- **Round end:** Won when Score ≥ target; Lost when submissions run out, hand and bag are both empty, or **deadlocked** (no legal play for the hand and no discards left — checked after every transition via `MoveGenerator`).
- **Desk Items:** up to 5 slots (`RunState.MaxDeskSlots`), no duplicates, applied left to right — slot order matters. Starter set in `Core/DeskItems/`, bought in the shop (CLI also has a dev `give` command).
- **Lexicon:** ENABLE, words of 2–15 letters. 2-letter words are legal (lowest tier). QI/ZA are NOT in ENABLE.

## Scoring Order (`ScoringEngine`, one pooled Chips × Mult per play)
1. **Tier:** longest word formed sets base Chips and base Mult (`ScoringConfig.Tiers`).
2. **Words:** each formed word adds Σ letter values (DL/TL per tile) × DW/TW — premiums only under tiles placed this play. Shared tiles count once per word, no extra bonus.
3. **Enhancements:** Bold/Italic/Gilded per formed word containing the tile.
4. **Intersections:** +`IntersectionMult` per new tile that is in both an Across and a Down word.
5. **Desk Items:** `EffectPipeline` in slot order (+Chips, +Mult, ×Mult).
6. **Total:** floor(Chips × Mult). Boss modifiers may alter the `ScoringConfig` for the round (`RoundConfig.EffectiveScoring`). Every step appends an `EffectEvent` (with running totals) for UI playback.
All numbers live in `ScoringConfig` / `RoundConfig` / `PremiumPairs` / Desk Item constructor defaults — tune there, not in code.

## Balance Workflow
- `RoundSimulator` (Core/Analysis) plays rounds automatically; `skill` 1.0 = greedy best play (upper bound), 0.9 ≈ strong human proxy.
- CLI `sim [rounds]` prints per-submission score distribution, intersection rate and submissions-to-target; `hint [n]` lists best plays.
- `RunSimulator` plays whole runs with a naive shop bot; CLI `runsim [runs] [skill]` reports victory %, week reached and what killed runs. Week targets are tuned so a skill-0.9 bot wins ~40–45%.
- Known gap: the bot's desk fills by ~round 5 and money piles up (~$30 unspent) — late-game scaling is content-limited; retune targets when scaling content lands.
- Current tuning rationale: tier Mult 1,1,2,2,3,3 + 3 Mult per intersection so grid-building beats isolated long words; round 1 target 300 (~90% clear rate at skill 0.9); starter items ~+35–55% alone.
- Unit tests must pin their own numbers (explicit config / constructor args) so retuning defaults never breaks them.

## Layout
- `src/Crossword.Core` — `Domain/` (state records, board, premium layout), `Rules/` (placement validation, round/draw transitions), `Scoring/`, `Effects/` (Desk Item pipeline), `DeskItems/` (concrete items + catalog), `Analysis/` (move generator, ranker, round + run simulators), `Run/` (run config, economy, bosses, shop, `GameSession`/`RunRules`), `Lexicon/` (DAWG + embedded ENABLE, `IWordGraph` traversal), `Random/`
- `src/Crossword.Cli` — developer QA console (parser, renderer, simulation report, REPL); references Core only
- `tests/Crossword.Tests` — xUnit tests, mirroring Core folder structure (+ `Cli/` parser tests, `TestSupport/Fixtures`)
- `run_local_qa.bat [--ci] [seed]` — build → test → launch CLI (double-click; `--ci` skips pauses). Keep it working as the project evolves.

## Commands
- Full QA loop: `run_local_qa.bat`
- Run all tests: `dotnet test`
- Run scoring tests only: `dotnet test --filter Category=Scoring`
- Play a specific seed: `run_local_qa.bat 42` or `dotnet run --project src/Crossword.Cli -- 42`
- Other categories: `Lexicon`, `Determinism`, `Architecture`

## Code Conventions
- Use immutable C# `record` types for state objects; collections are `ImmutableArray`/`ImmutableList`. Note `ImmutableArray` compares by reference inside records — compare contents explicitly in tests.
- Desk Items implement `IDeskItem.Apply(ScoreContext) -> ScoreContext` as pure functions and are applied strictly in slot order by `EffectPipeline`. Record each firing via `ScoreContext.Record(sourceId, description)`; the engine layer animates from that log (no observers/events in Core).
- Tag tests with `[Trait("Category", "...")]` (e.g. `Scoring`, `Determinism`, `Architecture`).
- Expected rule violations return `Result<TValue, TError>` (`PlacementError`, `RoundError`) — exceptions are for programmer errors only.
- Warnings are errors (`Directory.Build.props`). Nullable reference types are on.

## Licensing (commercial project)
- Avoid dependencies with commercial-use restrictions (e.g. FluentAssertions v8+). Use xUnit's built-in `Assert`.
- Word lists: TWL/Collins (SOWPODS) require a license. Use public-domain lists (e.g. ENABLE) unless a license is obtained.
