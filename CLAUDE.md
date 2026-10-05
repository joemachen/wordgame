# Wordgame Rules

Balatro-style crossword roguelike deckbuilder (working title "wordgame"). Target engine: **Godot 4 (C#, .NET 8)**.

> **Start of every session: read [`handoff.md`](handoff.md)** — current status, decisions, open concerns, next steps, and workflow tips.
> **End of every meaningful chunk of work: update `handoff.md`** (status table, decisions, next steps, HEAD/test counts, date).
> **Feature design roadmap: [`ROADMAP.md`](ROADMAP.md)** — the official plan for upcoming features (dictionaries, decks, items, Stationery, bosses, stakes, infra/simulation, game modes, presentation, persistence, build order) with *proposed* numbers. Mark entries ✅ when they land; keep current status in `handoff.md`.

## Core Principles
1. Core game logic must remain in `src/Crossword.Core` with zero game engine imports and no NuGet packages (BCL only). Enforced by `tests/Crossword.Tests/Architecture/CoreDependencyTests.cs`.
2. All state transitions must be pure and return new state instances.
3. **Determinism:** all randomness flows through the immutable `Crossword.Core.Random.Rng` (returns value + next state). Never use `System.Random`, `Guid.NewGuid`, `DateTime.Now`, or engine RNG in Core. The RNG state lives in `RunState`/`RoundState` so any run is reproducible from its seed. Each round gets its own RNG stream derived from the run RNG (`RoundRules.Start`).
4. Every new Desk Item or mechanics feature MUST include unit tests in `tests/Crossword.Tests`.

## Run Structure (implemented)
- **5 Weeks × 3 rounds:** Daily (target ×1, $3) → Saturday Stumper (×1.3, $4) → **Sunday Edition** boss (×1.6, $5 + a `BossModifier`). Week targets 340/1200/3600/9750/16000 (`RunConfig.Default`; boss-adjusted deadlines via `RunRules.TargetFor`); endless mode after victory (×2/week).
- **Bosses** (`Core/Run/BossModifiers.cs`), tiered by week via `RunConfig.BossTiers` (`BossCatalog.DefaultTiers`, ordered by measured difficulty): **Early** (Weeks 1–2) Ink Spill (6 symmetric blocked cells), Tight Margins (5×5); **Mid** (Weeks 3–4) Vowel Drought (vowels −1 chip), Tight Deadline (3 submissions); **Final** (Week 5) The Strict Grammarian (3+ letter words; its deadline is ×0.75 via `TargetScale`, otherwise the finale ends ~45% of runs that reach it). Endless weeks draw from every boss. Play totals are clamped at 0. The week's boss is picked from its tier pool using seed + week only (no RNG consumed) so it can be previewed.
- **Economy:** start $4; paycheck = base + $1/unused submission + overkill ($1 per full 50% over target, cap 3) + interest ($1 per $5 held, cap 5). Gilded tiles pay during play.
- **Shop** (after each won round): 2 Desk Items (rarity rolled 60/30/10: Common $4 / Uncommon $6 / Rare $8, sell for half), 2 deck edits (add tile, enhance a chosen tile, strike up to 2 tiles; deck min 30), 1 **Style Guide** ($3, named per tier via `StyleGuideNames`: Pulp Paperbacks 2 → The Lexicographer's Omnibus 7+; permanently levels a word tier, +`LevelChips`/+`LevelMult`, stored in `RunState.TierUpgrades`), 1 **Stationery** ($3), reroll $5 +$1 each.
- **Stationery** (`Core/Stationery/`): one-shot items in `RunState.Stationery` (2 slots, duplicates allowed, sell for half), used in-round via `RunRules.UseStationery(session, slot, lexicon, tileIds?, cell?)`. Each item declares a `StationeryTarget` (None / HandTiles / BoardTile); an item that can't take effect fails and stays in its slot. Catalog: **Answer Key** (reveals the best play; the UI places it as pending tiles), **Margin Clip** (+1 submission this round), **Scissors** (redraw up to 2 selected hand tiles without spending a discard; `RoundRules.Redraw`), **White-Out** (remove a board tile for the round; `RoundRules.RemoveTile`, UI board-targeting mode), **Red Ink Bottle** (+3 Mult on every play this round, stacks; stored as `RoundConfig.BonusMult`). `ShopConfig.StationeryIds` restricts the shop pool (null = all; the offer is rolled last so it never shifts other offers).
- **Deadlock escape:** holding Scissors (bag not empty) or White-Out (board not empty) keeps a stuck round (no legal play, no discards) alive (`StationeryCatalog.CanEscape`); `RunRules` re-checks after every transition, including using or selling Stationery (`RunRules.Recheck`), so selling the last escape item while stuck loses the round.
- **Hint:** the free in-round Hint shows a *decent* play, never the best (`Hints.Decent`, `HintConfig`: the 90th-percentile play or the best play worth ≤60% of the best score, whichever is lower). The best play costs an Answer Key. Game `--dev` makes the Hint button show the best play.
- **Tile enhancements:** Bold +10 Chips, Italic +2 Mult, Gilded +$1 — trigger once per formed word containing the tile, including tiles already on the board.
- **State:** `GameSession` (config, `RunState`, phase, current `RoundState`, `ShopState`, last `Payout`); transitions in `RunRules` / `ShopRules`; round mechanics stay in `RoundRules`.

## Game Rules (implemented)
- **Run → Rounds (blinds).** Each round: fresh board (default 7×7) with seeded, rotationally symmetric premium squares; full deck shuffled into the bag; hand of 7.
- **Play (submission):** 1..N hand tiles in one row/column; gaps only if filled by existing tiles; must touch existing tiles (first play may go anywhere); must form ≥1 word of 2+ letters; every word formed (main + cross) must be in the lexicon. Placed tiles are consumed for the round; hand refills.
- **Discard:** costs one discard; removes chosen tiles for the round and refills.
- **Round end:** Won when Score ≥ target; Lost when submissions run out, hand and bag are both empty, or **deadlocked** (no legal play for the hand and no discards left — checked after every transition via `MoveGenerator`; held Scissors / White-Out postpone it, see Stationery).
- **Desk Items:** up to 5 slots (`RunState.MaxDeskSlots`), no duplicates, applied left to right — slot order matters. 18 items in `Core/DeskItems/` (Starter, Conditional, Scaling), bought in the shop (CLI also has a dev `give` command).
- **Lexicon:** ENABLE, words of 2–15 letters. 2-letter words are legal (lowest tier). QI/ZA are NOT in ENABLE.
- **Definitions** (UI only, no rule depends on them): `DefinitionLoader.Default.Define(word)` from embedded `Lexicon/Data/definitions.tsv.gz` — Open English WordNet 2025 (CC BY 4.0, attribution in `THIRD_PARTY_NOTICES.md`) plus hand-written `tools/Crossword.DefinitionsBuilder/supplement.txt` (function words, all 2-letter words; overrides WordNet). Inflections point at their lemma. ~62% of ENABLE is covered; the rest shows "no definition on file". Regenerate with `dotnet run -c Release --project tools/Crossword.DefinitionsBuilder` (needs `english-wordnet-2025.xml.gz` in gitignored `tools/data/`).

## Scoring Order (`ScoringEngine`, one pooled Chips × Mult per play)
1. **Tier:** longest word formed sets base Chips and base Mult (`ScoringConfig.Tiers`).
2. **Words:** each formed word adds Σ letter values (DL/TL per tile) × DW/TW — premiums only under tiles placed this play. Shared tiles count once per word, no extra bonus.
3. **Enhancements:** Bold/Italic/Gilded per formed word containing the tile.
4. **Intersections:** +`IntersectionMult` per new tile that is in both an Across and a Down word.
4b. **Round bonus:** +`ScoringConfig.BonusMult` (Red Ink Bottle, via `RoundConfig.EffectiveScoring`; source `bonus`).
5. **Desk Items:** `EffectPipeline` in slot order (+Chips, +Mult, ×Mult).
6. **Total:** floor(Chips × Mult). Boss modifiers may alter the `ScoringConfig` for the round (`RoundConfig.EffectiveScoring`). Every step appends an `EffectEvent` (with running totals) for UI playback.
All numbers live in `ScoringConfig` / `RoundConfig` / `PremiumPairs` / Desk Item constructor defaults — tune there, not in code.

## Balance Workflow
- `RoundSimulator` (Core/Analysis) plays rounds automatically; `skill` 1.0 = greedy best play (upper bound). Two skill models (`PlayChooser`, `SkillModel`): **Percentile** (default; 0.9 = the play better than 90% of legal plays — well below the best, since most legal plays are tiny) and **ScoreFraction** (0.9 = the best play worth ≤90% of the best score; closer to how a human plays — they find good plays, just not always the top one).
- CLI `sim [rounds]` prints per-submission score distribution, intersection rate and submissions-to-target; `hint [n]` lists best plays.
- `RunSimulator` plays whole runs; CLI `runsim [runs] [skill] [naive] [frac]` reports victory %, week reached, unspent money, **mean submissions to win per week/day** and what killed runs (`frac` = ScoreFraction skill model). Default shop bot is `EvaluatingShopBot` (`Core/Analysis/ShopBot.cs`): it records recent decisions (trimmed candidate plays) and values each purchase by re-scoring them with the resulting loadout — every slot position, replacements when full, Style Guides, scaling items projected to mid-run, money effects as reduced price. Thresholds in `ShopBotConfig`. `NaiveShopBot` (priciest item first) kept for comparison. Stationery: the evaluating bot buys it at a fixed estimated gain per item (`ShopBotConfig.StationeryGain`, like deck edits) and `StationeryBot` uses it in rounds (Answer Key when only the best play wins the round; Margin Clip / Red Ink on a short last submission; Red Ink on a boss opener; Scissors / White-Out to escape a stuck hand). `SimulatedRun.StationeryUsed` lists what was used.
- **Balance reference = ScoreFraction model** (`runsim 150 0.75 frac`), not the percentile bot (its 0.9 is far weaker than a human). Current numbers (150 runs, evaluating bot): wins ~45% / 31% / 13% at ScoreFraction 0.9 / 0.75 / 0.6; rounds take ~2.1–2.6 submissions and 65–74% are won in 1–2 (was 81–87% before the 2026-10-05 retune). Targets alone plateau at ~2.5 submissions per round: steeper curves turn long rounds into lost rounds (4 submissions, high per-play variance). Getting 3+ play rounds would need a structural change (e.g. 5 submissions + higher targets).
- Findings: shop decisions dominate outcomes; Pulitzer, Margin Notes and Word Count end up in >90% of skill-0.9 evaluating runs; deck edits never pay off for the bot (buying them at any estimated gain lowered win rate). Retune targets with `runsim` whenever scaling content changes.
- Current tuning rationale: tier Mult 1,1,2,2,3,3 + 3 Mult per intersection so grid-building beats isolated long words; starter items ~+35–55% alone.
- Unit tests must pin their own numbers (explicit config / constructor args) so retuning defaults never breaks them.

## Layout
- `src/Crossword.Core` — `Domain/` (state records, board, premium layout), `Rules/` (placement validation, round/draw transitions), `Scoring/`, `Effects/` (Desk Item pipeline), `DeskItems/` (concrete items + catalog), `Stationery/` (one-shot items + catalog), `Analysis/` (move generator, ranker, hints, round + run simulators), `Run/` (run config, economy, bosses, shop, `GameSession`/`RunRules`), `Lexicon/` (DAWG + embedded ENABLE, `IWordGraph` traversal, word definitions), `Random/`
- `game/` — **Godot 4.7 (.NET) UI** (`Wordgame.Godot.csproj`, `project.godot`, `Scenes/Main.tscn`). UI is built in code in `Scripts/Main*.cs` + `UiKit.cs`; it only renders `GameSession` and calls `RunRules`/`ShopRules` — no rules in the UI. Visual-only state (selected/pending tiles) lives in `Main`. Compatibility renderer.
- `tools/Crossword.DefinitionsBuilder` — one-off data tool that generates the embedded definitions file
- `src/Crossword.Cli` — developer text console (parser, renderer, simulation report, REPL); references Core only
- `tests/Crossword.Tests` — xUnit tests, mirroring Core folder structure (+ `Cli/` parser tests, `TestSupport/Fixtures`)
- `ROADMAP.md` — feature design roadmap; `handoff.md` — session status brief
- `run_local_qa.bat [--cli] [--ci] [seed]` — build → test → launch the **Godot game window** (double-click). `--cli` uses the text console, `--ci` skips pauses. Godot path comes from `GODOT` in gitignored `qa.local.bat` (see `qa.local.bat.example`); falls back to the console if missing. Keep it working as the project evolves.

## Commands
- Full QA loop: `run_local_qa.bat`
- Run all tests: `dotnet test`
- Run scoring tests only: `dotnet test --filter Category=Scoring`
- Play a specific seed: `run_local_qa.bat 42` (game) or `run_local_qa.bat --cli 42` (console)
- Game dev flags (after `--`): `"%GODOT%" --path game -- --seed=42 --give=red-pen,pulitzer,answer-key --autoplay=3 --hint --dev --screenshot=out.png` (`--give` takes Desk Item or Stationery ids; `--hint` pre-places the best play; `--dev` makes the Hint button show the best play; screenshot saves and quits — use it to visually verify UI changes).
- UI regression check: `"<godot>_console.exe" --path game -- --seed=42 --selftest` drives the real UI with simulated mouse/keyboard input (select, drag-reorder incl. ghost slot/slide/gap drop/cancel, drag-to-board, shuffle, recall, hint definitions + hint is not the best play, Style Guides popup, every Stationery item incl. White-Out targeting) and prints PASS/FAIL; exit code 1 on failure. Extend `Main.SelfTest.cs` when adding interactions.
- Other categories: `Lexicon`, `Determinism`, `Architecture`

## Code Conventions
- Use immutable C# `record` types for state objects; collections are `ImmutableArray`/`ImmutableList`. Note `ImmutableArray` compares by reference inside records — compare contents explicitly in tests.
- Scaling Desk Items grow via `AfterPlay`/`AfterRoundWon`, which return an updated copy (applied by `RunRules`); items needing run resources read `ScoreContext.Env` (money held, submissions left incl. current, discards left).
- Desk Items implement `IDeskItem.Apply(ScoreContext) -> ScoreContext` as pure functions and are applied strictly in slot order by `EffectPipeline`. Record each firing via `ScoreContext.Record(sourceId, description)`; the engine layer animates from that log (no observers/events in Core).
- Tag tests with `[Trait("Category", "...")]` (e.g. `Scoring`, `Determinism`, `Architecture`).
- Expected rule violations return `Result<TValue, TError>` (`PlacementError`, `RoundError`) — exceptions are for programmer errors only.
- Warnings are errors (`Directory.Build.props`). Nullable reference types are on.

## Licensing (commercial project)
- Avoid dependencies with commercial-use restrictions (e.g. FluentAssertions v8+). Use xUnit's built-in `Assert`.
- Word lists: TWL/Collins (SOWPODS) require a license. Use public-domain lists (e.g. ENABLE) unless a license is obtained.
