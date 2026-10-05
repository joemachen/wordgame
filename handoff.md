# Session Handoff — wordgame

> **For a new Claude session:** read this file first, then `CLAUDE.md` (rules, architecture, commands).
> This file is the "where are we and what's next" brief; `CLAUDE.md` is the "how the code works" reference.
> **Update this file** (status, decisions, next steps, date) at the end of any meaningful chunk of work.

_Last updated: 2026-10-05 · HEAD `74e9fd8` · 240 unit tests passing · UI self-test 9/9 passing_

---

## 1. What this is

A **Balatro-style crossword roguelike deckbuilder** (working title "wordgame"), commercial indie project.
Players build words on a persistent crossword grid each round; scoring is Balatro's Chips × Mult fused with
Scrabble geometry. C# / .NET 8. Headless rules engine (`src/Crossword.Core`) + **Godot 4.7 .NET UI** (`game/`)
+ a developer text console (`src/Crossword.Cli`). Repo: https://github.com/joemachen/wordgame (branch `main`).

## 2. Current status (everything below is built, tested, and pushed)

| Area | State |
|---|---|
| Lexicon | ENABLE (public domain), embedded; DAWG (~1 MB, 0.5 s build); words 2–15 letters. **QI/ZA are not valid** (not in ENABLE). |
| Board & rules | 7×7 persistent grid per round, premium squares (seeded, symmetric), black squares, placement validation, cross words, deadlock detection. |
| Scoring | Pooled Chips × Mult per play: tier (longest word) → word chips (DL/TL/DW/TW, new tiles only) → tile enhancements → intersections (+3 Mult each) → Desk Items (slot order). |
| Run | 5 Weeks × (Daily, Saturday Stumper, Sunday Edition boss). Paycheck economy with interest + overkill bonus. Endless mode. |
| Content | 18 Desk Items (Common/Uncommon/Rare, incl. scaling items), 3 tile enhancements, Style Guide tier upgrades, 5 bosses, shop deck edits (add/enhance/strike). |
| Tooling | Move generator, greedy `RoundSimulator`, whole-run `RunSimulator` with naive shop bot (`runsim`), CLI `hint`/`sim`. |
| UI (Godot) | Full playable loop: board, hand (click/type/drag, shuffle, reorder), live score preview, animated scoring, Desk Items bar (reorder/sell), shop + tile picker, paycheck, win/lose screens. First-pass visuals (no art, sound, or tile animations yet). |
| QA | `run_local_qa.bat` (double-click): build → tests → opens game window. `--cli` for console. |

## 3. Decisions already made (don't re-litigate without the user)

- **Engine:** Godot 4 (C#). Core targets net8.0, BCL only. Godot 4.7.1 .NET installed at `D:\Projects\Godot\Godot_v4.7.1-stable_mono_win64\` (path lives in gitignored `qa.local.bat`).
- **Desk Items are pure functions in an ordered pipeline**, not `IObserver` events. Scaling items evolve via `AfterPlay`/`AfterRoundWon` returning updated copies.
- **Determinism:** all Core randomness via immutable `Rng`. Hand *display order* is cosmetic (UI-level, own RNG stream) — never affects game state.
- **Scoring is pooled** (one Chips × Mult per play), chosen over per-word sums. Longest word sets the tier; all words add chips.
- **2-letter words are legal** (lowest tier). **Discards exist** (3/round). **Premiums only count under newly placed tiles**; **enhancements trigger per formed word, including old tiles** (deliberate: rewards building onto the grid).
- **Economy:** Balatro-style payouts **plus capped overkill bonus** (user's choice). **Run length:** 5 Weeks × 3 rounds (user's choice). **Newspaper theme** (Week / Daily / Saturday Stumper / Sunday Edition, Desk Items, deadlines).
- **Balance is data-driven:** all numbers live in config records (`ScoringConfig`, `RunConfig`, `ShopConfig`, `EconomyConfig`, Desk Item constructor defaults) and are tuned with simulations, not by hand. Unit tests pin their own numbers so retuning never breaks them.
- **Licensing:** no commercially-restricted deps (e.g. FluentAssertions v8). Word lists: public domain only unless licensed.

## 4. Current balance snapshot (100-run sims, `RunSimulator`)

Week targets 150/400/900/1900/3800, day multipliers ×1/×1.3/×1.6.
Win rate: skill 0.9 ≈ 42%, 0.8 ≈ 29%, 0.7 ≈ 15%. Boss loss rate 4–16% per encounter. End-of-run unspent money ≈ $22 (skill 0.9).
Re-run with CLI `runsim 100 0.9` (or the scratch harness pattern described in §7) after any content/scoring change.

## 5. Open concerns / known gaps

1. **Skill gap is narrow** (0.9 vs 0.8 only ~13 pts apart): build strength dominates word-finding skill. Design levers proposed but not chosen: items rewarding word *quality* (rare letters, length, patterns), fewer unconditional ×Mult effects. May partly be an artifact of the naive shop bot. **Needs user input after playtesting.**
2. **Shop bot is naive** (buys priciest affordable item). A smarter bot would make tuning more trustworthy.
3. **No save/load.** State is immutable records, so it's mostly serialization (Desk Items are polymorphic records — needs a type discriminator).
4. **UI is first-pass:** no art, sound, tile animations, or settings; drag preview feel only verified via simulated input.
5. **Content hygiene for release:** ENABLE contains slurs — needs a denylist before shipping. "Q without U" is a dead tile (consider a "Qu" tile).
6. **Hand arrangement is UI-only** (not saved); fine until save/load exists.

## 6. Suggested next steps (offered to the user; they haven't picked yet)

1. User playtests a few runs via `run_local_qa.bat` → recalibrate targets/feel from their feedback.
2. Smarter shop bot → re-check the skill-gap concern.
3. Save/load (resume a run).
4. UI polish: tile placement/score animations, sound, juice; deck viewer; tooltips for Desk Items/bosses.
5. More content: Rare items, more bosses, more enhancements, consumables (Tarot-like one-shots).

## 7. How to work in this repo (practical tips learned the hard way)

- **Verify, don't assume.** After changes: `dotnet build wordgame.sln` (warnings are errors) → `dotnet test`. For UI changes also run the screenshot and self-test flags:
  - `"D:/Projects/Godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe" --path game -- --seed=42 --screenshot=<scratchpad>/shot.png` then view the PNG. Extra flags: `--give=red-pen,pulitzer --autoplay=3 --hint`.
  - `... --path game -- --seed=42 --selftest` → PASS/FAIL lines, exit 1 on failure. Extend `game/Scripts/Main.SelfTest.cs` for new interactions.
  - Build the Godot project (`dotnet build game/Wordgame.Godot.csproj`) before launching Godot; it loads assemblies from `game/.godot/mono/temp/bin`.
- **Desktop control (computer-use) can't target the portable Godot exe** — use `--selftest`/`--screenshot` instead.
- **Simulated input quirk:** under `Viewport.PushInput`, `_DropData`'s `atPosition` arrives in the wrong coordinate space. Don't base UI logic on it (hand reorder uses drag direction instead).
- **Editing gotchas:** the Write/Edit tools turn `\uXXXX` escapes into literal characters, and bash heredocs can mangle `\n`. For multi-file edits, write a Python script with raw strings (`r"""..."""`) to the scratchpad and run it; for C# char literals prefer `(char)0xFEFF` style.
- **Batch files must be CRLF** (`.gitattributes` enforces; normalize with `sed -i 's/\r*$/\r/'` after writing).
- Running `run_local_qa.bat` from a captured shell hangs because Godot inherits the pipe — expected; it's fine on double-click.
- Balance experiments: a throwaway console project in the scratchpad referencing `src/Crossword.Core` (loop over configs, call `RunSimulator.PlayRun` in parallel) is faster than editing defaults repeatedly. Note `RunConfig.Days` multipliers must be set explicitly in such harnesses.
- The user's machine has old Godot crash dumps; the project uses the **GL Compatibility** renderer, which has been stable.

## 8. Working with the user

- Wants Claude to **flag unsound decisions or anything against the spirit of the game** — give a recommendation, not just options.
- Prefers seeing things in the **real game UI** (disliked the console). Keep `run_local_qa.bat` double-clickable and working.
- Practice so far: work in focused commits on `main` with descriptive messages and push to `origin` after each verified chunk; the user has been fine with this.
- Starts new chats periodically to keep context small → **keep this file current**.

## 9. Commit history (newest first)

```
74e9fd8 Add hand shuffle and drag-and-drop tile arrangement to the game UI
d76b7f5 Add Godot 4 game UI and launch it from run_local_qa.bat
ed24341 Tune bosses and steepen week targets for the expanded content
9f3d345 Add 12 Desk Items, scaling hooks, run environment and rarity weights
c5493c2 Add Style Guide word-tier upgrades to the shop
546f3e6 Add run simulator with shop bot and tune week targets
621e79d Add run progression, economy, shop with deck edits, and session CLI
34eb50c Add Sunday Edition boss modifiers, black squares and min word length
711db6c Add tile enhancements (Bold, Italic, Gilded) to scoring
3f3afa2 Add starter Desk Items with slot management and CLI commands
8d9c1f7 Add greedy round simulator, hint/sim CLI commands, and tune scoring
e3edb03 Detect deadlocked rounds (no legal play and no discards left)
33aa175 Add legal move generator over the DAWG
c0db0d4 Add playable CLI round loop and document game rules
69ab247 Add round loop with submissions, discards, and seeded premium layouts
3741bd2 Add pooled Chips x Mult scoring engine
8f20a69 Add board, placement validation, and word extraction
0bf2e7d Add ENABLE lexicon with DAWG and reference hash-set implementations
6e496ea Scaffold headless C# core, tests, QA CLI, and local QA script
```
