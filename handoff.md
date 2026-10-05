# Session Handoff — wordgame

> **For a new Claude session:** read this file first, then `CLAUDE.md` (rules, architecture, commands).
> This file is the "where are we and what's next" brief; `CLAUDE.md` is the "how the code works" reference;
> [`ROADMAP.md`](ROADMAP.md) is the feature design roadmap (what we intend to build, phased).
> **Update this file** (status, decisions, next steps, date) at the end of any meaningful chunk of work.

_Last updated: 2026-10-05 · HEAD (see §9) · 294 unit tests passing · UI self-test 21/21 passing_

---

## 1. What this is

A **Balatro-style crossword roguelike deckbuilder** (working title "wordgame"), commercial indie project.
Players build words on a persistent crossword grid each round; scoring is Balatro's Chips × Mult fused with
Scrabble geometry. C# / .NET 8. Headless rules engine (`src/Crossword.Core`) + **Godot 4.7 .NET UI** (`game/`)
+ a developer text console (`src/Crossword.Cli`). Repo: https://github.com/joemachen/wordgame (branch `main`).

## 2. Current status (everything below is built, tested, and pushed)

| Area | State |
|---|---|
| Lexicon | ENABLE (public domain), embedded; DAWG (~1 MB, 0.5 s build); words 2–15 letters. **QI/ZA are not valid** (not in ENABLE). **Definitions** (embedded, ~1.7 MB gz): Open English WordNet 2025 + hand-written supplement for function words and every 2-letter word; ~62% of ENABLE covered (incl. inflections → lemma), shown in the play preview and CLI `check`. |
| Board & rules | 7×7 persistent grid per round, premium squares (seeded, symmetric), black squares, placement validation, cross words, deadlock detection. |
| Scoring | Pooled Chips × Mult per play: tier (longest word) → word chips (DL/TL/DW/TW, new tiles only) → tile enhancements → intersections (+3 Mult each) → Desk Items (slot order). |
| Run | 5 Weeks × (Daily, Saturday Stumper, Sunday Edition boss). Week targets 340/1200/3600/9750/16000; The Strict Grammarian's deadline ×0.75. Bosses tiered by week (Early / Mid / Final, `RunConfig.BossTiers`); endless weeks draw from all bosses. Paycheck economy with interest + overkill bonus. Endless mode. |
| Content | 18 Desk Items (Common/Uncommon/Rare, incl. scaling items), 3 tile enhancements, 6 named Style Guides (Pulp Paperbacks → The Lexicographer's Omnibus), 5 bosses (Ink Spill, Tight Margins, Vowel Drought, Tight Deadline, The Strict Grammarian), shop deck edits (add/enhance/strike), **Stationery** system (2 one-shot slots) with its first item, the **Answer Key** (reveals the best play, $3). |
| Hint | Free Hint shows a *decent* play only (`Hints.Decent`: 90th-percentile play or ≤60% of the best score, whichever is lower; message says "a hint, not the best play"). Best play = Answer Key. Game `--dev` flag restores the best-play Hint. |
| Tooling | Move generator, greedy `RoundSimulator`, whole-run `RunSimulator` (`runsim`, now with submissions-to-win per week/day and a `frac` ScoreFraction skill model) with **`EvaluatingShopBot`** (values purchases by re-scoring recent plays; `NaiveShopBot` kept for comparison), CLI `hint`/`sim`. `tools/Crossword.DefinitionsBuilder` regenerates the embedded definitions from Open English WordNet (+ `supplement.txt`). |
| UI (Godot) | Full playable loop: board, hand (click/type/drag, shuffle, drag-reorder with a ghost slot and tiles sliding apart), live score preview with word definitions, animated scoring, Desk Items bar (reorder/sell), Style Guides popup (Tab / sidebar button: every tier's guide, level, chips × mult, owned + current-play highlights), shop + tile picker, paycheck, win/lose screens. First-pass visuals (no art, sound, or tile animations yet). |
| QA | `run_local_qa.bat` (double-click): build → tests → opens game window. `--cli` for console. |

## 3. Decisions already made (don't re-litigate without the user)

- **Engine:** Godot 4 (C#). Core targets net8.0, BCL only. Godot 4.7.1 .NET installed at `D:\Projects\Godot\Godot_v4.7.1-stable_mono_win64\` (path lives in gitignored `qa.local.bat`).
- **Desk Items are pure functions in an ordered pipeline**, not `IObserver` events. Scaling items evolve via `AfterPlay`/`AfterRoundWon` returning updated copies.
- **Determinism:** all Core randomness via immutable `Rng`. Hand *display order* is cosmetic (UI-level, own RNG stream) — never affects game state.
- **Scoring is pooled** (one Chips × Mult per play), chosen over per-word sums. Longest word sets the tier; all words add chips.
- **2-letter words are legal** (lowest tier). **Discards exist** (3/round). **Premiums only count under newly placed tiles**; **enhancements trigger per formed word, including old tiles** (deliberate: rewards building onto the grid).
- **Economy:** Balatro-style payouts **plus capped overkill bonus** (user's choice). **Run length:** 5 Weeks × 3 rounds (user's choice). **Newspaper theme** (Week / Daily / Saturday Stumper / Sunday Edition, Desk Items, deadlines).
- **Balance is data-driven:** all numbers live in config records (`ScoringConfig`, `RunConfig`, `ShopConfig`, `EconomyConfig`, Desk Item constructor defaults) and are tuned with simulations, not by hand. Unit tests pin their own numbers so retuning never breaks them.
- **Roadmap decisions (ROADMAP.md):** new boss names *merge* with existing bosses (renames + additions; "Saturday Stumper" boss → "The Puzzle Master", "Tight Deadline" stake → "Rush Job"); every roadmap entry carries a *proposed* effect + status; "Scrabble Board" → "Tile Rack" and the Trademarks dictionary is parked pending legal review; **dictionaries are per-run choices with tradeoffs**, not permanent global unlocks.
- **Week targets 340/1200/3600/9750/16000 + Strict Grammarian deadline ×0.75** (user's choice, 2026-10-05, option "B · Medium" from a sweep; was 225/800/2400/6500/16000). Goal: rounds that take more than 1–2 plays for a human, and a difficulty ramp instead of a finale wall. Rejected: gentler ×1.3 (rounds barely longer) and steeper ×1.75 (Week 1 wall for weaker players). **Balance against the ScoreFraction model** (`runsim … frac`, reference skill 0.75) from now on.
- **Boss tiers follow measured difficulty** (user's choice over the original roadmap tiers): easiest bosses early, The Strict Grammarian (hardest) as the Week 5 finale.
- **Focus: a working game first.** Steam/launch work (Steamworks, store page, demo, Next Fest) is off the roadmap for now. Simulator throughput target: ~10k runs in minutes (100k+ not needed).
- **Licensing:** no commercially-restricted deps (e.g. FluentAssertions v8). Word lists: public domain only unless licensed.
- **Definitions come from Open English WordNet** (user's choice; CC BY 4.0 → attribution in `THIRD_PARTY_NOTICES.md`, must also appear in in-game credits before release). Wiktionary was rejected for now (CC BY-SA share-alike). Unknown words show "valid word — no definition on file". Definitions appear in the play preview only (user's choice; not in the scoring log or board tooltips).
- **Style Guides are shown in a Run Info-style popup** (Tab / sidebar button), user's choice over an always-visible sidebar table or desk-bar badges.
- **Hint is not a free solve** (user's choice, 2026-10-05): the free Hint shows a decent play, never the best; the best play is the paid one-shot **Answer Key** Stationery; `--dev` keeps the unlimited best-play hint for development. Chosen over money-cost hints, limited charges, or nudge-only hints.

## 4. Current balance snapshot (`RunSimulator`, 300 runs at 0.9, 150 at 0.8/0.7; after phase 1 boss tiers)

Week targets **225/800/2400/6500/16000** (retuned 2026-10-05 against the evaluating bot; was 150/400/900/1900/3800),
day multipliers ×1/×1.3/×1.6.

| Shop bot | skill 0.9 | 0.8 | 0.7 |
|---|---|---|---|
| Evaluating (default) | **39%** | 14% | 2% |
| Naive | 2% | — | — |

- **Bosses are tiered by week** (phase 1): Early Ink Spill / Tight Margins, Mid Vowel Drought / Tight Deadline,
  Final The Strict Grammarian. Overall win rates barely moved versus random bosses (39/14/3 → 39/14/2).
- Losses at skill 0.9 by week: 10 / 23 / 33 / 43 / 75 (of 300 runs). Boss loss rate per encounter at 0.9:
  **The Strict Grammarian 29% as the Week 5 finale** (was ~12% when it could appear in any week), Tight Deadline 9%,
  Vowel Drought 4%, Ink Spill 2%, Tight Margins 2%. The finale is now a real wall: ~1 in 4 losses happen there.
- **Skill gap is now wide** (0.9→0.8 ≈ 25 pts), so the old "narrow gap" concern was a bot/target artifact.
- **Early weeks are punishing for weaker players:** skill 0.7 loses in Week 1 ~60% of runs (89 of 150 after phase 1). The user chose this over a
  softer early curve (e.g. 200/800/…: 45% / 17% / 5%). Revisit after human playtests.
- **Shopping well matters more than word-finding:** with the *old* targets the same word skill went 42% → 97% from
  the shop bot alone.
- Item concentration (measured on the *old* targets, re-measure): Pulitzer, Margin Notes, Word Count in >90% of
  skill-0.9 runs; Margin Notes is strong partly because the bot only discards when it has no play (+6 Mult).
- Deck edits (enhance/strike/add tile) never pay off: buying them at any fixed estimated gain *lowered* the bot's
  win rate, so `ShopBotConfig` defaults skip them. A money reserve for interest also lowered win rate.
- Re-run with CLI `runsim 100 0.9` (add `naive` for the old bot, `frac` for the score-fraction model) or the scratch harness pattern in §7.

**Round length (2026-10-05, 150 runs each, evaluating bot)** — prompted by the user's "I only ever need 1–2 words a round":

| Skill | Victory | Won in 1–2 subs | Mean subs to win, W1 → W5 (Daily) |
|---|---|---|---|
| 1.0 (= old Hint, best play) | 77% | 93% | 1.57 → 1.79 |
| ScoreFraction 0.9 | 54% | 87% | 1.84 → 2.10 |
| ScoreFraction 0.75 | 35% | 81% | 1.93 → 2.50 |
| ScoreFraction 0.6 | 19% | 71% | 2.28 → 2.59 |
| Percentile 0.9 (tuning reference) | 39% | 61% | 2.85 → 2.64 |

- **The percentile bot is a poor human proxy.** Its 90th-percentile play is far below the best (most legal plays are
  tiny), so targets tuned for it are soft for a human who finds *good* plays. Human-like play (ScoreFraction) clears
  most rounds in 1–2 submissions, matching the user's experience.
- **Difficulty is a cliff, not a curve:** even ScoreFraction 0.6 wins 71% of rounds in 1–2 submissions, yet only 19% of
  runs — losses pile up at The Strict Grammarian (38–41 of 150 runs for every fraction model) and Saturday Stumpers.
- **Retuned (option B):** week targets 340/1200/3600/9750/16000, Grammarian deadline ×0.75. Verified with `runsim 150 … frac`:
  wins 45% / 31% at 0.9 / 0.75; rounds ~2.1–2.6 submissions; 65–74% of rounds won in 1–2; losses spread across the
  weeks (Stumpers and Dailies now kill most runs; Grammarian 13 / 8 of 150). Sweep harness: scratch console project
  crossing curves × Grammarian scale (a `ScaledBoss` wrapper) × skills — see §7.
- Sweep findings: targets plateau at ~2.5 plays per round (steeper curves just add losses); scaling the Grammarian's
  deadline to 0.75 cut its finale loss rate from ~45% to ~15%. The percentile tables above are pre-retune.

## 5. Open concerns / known gaps

1. **Harsh early game for average players** (see §4). Decide after playtests; the Press Run stakes (ROADMAP §5)
   could carry difficulty instead if the base game should be gentler.
2. **Possible dominant items / weak deck edits** (see §4): Pulitzer, Margin Notes, Word Count near-universal picks;
   deck edits not worth buying. The bot values items by the *best* play per decision (strong-shopper view), not
   the play its skill level would pick.
3. **No save/load.** State is immutable records, so it's mostly serialization (Desk Items are polymorphic records — needs a type discriminator).
4. **UI is first-pass:** no art, sound, or settings; only the hand-reorder slide is animated. Hand drag (ghost slot, sliding tiles, lifted preview) confirmed good by the user with a real mouse.
5. **Content hygiene for release:** ENABLE contains slurs — needs a denylist before shipping (the same pass should cover definitions; WordNet glosses include crude senses). "Q without U" is a dead tile (consider a "Qu" tile).
6. **Hand arrangement is UI-only** (not saved); fine until save/load exists.
7. **~38% of ENABLE has no definition** (mostly obscure words, e.g. GLEY, the user's own example; 2–5-letter words ~73% covered, every 2-letter word covered). Options if it matters: extend `supplement.txt` for words that come up often, or add a second source after a license check (Wiktionary is CC BY-SA).

## 6. Suggested next steps (offered to the user; they haven't picked yet)

Longer-term phases live in `ROADMAP.md` §11 (phase 0 retune ✅ → phase 1 naming pass ✅ → new items/bosses → Stationery →
save/load + meta → decks/dictionaries/stakes), plus parallel tracks (CI, seed entry, Daily Editorial, presentation, onboarding). §6–§10 cover infra, modes, presentation, persistence and suggested additions.

0. **Playtest the retune + new Hint** — do rounds feel longer? If 3+ play rounds are wanted, try 5 submissions per
   round with higher targets (structural; targets alone plateau at ~2.5 plays).
1. User playtests a few runs via `run_local_qa.bat` → check early-game feel (concern #1) and whether a 29%
   Strict Grammarian finale feels fair.
2. ROADMAP phase 2: new Desk Items + Redundant Copy (Mid) and The Puzzle Master (Final) bosses.
3. Balance pass on outliers: re-measure item pick rates at the new targets; Pulitzer / Margin Notes / Word Count;
   make deck edits worth buying.
4. Save/load (resume a run) — prerequisite for meta-progression.
5. UI polish: tile placement/score animations, sound, juice; deck viewer; tooltips for Desk Items/bosses.

## 7. How to work in this repo (practical tips learned the hard way)

- **Verify, don't assume.** After changes: `dotnet build wordgame.sln` (warnings are errors) → `dotnet test`. For UI changes also run the screenshot and self-test flags:
  - `"D:/Projects/Godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe" --path game -- --seed=42 --screenshot=<scratchpad>/shot.png` then view the PNG. Extra flags: `--give=red-pen,pulitzer --autoplay=3 --hint`.
  - `... --path game -- --seed=42 --selftest` → PASS/FAIL lines, exit 1 on failure. Extend `game/Scripts/Main.SelfTest.cs` for new interactions.
  - Build the Godot project (`dotnet build game/Wordgame.Godot.csproj`) before launching Godot; it loads assemblies from `game/.godot/mono/temp/bin`.
- **Desktop control (computer-use) can't target the portable Godot exe** — use `--selftest`/`--screenshot` instead.
- **Simulated input quirk:** under `Viewport.PushInput`, `_DropData`'s `atPosition` arrives in the wrong coordinate space. Don't base UI logic on it: hand reorder tracks the cursor in `Main._Input` (canvas coords for real and pushed input, matching `GetGlobalRect()`) and drops into the ghost's slot.
- **Mid-interaction screenshots:** `--screenshot` quits before any input; to see a drag mid-flight, temporarily add `GetViewport().GetTexture().GetImage().SavePng(...)` inside a self-test step.
- **Editing gotchas:** the Write/Edit tools turn `\uXXXX` escapes into literal characters, and bash heredocs can mangle `\n`. For multi-file edits, write a Python script with raw strings (`r"""..."""`) to the scratchpad and run it; for C# char literals prefer `(char)0xFEFF` style.
- **Python on this machine prints with cp1252:** printing non-ASCII (e.g. ✅, →) from a script raises `UnicodeEncodeError` — write to files instead or set `PYTHONIOENCODING=utf-8`. Long bash heredocs containing quotes/apostrophes can also fail to parse; use the Write tool for data files.
- **Definitions data:** edit `tools/Crossword.DefinitionsBuilder/supplement.txt` (`WORD | pos | gloss`, replaces WordNet for that word), then `dotnet run -c Release --project tools/Crossword.DefinitionsBuilder` (needs `english-wordnet-2025.xml.gz` in gitignored `tools/data/`; download it from the OEWN GitHub 2025-edition release if missing). It prints coverage and warns about supplement words not in ENABLE.
- **Batch files must be CRLF** (`.gitattributes` enforces; normalize with `sed -i 's/\r*$/\r/'` after writing).
- Running `run_local_qa.bat` from a captured shell hangs because Godot inherits the pipe — expected; it's fine on double-click.
- Target sweeps: wrap a boss in a harness-side `BossModifier` subclass whose `ModifyRound` calls `Inner.Apply(config) with { Boss = null }` and rescales `TargetScore` — lets you test boss deadlines without touching Core. Flatten configs × seeds into one `.AsParallel()` query (20 cores: ~0.5 s per run).
- Balance experiments: a throwaway console project in the scratchpad referencing `src/Crossword.Core` (loop over configs, call `RunSimulator.PlayRun(seed, config, lexicon, skill, strategy, botConfig)` with `.AsParallel()`, build `-c Release`) is faster than editing defaults repeatedly. 100 runs ≈ 1 min with the evaluating bot. Note `RunConfig.Days` multipliers must be set explicitly in such harnesses. n=60 runs is too noisy (±6 pts) to compare close variants; use 150+.
- The user's machine has old Godot crash dumps; the project uses the **GL Compatibility** renderer, which has been stable.

## 8. Working with the user

- Wants Claude to **flag unsound decisions or anything against the spirit of the game** — give a recommendation, not just options.
- Prefers seeing things in the **real game UI** (disliked the console). Keep `run_local_qa.bat` double-clickable and working.
- Practice so far: work in focused commits on `main` with descriptive messages and push to `origin` after each verified chunk; the user has been fine with this.
- Starts new chats periodically to keep context small → **keep this file current**.

## 9. Commit history (newest first)

```
97ab47a Make the free hint a decent play and sell the best play as an Answer Key
244d9e1 Measure submissions to win and add a score-fraction skill model
535d7ae Bring handoff up to date: drag confirmed, definitions gap, builder tips
e84ee88 Record Style Guides popup commit in handoff
cdcf32a Add a Style Guides popup listing every word tier and its guide
6dbf5be Record definitions commit in handoff
ebe236e Show word definitions in the play preview
d6a99cd Record hand drag ghost commit in handoff
b469770 Show a ghost slot and slide tiles apart when dragging in the hand
458a1bf Bring handoff status and decisions up to date after phase 1
f7b61a7 Record phase 1 commit hash in handoff
846dfc2 Rename bosses, name Style Guides, and tier bosses by week
1dbb5f3 Trim roadmap to a working game first
fded71c Fold infrastructure, modes, presentation, persistence and Steam into the roadmap
24041f3 Update handoff with retuned targets and balance snapshot
7c9f4ad Retune week targets against the evaluating shop bot
d1f42bc Add ROADMAP.md feature design roadmap
1737088 Update handoff with evaluating shop bot results and next steps
51f5925 Add evaluating shop bot to the run simulator
cb0b6f0 Add handoff.md session brief and point CLAUDE.md at it
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
