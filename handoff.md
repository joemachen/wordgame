# Session Handoff — wordgame

> **For a new Claude session:** read this file first, then `CLAUDE.md` (rules, architecture, commands).
> This file is the "where are we and what's next" brief; `CLAUDE.md` is the "how the code works" reference;
> [`ROADMAP.md`](ROADMAP.md) is the feature design roadmap (what we intend to build, phased).
> **Update this file** (status, decisions, next steps, date) at the end of any meaningful chunk of work.

_Last updated: 2026-10-05 · HEAD `62ad429` (code) · 336 unit tests passing · UI self-test 36/36 passing_

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
| Run | 5 Weeks × (Daily, Saturday Stumper, Sunday Edition boss). Week targets 440/1560/4680/12680/20800; The Strict Grammarian's deadline ×0.75. **Balanced draws** (≥2 vowels, ≥2 consonants, ≤2 of a vowel per refill) from a 98-tile deck with 41 vowels. Bosses tiered by week (Early / Mid / Final, `RunConfig.BossTiers`); endless weeks draw from all bosses. Paycheck economy with interest + overkill bonus. Endless mode. |
| Content | 18 Desk Items (Common/Uncommon/Rare, incl. scaling items), 3 tile enhancements, 6 named Style Guides (Pulp Paperbacks → The Lexicographer's Omnibus), 5 bosses (Ink Spill, Tight Margins, Vowel Drought, Tight Deadline, The Strict Grammarian), shop deck edits (add/enhance/strike), **Stationery** (2 one-shot slots, $3 each, targets: none / hand tiles / board cell): **Answer Key** (best play), **Margin Clip** (+1 submission), **Scissors** (redraw up to 2 hand tiles, no discard spent), **White-Out** (remove a board tile), **Red Ink Bottle** (+3 Mult per play this round). Holding Scissors/White-Out keeps a stuck round alive. |
| Hint | Free Hint shows a *decent* play only (`Hints.Decent`: 90th-percentile play or ≤60% of the best score, whichever is lower; message says "a hint, not the best play"). Best play = Answer Key. Game `--dev` flag restores the best-play Hint. |
| Tooling | Move generator, greedy `RoundSimulator`, whole-run `RunSimulator` (`runsim`, now with submissions-to-win per week/day and a `frac` ScoreFraction skill model) with **`EvaluatingShopBot`** (values purchases by re-scoring recent plays; buys Stationery at a fixed gain per item; `NaiveShopBot` kept for comparison) and **`StationeryBot`** (uses Stationery in simulated rounds), CLI `hint`/`sim`. `tools/Crossword.DefinitionsBuilder` regenerates the embedded definitions from Open English WordNet (+ `supplement.txt`). |
| UI (Godot) | Full playable loop: board, hand (click/type/drag, shuffle, drag-reorder with a ghost slot and tiles sliding apart), live score preview with word definitions, animated scoring, Desk Items bar (reorder/sell; ◀ ▶ tooltips preview the pending play's score after the move, green/red tint) + 2 Stationery slots (use/sell; Scissors use the selected hand tiles, White-Out arms a board-targeting mode, Red Ink shows in the round info), Style Guides popup (Tab / sidebar button: every tier's guide, level, chips × mult, owned + current-play highlights), shop + tile picker, paycheck, win/lose screens. Week progress in the sidebar (pips, this week's three puzzles, puzzles until the boss), A→Z/Z→A sort, NEW tag on drawn tiles, drag pending tiles between squares or back to the hand. **Scoring ring-up** (`Juice.cs`): count-ups, punches, Desk Item card pops with floating deltas, escalation to shake + confetti, "STOP THE PRESSES!" stamp when one play clears the deadline. **Player profile + Stats popup** (`user://profiles/<name>.json`). First-pass visuals (no art or sound yet). |
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
- **Fairer letter mix + targets ×1.3** (user's choices, 2026-10-05): hands felt bad in playtests — measured 29.5% of hands at play time had ≤1 or ≥5 vowels. Chose balanced refills + deck at ~42% vowels + max 2 of a vowel (rejected for now: dropping Q; a "Qu" tile stays a release-hygiene item). That raised the reference player's wins ~43% → ~63%, so targets went ×1.3 → **440/1560/4680/12680/20800** (reference ~36%, rounds ~2.6 plays). Drawing the whole bag yourself is fine — opponents' random draws wouldn't change the odds.
- **Week targets 340/1200/3600/9750/16000 + Strict Grammarian deadline ×0.75** (user's choice, 2026-10-05, option "B · Medium" from a sweep; was 225/800/2400/6500/16000; since ×1.3, see above). Goal: rounds that take more than 1–2 plays for a human, and a difficulty ramp instead of a finale wall. Rejected: gentler ×1.3 (rounds barely longer) and steeper ×1.75 (Week 1 wall for weaker players). **Balance against the ScoreFraction model** (`runsim … frac`, reference skill 0.75) from now on.
- **Boss tiers follow measured difficulty** (user's choice over the original roadmap tiers): easiest bosses early, The Strict Grammarian (hardest) as the Week 5 finale.
- **Focus: a working game first.** Steam/launch work (Steamworks, store page, demo, Next Fest) is off the roadmap for now. Simulator throughput target: ~10k runs in minutes (100k+ not needed).
- **Licensing:** no commercially-restricted deps (e.g. FluentAssertions v8). Word lists: public domain only unless licensed.
- **Definitions come from Open English WordNet** (user's choice; CC BY 4.0 → attribution in `THIRD_PARTY_NOTICES.md`, must also appear in in-game credits before release). Wiktionary was rejected for now (CC BY-SA share-alike). Unknown words show "valid word — no definition on file". Definitions appear in the play preview only (user's choice; not in the scoring log or board tooltips).
- **Style Guides are shown in a Run Info-style popup** (Tab / sidebar button), user's choice over an always-visible sidebar table or desk-bar badges.
- **Stationery batch (user's choice, 2026-10-05):** Margin Clip, Scissors, White-Out, Red Ink Bottle, $3 each except
  **Margin Clip $6** (user's call after it measured +13 pts at $3; per-item prices via `ShopConfig.StationeryPrices`). Red Ink is a round-level `RoundConfig.BonusMult` (scoring step 4b, before Desk Items, so ×Mult items multiply it). White-Out's tile is gone for the round (not returned); leftover fragments are only checked when a later play crosses them. **Deadlock escape** (Claude's call, flagged to the user): holding Scissors/White-Out postpones the "no play, no discards" loss, since otherwise the round would end while the player holds the way out.
- **Playtest batch (user's choices, 2026-10-05):** only *pending* tiles can be moved (submitted tiles stay — White-Out is the way to remove one); Desk Item order is explained by a live score preview on the ◀ ▶ arrows (Stationery isn't reorderable); stats = profile + core stats now, **vocabulary grading later** (needs a licensed word-frequency list). Stats count **every word a play forms** (main + cross). QA flags (`--selftest`, `--screenshot`, `--autoplay`) never write the profile.
- **Hint is not a free solve** (user's choice, 2026-10-05): the free Hint shows a decent play, never the best; the best play is the paid one-shot **Answer Key** Stationery; `--dev` keeps the unlimited best-play hint for development. Chosen over money-cost hints, limited charges, or nudge-only hints.

## 4. Current balance snapshot (after balanced draws + targets ×1.3, 2026-10-05)

Week targets **440/1560/4680/12680/20800**, day multipliers ×1/×1.3/×1.6, The Strict Grammarian's deadline ×0.75,
balanced draws, 41-vowel deck, Margin Clip $6 (the bot buys it).
**Reference player = ScoreFraction model** (`runsim 150 0.75 frac`: picks the best play worth ≤75% of the best one).
Evaluating shop bot (target-sweep harness, 150–200 runs per cell):

| Skill (ScoreFraction) | Victory | Mean subs per won round | Won in 1–2 subs |
|---|---|---|---|
| 0.9 | **59%** | 2.33 | 68% |
| 0.75 (reference) | **36.5%** | 2.57 | 55% |
| 0.6 | **15%** | 2.73 | 43% |

**Letter-mix measurement** (200 paired runs + 600 rounds per arm, ScoreFraction 0.75, old targets): hands at play time
with ≤1 or ≥5 vowels — current 29.5% / deck only 28.7% / balanced only 5.3% / all three 10.0%; hands holding a vowel 3×
5.8% → 0%; best play per hand 275 → 316 (+15%); wins 43.5% → 63%. Balanced refills do almost all the work; the vowel bump
alone changes nothing. **Target sweep with the new draws** (reference skill): ×1.0 63% · ×1.15 48% · ×1.3 36.5% · ×1.45 24%.

**Before the letter-mix change** (old targets 340/…/16000): Victory at 0.9 / 0.75 / 0.6 went 45/31/13% (no Stationery)
→ 61/47/23% (Margin Clip $3) → 56/43/18% (Margin Clip $6).

**Margin Clip price sweep** (scratch harness, 200 paired runs, ScoreFraction 0.75, baseline without Stationery 37.5%):
always offered +13 / +13 / +9.5 / +10 pts at $3 / $5 / $6 / $7; realistic full pool +8.5 / +5.5 / +6 / +4.5. Price is a
weak lever — an extra submission saves runs, so the bot buys it at any of these prices; $5–$7 are within noise (±3.5).

**Per-item Stationery value** (scratch harness: 200 paired runs per arm, ScoreFraction 0.75, the shop offers only that
item and the bot buys it at a fixed gain; baseline without Stationery 37.5% on seeds 1–200):

| Item ($3) | Win-rate change | Uses/run | Notes |
|---|---|---|---|
| **Margin Clip** | **+13 to +14 pts** | ~1.6 | Used on a short last submission. Far the best buy per dollar → now $6 (still +9.5 when always offered). |
| Answer Key | −3 (gain 0.1), −11 (0.3) | ~3 | Used when only the best play wins the round now. |
| Red Ink Bottle | −2.5 (0.1), −10 (0.3) | ~5.6 | Used on boss openers and short last submissions. |
| Scissors | −8 (0.05), −10.5 (0.1) | ~2.7 | Cuts unused Q/Z/X/J tiles before a play. |
| White-Out | −11.5 | 0 | Bot only uses it to escape a stuck hand, which practically never happens. |

  Negative = the $3 (and the slot) would have done more as money/Desk Items for this bot. The bot's Stationery use
  is rule-based, so these are a floor on what a human gets — but Margin Clip is clearly over-tuned (open question).

- Losses are spread across the weeks; Dailies and Saturday Stumpers end most runs. The Strict Grammarian ends 7 (0.9) /
  8 (0.75) of 150 runs — still among the hardest bosses, no longer a wall.
- **Targets plateau at ~2.5 plays per round:** steeper curves (×1.75, ×2) only turn long rounds into lost rounds
  (4 submissions, high per-play variance). 3+ play rounds need a structural change (e.g. 5 submissions + higher targets).
- **The percentile skill model is a poor human proxy** (its 0.9 = the play better than 90% of legal plays, far below the
  best, since most legal plays are tiny). It was the tuning reference before 2026-10-05, which is why rounds felt short.
- Re-run with CLI `runsim 150 0.75 frac` (add `naive` for the old shop bot). Target sweeps: scratch harness pattern in §7.

**Still true from earlier measurements (old targets — re-measure):**
- **Shopping well matters more than word-finding:** the same word skill went 42% → 97% from the shop bot alone.
- Item concentration: Pulitzer, Margin Notes, Word Count in >90% of strong runs; Margin Notes is strong partly because
  the bot only discards when it has no play (+6 Mult).
- Deck edits (enhance/strike/add tile) never pay off: buying them at any fixed estimated gain *lowered* the bot's
  win rate, so `ShopBotConfig` defaults skip them. A money reserve for interest also lowered win rate.
- Bots ignore Stationery, so the Answer Key's value isn't in any simulated number.

**History:** 150/400/900/1900/3800 → 225/800/2400/6500/16000 (tuned against the percentile 0.9 bot: 39% wins, but a
human-like player won 81–87% of rounds in 1–2 submissions and runs died at a 40%+ Grammarian finale) → current.

## 5. Open concerns / known gaps

1. **Overall difficulty after the retune + Stationery** (see §4): the reference player wins ~43%, a weaker one ~18%,
   much of it from Margin Clips. Check in playtests; the Press Run stakes (ROADMAP §5) could carry difficulty.
   **Margin Clip is still the strongest Stationery at $6** (+9.5 pts when always offered, ~+6 in the real pool); if
   it stays too strong, price won't fix it — give it a cost instead (see §6). The other four don't pay for themselves
   for the bot.
2. **Possible dominant items / weak deck edits** (see §4): Pulitzer, Margin Notes, Word Count near-universal picks;
   deck edits not worth buying. The bot values items by the *best* play per decision (strong-shopper view), not
   the play its skill level would pick. Stationery is bought at fixed per-item gains (`ShopBotConfig.StationeryGain`),
   not by re-scoring, and its in-round use is rule-based (`StationeryBot`) — a floor on what a human gets from it.
3. **No save/load.** State is immutable records, so it's mostly serialization (Desk Items are polymorphic records — needs a type discriminator).
4. **UI is first-pass:** no art, sound, or settings; only the hand-reorder slide is animated. Hand drag (ghost slot, sliding tiles, lifted preview) confirmed good by the user with a real mouse.
5. **Content hygiene for release:** ENABLE contains slurs — needs a denylist before shipping (the same pass should cover definitions; WordNet glosses include crude senses). "Q without U" is a dead tile (consider a "Qu" tile).
6. **Hand arrangement is UI-only** (not saved); fine until save/load exists.
7. **~38% of ENABLE has no definition** (mostly obscure words, e.g. GLEY, the user's own example; 2–5-letter words ~73% covered, every 2-letter word covered). Options if it matters: extend `supplement.txt` for words that come up often, or add a second source after a license check (Wiktionary is CC BY-SA).
8. **The CLI has no Stationery commands** (dev `give` only covers Desk Items); the game UI is the only way to use it.
9. **Player profile is one file per name, no picker yet** (`--profile=name`); stats aren't shown in the CLI. Vocabulary grading not started.

## 6. Suggested next steps (offered to the user; they haven't picked yet)

Longer-term phases live in `ROADMAP.md` §11 (phase 0 retune ✅ → phase 1 naming pass ✅ → new items/bosses → Stationery →
save/load + meta → decks/dictionaries/stakes), plus parallel tracks (CI, seed entry, Daily Editorial, presentation, onboarding). §6–§10 cover infra, modes, presentation, persistence and suggested additions.

0. **Playtest the fairer hands + ×1.3 targets:** do hands feel playable now, and is the difficulty right? Also: does the ring-up escalate nicely (thresholds in `Juice.cs`: Big ≥25% / Huge ≥60% of the deadline; the stamp fires whenever one play clears the deadline, which is common in Week 1)? Is the week progress clear? Next for stats: vocabulary grading (find + license-check a frequency list), a profile picker, more fun stats.
1. **Stationery balance:** Margin Clip raised to $6 (done). If it still dominates in playtests, give it a cost
   (e.g. −1 discard) or make it rarer — price alone stops working above ~$5. The others may need buffs (e.g. Red Ink +5,
   Answer Key $2) — check in playtests, since the bot's use is rule-based. Re-measure with the scratch harness (§7).
2. **Playtest the retune, new Hint and Stationery** via `run_local_qa.bat` — do rounds feel longer, is difficulty right
   (concern #1), is the free Hint useful without being a crutch, do Scissors/White-Out feel worth $3? If 3+ play rounds
   are wanted, try 5 submissions per round with higher targets (targets alone plateau at ~2.5 plays).
3. ROADMAP phase 2: new Desk Items + Redundant Copy (Mid) and The Puzzle Master (Final) bosses. Remaining Stationery
   (Highlighter, Fountain Pen, Correction Tape) can come later on the same targeting system.
4. Balance pass on outliers: re-measure item pick rates at the new targets; Pulitzer / Margin Notes / Word Count;
   make deck edits worth buying.
5. Save/load (resume a run) — prerequisite for meta-progression.
6. UI polish: tile placement/score animations, sound, juice; deck viewer; tooltips for Desk Items/bosses.

## 7. How to work in this repo (practical tips learned the hard way)

- **Verify, don't assume.** After changes: `dotnet build wordgame.sln` (warnings are errors) → `dotnet test`. For UI changes also run the screenshot and self-test flags:
  - `"D:/Projects/Godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe" --path game -- --seed=42 --screenshot=<scratchpad>/shot.png` then view the PNG. Extra flags: `--give=red-pen,pulitzer,answer-key --autoplay=3 --hint --dev` (`--give` takes Desk Item or Stationery ids; `--dev` = best-play Hint button).
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
- Stationery value: same harness pattern, one arm per item with `ShopConfig.StationeryIds = {id}` (the shop always
  offers it) and `ShopBotConfig.StationeryGain = {id: g}`, paired seeds against a no-Stationery baseline. A full
  5-item × 2-gain sweep at 200 runs/arm takes ~18 min on 20 cores.
- Balance experiments: a throwaway console project in the scratchpad referencing `src/Crossword.Core` (loop over configs, call `RunSimulator.PlayRun(seed, config, lexicon, skill, strategy, botConfig, model: SkillModel.ScoreFraction)` with `.AsParallel()`, build `-c Release`) is faster than editing defaults repeatedly. 100 runs ≈ 1 min with the evaluating bot. Note `RunConfig.Days` multipliers must be set explicitly in such harnesses. n=60 runs is too noisy (±6 pts) to compare close variants; use 150+.
- The user's machine has old Godot crash dumps; the project uses the **GL Compatibility** renderer, which has been stable.

## 8. Working with the user

- Wants Claude to **flag unsound decisions or anything against the spirit of the game** — give a recommendation, not just options.
- Prefers seeing things in the **real game UI** (disliked the console). Keep `run_local_qa.bat` double-clickable and working.
- Practice so far: work in focused commits on `main` with descriptive messages and push to `origin` after each verified chunk; the user has been fine with this.
- Starts new chats periodically to keep context small → **keep this file current**.

## 9. Commit history (newest first)

```
62ad429 Track player stats in a saved profile and show them in a Stats popup
b865f83 Make the scoring ring-up escalate and pop its sources
07cf39a Preview Desk Item reordering on the move arrows
2fa26a7 Show week progress, sort the hand, mark new tiles, drag pending tiles
f590c2e Raise Margin Clip to $6 with per-item Stationery prices
3ef83ac Teach the run simulator to buy and use Stationery
49ade8b Use the new Stationery in the game UI
cf2fb05 Add Margin Clip, Scissors, White-Out and Red Ink Bottle Stationery
cf7ab5a Bring handoff balance snapshot, concerns and next steps up to date
7fec654 Record retune commit in handoff
e928808 Retune week targets and soften The Strict Grammarian's deadline
c4fd76d Record hint rework and round-length measurements in handoff
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
