# Session Handoff — wordgame

> **For a new Claude session:** read this file first, then `CLAUDE.md` (rules, architecture, commands).
> This file is the "where are we and what's next" brief; `CLAUDE.md` is the "how the code works" reference;
> [`ROADMAP.md`](ROADMAP.md) is the feature design roadmap (what we intend to build, phased).
> **Update this file** (status, decisions, next steps, date) at the end of any meaningful chunk of work.

_Last updated: 2026-10-10 · HEAD `5061d03` (code) · 507 unit tests passing · UI self-test 80/80 passing · CI green_

## 0. ▶ Queued for the next session — the user will say "go"

The user approved everything from 2026-10-10 (title menu, profiles, Settings; **fullscreen tested by hand and
approved**). The dev-only Best button (B) is built (see §3); the economy re-examination (A) is next:

### A. Re-examine the economy ("I never feel like I have enough money")
The user says money feels very hard to make in playtests, and suspects it's partly their own skill. **Measure first,
then bring options with a recommendation to the user before changing any numbers** (economy and difficulty are the
user's calls; see §3).
- **Current economy** (`EconomyConfig` in `Core/Run/RunConfig.cs`, payout in `Core/Run/Economy.cs`): start $4;
  base pay Daily $3 / Saturday $4 / Sunday $5 (`RoundKind.BasePay`); +$1 per unused submission; overkill $1 per full
  25% over the target (cap $3); interest $1 per $4 held (cap $5). Other income: Gilded tiles (+$1 per word),
  Syndication Desk Item ($1 per intersection), selling (half price). Prices: Desk Items $4 / $6 / $8, Style Guide $3,
  Stationery $3 (Margin Clip $6), wild tile $6, reroll $5 (+$1 each).
- **Last measurement** (§4c, reference bot at ScoreFraction 0.75): $6.57 income per round (base $3.71, unused
  submissions $1.33, overkill $1.09, interest $0.38), about $9.8 held entering a shop.
- **Hypothesis to test:** a weaker player loses most of the skill-based income (unused submissions, overkill), so their
  paycheck is close to base pay alone (~$3–5), less than one Common Desk Item per shop.
- **Step 1, measure income by skill.** Recreate the `econsim` scratch harness (§7: `RunSimulator.PlayRun` over paired
  seeds, read `SimulatedRunRound.Payout` / `InRoundMoney` / `Shop`). Cover ScoreFraction 0.5 / 0.6 / 0.75 / 0.9 and
  report: income per round by source, money held entering each shop, how often the player can afford ≥1 Desk Item,
  and spending by category, all by week. Also measure what fraction of shop visits end with nothing affordable.
- **Step 2, candidate levers.** Measure each against the baseline on the same 200 seeds, for win % at 0.6 and 0.75
  and for income:
  - +$1 base pay. In 2026-10-05 this measured +14.5 pts at 0.75, so it's a big lever.
  - Unused discards pay $1. This rewards a cautious player, not just a strong one.
  - A minimum paycheck floor.
  - Cheaper Commons ($3).
  - Reroll base $3 (previously measured +1.5 pts).
  - Interest per $3.
  - A boss-win bonus.
  - New money sources: a "freelance check" Stationery, or more money Desk Items.

  **Prefer levers that help weaker players more than strong ones** (flat or base income over overkill), which matches
  the user's complaint.
- **Step 3, retune.** More income raises win rates, so retune `WeekTargets` to keep the reference (0.75) near
  37–39%. Alternatively, ask the user whether the game should simply get easier. The Press Run ladder (§4a) and decks
  (§4) shift with it, so re-check the deck table.
- Also consider **clarity**: the paycheck screen (`Main.Shop.cs` `BuildPaycheck`) could say how to earn more (e.g.
  "+$1 for every submission you don't use"). Players may not know where money comes from.
- Deliverables: a measurement table for the user, then the chosen change with tests (pin the new numbers in tests'
  own configs), a `runsim` re-check, and doc updates (§2, §3, §4c, CLAUDE.md Economy line).

### B. ~~Dev-only "Best" button~~ ✅ 2026-10-10
Built as planned: `--dev` no longer changes Hint; it shows a separate **Best (dev)** button (`Main.Round.cs`,
`_bestButton`, created always, `Visible = _devMode`) that calls `ShowHint(best: true)`. Self-test step 6b covers it
(80 checks now). **Note for the user:** "best" = the highest-*scoring* play (`Hints.Best`, the top of
`MoveRanker.Rank` with the run's Desk Items, boss, theme and censored letter, cross words included), not the
longest word — say so if you meant the longest word.

---

## 1. What this is

A **Balatro-style crossword roguelike deckbuilder** (working title "wordgame"), commercial indie project.
Players build words on a persistent crossword grid each round; scoring is Balatro's Chips × Mult fused with
Scrabble geometry. C# / .NET 8. Headless rules engine (`src/Crossword.Core`) + **Godot 4.7 .NET UI** (`game/`)
+ a developer text console (`src/Crossword.Cli`). Repo: https://github.com/joemachen/wordgame (branch `main`).

## 2. Current status (everything below is built, tested, and pushed)

| Area | State |
|---|---|
| Lexicon | ENABLE (public domain), embedded; DAWG (~1 MB, 0.5 s build); words 2–15 letters. **QI/ZA are not valid** (not in ENABLE). **Slur denylist** (`Core/Lexicon/Denylist.cs` + `Lexicon/Data/denylist.txt`, 128 words incl. inflections; slurs only, profanity stays): denied words are never legal, suggested or defined (168,423 playable words). **Definitions** (embedded, ~1.7 MB gz): Open English WordNet 2025 + hand-written supplement for function words and every 2-letter word; ~62% of ENABLE covered (incl. inflections → lemma), shown in the play preview and CLI `check`. Slur senses and crude glosses are hidden (next clean sense shown: TACO → the food, CHINK → a narrow opening). **Dictionary overlays** (`Core/Lexicon/Dictionaries.cs`): a run's `RunState.Dictionaries` adds words on top of ENABLE (`LexiconLoader.For(ids)`, cached merged DAWG). First one: **The Tech Shorthand** (297 hand-written acronyms/initialisms of 3+ letters with expansions, `Lexicon/Data/tech-shorthand.tsv`; expansions show as "abbr." definitions). Second: **The Atlas Unlocked** (`atlas`, 567 hand-written single-word place names of 3–7 letters, `Lexicon/Data/atlas.tsv`; shown as "n. capital of Norway"; each dictionary has a `SenseLabel`). Third: **The Olde English Folio** (`olde-folio`), a **theme dictionary**: +3 Mult per archaic word a play forms (106 words: THEE, HATH, ERE, YON…; scoring step 4b `theme`, `ScoringConfig.Theme` set by `RunRules.ConfigFor`), plus 5 new words (OER, NEER, EER, EEN, OLDE). |
| Board & rules | 7×7 persistent grid per round, premium squares (seeded, symmetric), black squares, placement validation, cross words, deadlock detection. |
| Scoring | Pooled Chips × Mult per play: tier (longest word) → word chips (DL/TL/DW/TW, new tiles only) → tile enhancements → intersections (+3 Mult each) → Desk Items (slot order). |
| Run | 5 Weeks × (Daily, Saturday Stumper, Sunday Edition boss). Week targets 510/1790/5380/14580/23920; The Strict Grammarian's deadline ×0.75. **Balanced draws** (≥2 vowels, ≥2 consonants, ≤2 of a vowel per refill) from a 100-tile deck: 98 lettered (41 vowels) + 2 **wild tiles**. Bosses tiered by week (Early / Mid / Final, `RunConfig.BossTiers`; Mid adds Redundant Copy, Final adds The Puzzle Master); endless weeks draw from all bosses. Paycheck economy: base + $1/unused submission + overkill ($1 per 25% over, cap $3) + interest ($1 per $4 held, cap $5). Endless mode. |
| Content | 23 Desk Items (Common/Uncommon/Rare, incl. scaling items; Phase 2 added **Etymology Tome**, **Rubber Stamp**, **Printing Press Roller**, **Tile Rack**, **Coffee Stain** — the last two via the round-start hook `IDeskItem.ModifyRound`), 3 tile enhancements, 6 named Style Guides (Pulp Paperbacks → The Lexicographer's Omnibus), 7 bosses (Ink Spill, Tight Margins, Vowel Drought, Tight Deadline, **Redundant Copy**, The Strict Grammarian, **The Puzzle Master** = two Early/Mid bosses at once), shop deck edits (add/enhance/strike), **Stationery** (2 one-shot slots, $3 each except Margin Clip $6, targets: none / hand tiles / board cell): **Answer Key** (best play), **Margin Clip** (+1 submission), **Scissors** (redraw up to 2 hand tiles, no discard spent), **White-Out** (remove a board tile), **Red Ink Bottle** (+3 Mult per play this round), **Fountain Pen** (a hand tile turns wild this round). **Wild tiles**: any letter (picked when placed), 0 letter chips; 2 in the starting deck, shop wild tile ($6) and "make a tile wild" edit ($5). Holding Scissors/White-Out/Fountain Pen keeps a stuck round alive. Lifetime **player stats** in a saved profile (words by length, newest words, runs/wins, best play, intersections, close calls, bosses beaten, full-spread rounds). |
| Hint | Free Hint shows a *decent* play only (`Hints.Decent`: 90th-percentile play or ≤60% of the best score, whichever is lower; message says "a hint, not the best play"). Best play = Answer Key. Game `--dev` adds a **Best (dev)** button next to Hint that places the best play (Hint stays decent). |
| Tooling | Move generator, greedy `RoundSimulator`, whole-run `RunSimulator` (`runsim`, now with submissions-to-win per week/day and a `frac` ScoreFraction skill model) with **`EvaluatingShopBot`** (values purchases by re-scoring recent plays; buys Stationery at a fixed gain per item; `NaiveShopBot` kept for comparison) and **`StationeryBot`** (uses Stationery in simulated rounds, incl. Fountain Pen on dead Q/Z/X/J), CLI `hint`/`sim`, plus Stationery (`use <slot> [LETTERS|cell]`, `sellst`, `give`) and `save`/`load` (game save format). The bot *can* buy wild tiles/edits and the Fountain Pen (`ShopBotConfig.WildTileGain`/`WildEditGain`/`StationeryGain`) but defaults are 0 (measured no gain). `SimulatedRunRound` records each round's paycheck breakdown, in-round money and shop spending by category (`ShopSpend`). Clue engine in Core (`Clues/`: `BoardWords`, `MarginClues`, `NewsroomClues`). `tools/Crossword.DefinitionsBuilder` regenerates the embedded definitions from Open English WordNet (+ `supplement.txt`). |
| UI (Godot) | Full playable loop: board, hand (click/type/drag, shuffle, drag-reorder with a ghost slot and tiles sliding apart), live score preview with word definitions, animated scoring, Desk Items bar (reorder/sell; ◀ ▶ tooltips preview the pending play's score after the move, green/red tint) + 2 Stationery slots (use/sell; Scissors use the selected hand tiles, White-Out arms a board-targeting mode, Red Ink shows in the round info), Style Guides popup (Tab / sidebar button: every tier's guide, level, chips × mult, owned + current-play highlights), shop + tile picker, paycheck, win/lose screens. **Title menu** on launch (Continue — Enter — / New run / Profile / Stats / Settings / Quit; sidebar Menu button reopens it), **profiles** (list, switch, create; each keeps its stats, unlocks and run; last one remembered), **Settings** (reduced motion, fullscreen, text size 90–120%; `user://settings.cfg`), New-run picker with a **seed box**. Week progress in the sidebar (pips, this week's three puzzles, puzzles until the boss), A→Z/Z→A sort, NEW tag on drawn tiles (fades after 3 s or on first touch of the hand), drag pending tiles between squares or back to the hand. **Wild tiles** show as "?" in hand; placing one opens a letter picker (click or type). ACROSS/DOWN **clue columns** are built but hidden (`Main.ShowClueColumns`). **Scoring ring-up** (`Juice.cs`): count-ups, punches, Desk Item card pops with floating deltas, escalation to shake + confetti, "STOP THE PRESSES!" stamp when one play clears the deadline. **Player profile + Stats popup** (`user://profiles/<name>.json`). First-pass visuals (no art or sound yet). |
| Save & resume | The run is saved to `user://saves/<profile>.json` after every session change and on window close, and offered by the title menu's **Continue** on launch ("Resumed your run: Week N, …"). `Core/Save/RunSaveJson` stores the whole `GameSession` except `RunConfig` (+ the hand arrangement); a lost run deletes the save, a won run keeps it (endless choice); a corrupt or other-version save is moved to `.bak` with a notice. The title's New run needs a second click while a run is in progress. `--seed` replaces the save; QA flags and `--give`/`--week` never touch it. The CLI can `save`/`load` the same format. **Seeded runs** (seed box or `--seed`, `RunState.Seeded`) count in the stats but unlock nothing (`PlayerStats.SeededRunsWon`). |
| Press Runs | 8 stacking difficulty levels (`Core/Run/PressRuns.cs`, numbers in `PressRunConfig`): Proofreader → First Edition (Dailies pay $1) → Late Edition (targets ×1.05/week) → Rush Job (−1 Sunday submission) → Ink Shortage (−1 discard) → Heavy Printing (rerolls +$1) → Censored Press (one of B C F G H M P W Y unplayable per round) → Final Print Run (the Sunday boss adds a second Early/Mid rule, `Reprint`). Level stored in `RunState.PressRun` and re-applied to the config on load. **Unlocks:** winning level N unlocks N+1 (`PlayerStats.HighestPressRunWon`; old profiles with wins start at 1). Game: New run opens a picker once level 2 is unlocked (locked rows greyed), victory screen announces unlocks, sidebar shows the level + censored letter, censored hand tiles struck through; `--press=N` dev flag. CLI: `runsim … press=N`, `new [seed] [press=N]`. |
| Starting decks | `Core/Run/Decks.cs` (numbers in `DeckConfig`): **Standard**, **The Crossword Draft Deck** (+1 Mult per intersection, deadlines ×0.7; words need 3+ letters; no Strict Grammarian), **The Redactor Deck** (thin 30-tile deck incl. Q Z X J; −1 discard), **The Copy Editor's Deck** (starts with Red Pen, +1 discard; 4 Desk Item slots), **The Lexicographer's Deck** (pick an unlocked dictionary; deadlines ×0.85; 4 Desk Item slots). A deck is a `RunConfig` transform applied before the Press Run (`RunRules.ConfigFor`); the run stores `RunState.DeckId`. New knobs: `RunConfig.MinWordLength`, `DeskSlots`, `StartingDeskItems`, `ExcludedBosses`. **Unlocks:** one deck per run won (`StatsQueries.UnlockedDecks`); Press Run unlocks are **per deck** (`PlayerStats.HighestPressRunWon`: deck id → level; old profiles count theirs for Standard). Game: one New-run screen (decks left, the deck's Press Runs right), `--deck=id`; victory screen announces deck unlocks. CLI: `runsim … deck=id`, `new … deck=id`. Each deck wins within ~±7 pts of Standard (§4). **Dictionary unlocks:** the first comes with The Lexicographer's Deck (4 wins), then one more per win (`StatsQueries.UnlockedDictionaries`): The Atlas Unlocked at 5 wins, The Olde English Folio at 6. New-run picker shows a dictionary row for that deck; `--dict=id` dev flag; CLI `new`/`runsim … dict=id`. Tabloid waits for a licensable Slang list. |
| QA | `run_local_qa.bat` (double-click): build → tests → opens game window. Launches the game **in dev mode** (the Best button; user's choice 2026-10-10); `--nodev` for the player's view, `--cli` for console. **GitHub Actions CI** (`.github/workflows/ci.yml`): restore, build (incl. the Godot project) and test in Release on every push/PR to `main`, ~50 s. |

## 3. Decisions already made (don't re-litigate without the user)

- **Engine:** Godot 4 (C#). Core targets net8.0, BCL only. Godot 4.7.1 .NET installed at `D:\Projects\Godot\Godot_v4.7.1-stable_mono_win64\` (path lives in gitignored `qa.local.bat`).
- **Desk Items are pure functions in an ordered pipeline**, not `IObserver` events. Scaling items evolve via `AfterPlay`/`AfterRoundWon` returning updated copies.
- **Denylist scope (2026-10-06): slurs only** — words that target a group (ethnic, racial, religious, homophobic, ableist). Profanity and generic insults stay playable; a word whose main meaning is innocent (CRACKER, GUINEA, PADDY, QUEER, RETARD the verb, KAFIR the sorghum) stays and only its slur sense is hidden from definitions. Definitions skip crude senses rather than hiding the whole word. Every future word list must pass through `Denylist.Default.Filter`.
- **Determinism:** all Core randomness via immutable `Rng`. Hand *display order* is cosmetic (UI-level, own RNG stream) — never affects game state.
- **Scoring is pooled** (one Chips × Mult per play), chosen over per-word sums. Longest word sets the tier; all words add chips.
- **2-letter words are legal** (lowest tier). **Discards exist** (3/round). **Premiums only count under newly placed tiles**; **enhancements trigger per formed word, including old tiles** (deliberate: rewards building onto the grid).
- **Economy:** Balatro-style payouts **plus capped overkill bonus** (user's choice). **Run length:** 5 Weeks × 3 rounds (user's choice). **Newspaper theme** (Week / Daily / Saturday Stumper / Sunday Edition, Desk Items, deadlines).
- **Balance is data-driven:** all numbers live in config records (`ScoringConfig`, `RunConfig`, `ShopConfig`, `EconomyConfig`, Desk Item constructor defaults) and are tuned with simulations, not by hand. Unit tests pin their own numbers so retuning never breaks them.
- **Roadmap decisions (ROADMAP.md):** new boss names *merge* with existing bosses (renames + additions; "Saturday Stumper" boss → "The Puzzle Master", "Tight Deadline" stake → "Rush Job"); every roadmap entry carries a *proposed* effect + status; "Scrabble Board" → "Tile Rack" and the Trademarks dictionary is parked pending legal review; **dictionaries are per-run choices with tradeoffs**, not permanent global unlocks.
- **Economy + targets ×1.15** (user's choice, 2026-10-05): money felt tight after the ×1.3 targets (measured $5.90/round, interest only $0.16/round, rerolls ~25% of spending). Chose **overkill every 25%** (was 50%) + **interest $1 per $4** (was $5) — both reward skill (big finishing plays, saving) — over +$1 base pay. Targets then ×1.15 → **510/1790/5380/14580/23920**; income $6.57/round. Bots don't buy wild offers or the Fountain Pen (measured: no gain).
- **Margin clue columns** (user's choices, 2026-10-05): the pasted spec was adapted — reuse `Core/Profile` (no `*Service`s, no Core events), clues = board words with definitions → lifetime records → newsroom tips. Built, then **hidden** (`Main.ShowClueColumns = false`) because they distracted from the board; revisit in the visual overhaul as background. New stats kept: intersections, close calls, bosses beaten, full-spread rounds ("pangram" redefined per round). Art direction reference: `art/art-direction.jpg`.
- **Wild tiles** (user's choices, 2026-10-05): from every source offered — 2 deck blanks, shop wild tile, wild deck edit, Fountain Pen — and they score **0 letter chips** (Scrabble rule; chosen over the chosen letter's value or a flat 1). The move generator keeps one play per placement whatever a wild stands for (same score) and prunes start squares that can't reach the board, so simulations stay ~0.6 s/run. Measured effect on wins: none beyond noise (58/37/16% at 0.9/0.75/0.6), so targets stay.
- **Fairer letter mix + targets ×1.3** (user's choices, 2026-10-05): hands felt bad in playtests — measured 29.5% of hands at play time had ≤1 or ≥5 vowels. Chose balanced refills + deck at ~42% vowels + max 2 of a vowel (rejected for now: dropping Q; a "Qu" tile stays a release-hygiene item). That raised the reference player's wins ~43% → ~63%, so targets went ×1.3 → **440/1560/4680/12680/20800** (reference ~36%, rounds ~2.6 plays). Drawing the whole bag yourself is fine — opponents' random draws wouldn't change the odds.
- **Week targets 340/1200/3600/9750/16000 + Strict Grammarian deadline ×0.75** (user's choice, 2026-10-05, option "B · Medium" from a sweep; was 225/800/2400/6500/16000; since raised ×1.3 then ×1.15, see above). Goal: rounds that take more than 1–2 plays for a human, and a difficulty ramp instead of a finale wall. Rejected: gentler ×1.3 (rounds barely longer) and steeper ×1.75 (Week 1 wall for weaker players). **Balance against the ScoreFraction model** (`runsim … frac`, reference skill 0.75) from now on.
- **Boss tiers follow measured difficulty** (user's choice over the original roadmap tiers): easiest bosses early, The Strict Grammarian (hardest) as the Week 5 finale.
- **Focus: a working game first.** Steam/launch work (Steamworks, store page, demo, Next Fest) is off the roadmap for now. Simulator throughput target: ~10k runs in minutes (100k+ not needed).
- **Licensing:** no commercially-restricted deps (e.g. FluentAssertions v8). Word lists: public domain only unless licensed.
- **Definitions come from Open English WordNet** (user's choice; CC BY 4.0 → attribution in `THIRD_PARTY_NOTICES.md`, must also appear in in-game credits before release). Wiktionary was rejected for now (CC BY-SA share-alike). Unknown words show "valid word — no definition on file". Definitions appear in the play preview only (user's choice; not in the scoring log or board tooltips).
- **Style Guides are shown in a Run Info-style popup** (Tab / sidebar button), user's choice over an always-visible sidebar table or desk-bar badges.
- **Stationery batch (user's choice, 2026-10-05):** Margin Clip, Scissors, White-Out, Red Ink Bottle, $3 each except
  **Margin Clip $6** (user's call after it measured +13 pts at $3; per-item prices via `ShopConfig.StationeryPrices`). Red Ink is a round-level `RoundConfig.BonusMult` (scoring step 4b, before Desk Items, so ×Mult items multiply it). White-Out's tile is gone for the round (not returned); leftover fragments are only checked when a later play crosses them. **Deadlock escape** (Claude's call, flagged to the user): holding Scissors/White-Out postpones the "no play, no discards" loss, since otherwise the round would end while the player holds the way out.
- **Playtest batch (user's choices, 2026-10-05):** only *pending* tiles can be moved (submitted tiles stay — White-Out is the way to remove one); Desk Item order is explained by a live score preview on the ◀ ▶ arrows (Stationery isn't reorderable); stats = profile + core stats now, **vocabulary grading later** (needs a licensed word-frequency list). Stats count **every word a play forms** (main + cross). QA flags (`--selftest`, `--screenshot`, `--autoplay`) never write the profile.
- **Phase 2 content** (user's choices, 2026-10-05): ship 5 of the 7 proposed Desk Items; **Magnifying Glass deferred**
  (balanced draws have no fixed "next 3 tiles"; a pre-shuffled bag would change every seed — rework idea: show the
  letters left in the bag) and **Brass Paperclip deferred** (needs a keep-tiles picker + `RunState` carry-over).
  **Redundant Copy** zeroes only a repeated word's *letter chips* (tier, intersections, enhancements still count; matched
  by text, so CAT → CATS is new). **The Puzzle Master** = a seeded random pair of two different Early/Mid bosses (never
  the Grammarian), drawn in `BossFor` from the preview's stream. **Coffee Stain** stains one *mirrored pair* (keeps the
  grid symmetric). Claude's calls: Rubber Stamp = ×2 Mult on the round's first play (slot order applies, like every
  ×Mult item); `--give` now applies before the first round (so round hooks work at once); new `--week=N` dev flag.
- **Dictionary overlays** (user's choices, 2026-10-07): first dictionary = **Acronyms ("The Tech Shorthand")**,
  hand-written (no license issue); turned on only by **The Lexicographer's Deck**; **win-count unlocks** (the first with
  the deck, then one per run won; nothing stored). Tuning after measuring: the acronyms are worth ~+2 pts to the bot,
  the lost Desk Item slot ~−15 (23.5% at 0.75), so the deck got **deadlines ×0.85** (over "no cost" or "keep as built").
  Claude's calls: the overlay word graph is ENABLE + overlay built once per id set and cached (`LexiconLoader.For`);
  frontends pass it to every rules call (Core sessions still hold no lexicon); the Lexicographer's Deck is appended
  after Copy Editor's (unlocks at 4 wins) so adding Tabloid later doesn't shift existing unlocks; no brand, company or
  product names in the acronym list (USB/HDMI/Wi-Fi-style trademarks left out), and no entry already in ENABLE
  (LASER, RADAR, SCUBA…); `null` dictionaries in a save is rejected like any other malformed collection.
- **The Atlas Unlocked** (2026-10-10, built as the handoff's first "build next" item; Claude's calls, flagged to the
  user): **places only** (no people, real or fictional, and no mythical places), single words (no NEWYORK-style
  run-togethers), accents dropped (YAOUNDE), **3–7 letters** (8+ can never fit the 7×7 board; a test enforces it),
  no ENABLE words (PARIS, CHINA, TURKEY, TEXAS… are already legal and were dropped automatically: 45 of 612
  drafts). **Contested places get a geographic description** (TAIWAN "island in East Asia", TAIPEI "city in northern
  Taiwan"; KOSOVO, PALESTINE, GAZA left out). Where the capital is disputed or moving, the description says "largest
  city" (JAKARTA, YANGON, COLOMBO). Definitions use a per-dictionary `SenseLabel` ("n." here, "abbr." for acronyms).
  **No tuning:** 16 / 36 / 53% at 0.6 / 0.75 / 0.9 vs The Tech Shorthand's 16 / 39 / 54% (`runsim 200 … frac
  deck=lexicographer dict=…`, seeds 1–200), within noise, so the deck's ×0.85 deadlines serve both.
- **The Olde English Folio is a bonus theme, not new words** (user's choice, 2026-10-10, over parking it or building
  it from Webster's 1913): ENABLE already has 166 of 177 familiar archaic words tested, so a Webster's overlay would
  only add obscure obsolete words. Each archaic word a play forms (main or cross) adds **+3 Mult** (= an
  intersection), after intersections and before Red Ink. Claude's calls: hand-written list of words whose *main*
  sense is archaic (ART, WILT, MINE, ALBEIT, AYE left out), 3–7 letters (YE would be too easy to farm), glosses
  shown as "arch. …" and preferred over WordNet in that run; the 5 non-ENABLE entries (OER, NEER, EER, EEN, OLDE)
  become legal. The theme lives in `RunConfig.Scoring.Theme`, derived from `RunState.Dictionaries` by
  `RunRules.ConfigFor`, so no save-format change. Measured: 16 / 36 / 54% at 0.6 / 0.75 / 0.9 (like the other
  dictionaries). The bonus is small for the bot (round scores +3% at +3, +5% at +5, +9% at +8; scratch harness
  `themecheck`, 400 rounds at 0.75), so +3 stays; a human hunting archaic words should get more.
- **Save & resume** (user's choices, 2026-10-05): **auto-resume** on launch (over a Continue/New prompt) with a
  two-click New run; the save **doesn't store `RunConfig`**, so a resumed run picks up new tuning (the round in progress
  keeps its snapshot). Claude's calls: reflection-based JSON over the records with id-tagged Desk Items/Stationery/bosses
  (no per-type DTOs), version must match exactly (no migrations until a release needs them), a committed fixture save as
  the rename tripwire, pending (unsubmitted) tiles aren't saved (resume = Recall), saves are per profile.
- **Press Runs + unlocks** (user's choices, 2026-10-06): built next after save & resume (unlocks needed something to
  unlock); **an even ladder** (strong player ~55% → ~10%) over the brutal proposal, which measured 0% from level 5.
  Claude's calls: the level lives on `RunState` and is a `RunConfig` transform (so the "config isn't saved" rule
  holds); deltas apply after the boss (Tight Deadline under Rush Job = 2 submissions); censored letter and Reprint
  extra come from salted seed streams (no RNG consumed, previewable, same base boss at every level); New run skips
  the picker until level 2 is unlocked. **5-vowel hands stay possible** (user's choice: balanced draws keep ≥2
  consonants, not ≥3).
- **Starting decks** (user's choices, 2026-10-06): the three decks that need no dictionary first; **one deck unlocked
  per run won** (any deck, any Press Run) over per-deck challenges; **Press Run unlocks per deck** with **one New-run
  screen** (deck + level); The Crossword Draft Deck **drops The Strict Grammarian** from its pool (its rule would cost
  nothing there). Tuning after measuring (all decks should win about as often as Standard): Crossword Draft's 3-letter
  rule bans 2-letter cross words, so intersections mostly vanish and it won 10% → **deadlines ×0.7** (kept the rule
  over dropping it for "+1 intersection Mult, −1 discard", which measured 39.5% but had little identity); the
  no-rare-letter Redactor Deck won 61% → **Q Z X J swapped in** (over −3 discards, 54%). Claude's calls: a deck is a
  `RunConfig` transform like a Press Run (config still not saved; deck applied first); the Redactor tiles are
  hand-picked; clicking a Press Run row starts the run (no separate Start button); a starting Red Pen can be sold.
- **Cheap parallel tracks + title menu** (user's choices, 2026-10-10): built CI, CLI Stationery + save/load, a seed
  box and a profile picker. **Seeded runs count stats but unlock nothing** (over counting everything or nothing).
  **New run always opens the picker** (the seed box lives there; the old "skip until something is unlocked" is gone).
  The user first put the profile picker in the Stats popup, then asked for a **start menu** instead: launch now shows
  a **title menu** (Continue default + Enter, so resuming is still one keypress) — this **replaces the 2026-10-05
  auto-resume decision**; the sidebar's New run became Menu. **Settings v1** = reduced motion, fullscreen, text size
  (+ a "sound comes later" row). Claude's calls: settings are game-wide (`user://settings.cfg`), profiles have no
  delete/rename (deleting is permanent), a profile appears in the list once it has a file (any run start writes it),
  `--seed` counts as a chosen seed, CI on `ubuntu-latest` without a NuGet cache. **Text size scales fonts, not the
  window**: zooming the whole UI (`ContentScaleFactor`) overflowed the 1440×900 layout at any size above 100%, so
  `UiKit.TextScale` multiplies every font except tile faces and a change rebuilds the UI; sizes stop at 120%
  (130% overflowed vertically). Two layout fixes came out of it: the sidebar scrolls instead of growing past the
  window, and the seed/run-name line wraps on its own row (a long custom-run name used to widen the sidebar to
  ~670 px).
- **Hint is not a free solve** (user's choice, 2026-10-05): the free Hint shows a decent play, never the best; the best play is the paid one-shot **Answer Key** Stationery; `--dev` keeps an unlimited best play for development — since 2026-10-10 as a separate **Best (dev)** button next to Hint (user's request), so a dev build still shows the player's decent Hint. Chosen over money-cost hints, limited charges, or nudge-only hints.

## 4. Starting decks (2026-10-06)

CLI `runsim 200 <skill> frac deck=<id>` (seeds 1–200, evaluating bot, ScoreFraction model):

| Deck | 0.6 | 0.75 (reference) | 0.9 | 0.9 at Press Run 8 |
|---|---|---|---|---|
| Standard | 14% | 39% | 55% | 7.5% |
| The Crossword Draft Deck | 17% | 37% | 62% | 8% |
| The Redactor Deck | 17% | 39% | 57% | 7% |
| The Copy Editor's Deck | 16% | 42% | 62% | 7% |
| The Lexicographer's Deck (Tech Shorthand, 2026-10-07) | 16% | 39% | 54% | — |
| The Lexicographer's Deck (Atlas Unlocked, 2026-10-10) | 16% | 36% | 53% | — |
| The Lexicographer's Deck (Olde English Folio, 2026-10-10) | 16% | 36% | 54% | — |

- First versions: Crossword Draft (no deadline cut) **10 / 26%** at 0.75 / 0.9 — +2 or +4 intersection Mult and/or +1
  discard only reached 9.5–12.5%; deadlines ×0.75 → 30%, ×0.6 → 57% (0.9: 71.5%), ×0.5 → 68.5%. Redactor (no rare
  letters) **61 / 76%** — −2 discards 58.5%, −3 54%, J X swapped in 53% (0.9: 68.5%). Copy Editor's 42 / 62%; with 3
  slots 17.5%, so 4 stays.
- Crossword Draft and Copy Editor's run +7 at 0.9 — watch in playtests. Crossword Draft's losses shift to The Puzzle
  Master (19 of 200 at 0.75), its only finale.
- Lexicographer's Deck sweep (200 paired seeds, 0.75): Standard 39%, Standard + Tech Shorthand words 41% (0.9: 55 vs 56%),
  + words −1 discard 31%; Lexicographer 4 slots 23.5%, + deadlines ×0.85 39% (0.9: 53.5%), ×0.75 44%, +1 discard 30.5%.
  Scratch harness `lexisweep` (arms pass `LexiconLoader.For([id])` as the lexicon of a Standard run to measure the words
  alone, or `deck: lexicographer` with pre-scaled `WeekTargets`).
- Scratch harness `decksweep` (arms = `Decks.Apply(base, id, new DeckConfig(...))` or a hand-edited config, played
  with the Standard deck id so `NewGame` doesn't re-apply the deck; `RunConfig.StartingDeskItems` is still honoured).

## 4a. Press Run ladder (2026-10-06)

`presssim` scratch harness: 200 paired runs per level (seeds 1–200), evaluating bot, ScoreFraction model; Proofreader =
the snapshot below.

| Level | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| Strong (0.9) win % | 55 | 44.5 | 36.5 | 23 | 18.5 | 15.5 | 13.5 | 7.5 |
| Reference (0.75) win % | 39 | 23.5 | 17.5 | 6 | 5 | 4.5 | 2.5 | 1 |

- Each rule alone at 0.9 with the **first** numbers (150 runs, vs 55%): −1 submission every round −34, censoring one
  of D/L/N/R/S/T −26, Final Print Run −16, targets ×1.1/week −14, no Daily pay −9, Desk Items/rerolls +$1 −9, −1
  discard −6. Stacked they hit 0% from level 5 (0.75: 0% from level 4).
- Softening, step by step (0.9 at levels 1–8): Sunday-only Rush Job + rarer consonants + ×1.05 → 55/42/33/18.5/15/
  6.5/5.5/2; then Dailies $1 + reroll-only Heavy Printing → current. Rush Job is still the biggest step (−13.5).
- Sunday Edition losses dominate from level 4 up (boss loss rate ~14% → ~31% at level 8).

## 4b. Balance snapshot (after Phase 2 content, 2026-10-05)

Same targets and economy as below; 23 Desk Items, 7 bosses. Scratch harness `p2sim`, 200 paired runs per arm
(seeds 1–200), evaluating bot, ScoreFraction model. "old" = shop limited to the 18 old items (`ShopConfig.DeskItemIds`)
and the old boss tiers.

| Skill | old | new items only | new bosses only | **all new (current)** |
|---|---|---|---|---|
| 0.9 | 56.5% | — | — | **55%** |
| 0.75 (reference) | 36% | 32% | 42% | **39%** |
| 0.6 | 13% | — | — | **14%** |

- All within noise (±3.5) of the old content, so **targets stay**. Rounds unchanged (~2.5 subs per won round, 55% won in 1–2).
- New items alone −4 (pool dilution); new bosses alone +6: the Final tier now splits between The Strict Grammarian
  (27% loss rate when reached, 0.75) and the softer **Puzzle Master (~8–13%)**. **Redundant Copy ~10%** (Mid, between
  Vowel Drought ~6% and Tight Deadline ~15%).
- Final-desk rates at 0.75: Coffee Stain 31%, Printing Press Roller 16%, Rubber Stamp 15%, Etymology Tome 9%, Tile Rack
  0%. Pulitzer fell 63% → 44% (the bigger pool dilutes the old dominant picks).
- **Tile Rack is never bought:** re-scoring can't see a bigger hand. Forcing a fixed gain (`ShopBotConfig.DeskItemGain`
  0.05 / 0.1 / 0.2) got it bought in ≤6% of runs and changed wins by ≤0.5 pts → default off.
- **Coffee Stain's downside is invisible to the bot** (it only sees +4 Mult): Tight Margins losses rose 9 → 17 of ~170
  at 0.75 in the arms with new items (2 blocked squares on a 5×5 board hurt), not at 0.9/0.6 — possibly noise.

## 4c. Previous snapshot (after the economy change + targets ×1.15, 2026-10-05)

Week targets **510/1790/5380/14580/23920**, day multipliers ×1/×1.3/×1.6, The Strict Grammarian's deadline ×0.75,
balanced draws, 100-tile deck with 2 wilds, Margin Clip $6 (the bot buys it), overkill every 25%, interest $1 per $4.

| Skill (ScoreFraction) | Victory | Mean subs per won round | Won in 1–2 subs |
|---|---|---|---|
| 0.9 | **60%** | 2.31 | 69% |
| 0.75 (reference) | **36.7%** | ~2.6 | ~55% |
| 0.6 | **14.7%** | 2.83 | 41% |

**Economy** (`econsim` scratch harness; money recorded per round by `SimulatedRunRound.Payout`/`InRoundMoney`/`Shop`):
income per round played base $3.71 · unused submissions $1.33 · overkill $1.09 · interest $0.38 = **$6.57** (was $5.90);
$9.8 held entering a shop; per shop: Desk Items $4.09, Style Guides $1.09, Stationery $0.53, rerolls $2.11, deck edits ~$0.
Tweaks measured before choosing (vs then-current 37%): +$1 base pay +14.5 pts, overkill 25% +10, interest per $4 +3,
rerolls from $3 +1.5; old ×1.0 targets +28.5. Wild buying: wild tile/edit ±0.5, Fountain Pen −1.5 → bot gains stay 0.

### Previous snapshot (balanced draws + targets ×1.3, before the economy change)

Week targets 440/1560/4680/12680/20800, balanced draws, 41-vowel deck, Margin Clip $6 (the bot buys it).
**Reference player = ScoreFraction model** (`runsim 150 0.75 frac`: picks the best play worth ≤75% of the best one).
Evaluating shop bot (target-sweep harness, 150–200 runs per cell):

| Skill (ScoreFraction) | Victory | Mean subs per won round | Won in 1–2 subs |
|---|---|---|---|
| 0.9 | **59%** | 2.33 | 68% |
| 0.75 (reference) | **36.5%** | 2.57 | 55% |
| 0.6 | **15%** | 2.73 | 43% |

With wild tiles added (2 deck blanks; bots don't buy wild offers or the Fountain Pen): **58% / 37% / 16%** — unchanged within noise.

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
- Stationery value is measured with fixed buying gains and rule-based use (see the per-item table above), so it's a floor on what a human gets.

**History:** 150/400/900/1900/3800 → 225/800/2400/6500/16000 (tuned against the percentile 0.9 bot: 39% wins, but a
human-like player won 81–87% of rounds in 1–2 submissions and runs died at a 40%+ Grammarian finale) → current.

## 5. Open concerns / known gaps

1. **Overall difficulty** (see §4b): the reference player wins ~39%, a weaker one ~14%, a strong one ~55% — after
   balanced draws, wild tiles, the richer economy and targets ×1.3 × 1.15. Check in playtests; the Press Runs (§4a)
   now carry the extra difficulty, and every deck sits near Standard (§4).
   **Margin Clip is still the strongest Stationery at $6** (+9.5 pts when always offered, ~+6 in the real pool); if
   it stays too strong, price won't fix it — give it a cost instead (see §6). The other four don't pay for themselves
   for the bot.
2. **Possible dominant items / weak deck edits** (see §4c): Pulitzer, Margin Notes, Word Count near-universal picks;
   deck edits not worth buying. The bot values items by the *best* play per decision (strong-shopper view), not
   the play its skill level would pick. Stationery is bought at fixed per-item gains (`ShopBotConfig.StationeryGain`),
   not by re-scoring, and its in-round use is rule-based (`StationeryBot`) — a floor on what a human gets from it.
3. ~~No save/load~~ ✅ (save & resume, see §2/§3). Saves have no migrations: any change to `RunSaveJson.CurrentVersion`
   discards players' runs — add a migration before the first external playtest build changes the format. Known quirk:
   after "Keep going (endless)" the run end is already recorded, so a later endless loss isn't counted in stats.
4. **UI is first-pass:** no art or sound. Settings v1 exists (reduced motion, fullscreen, text size ≤120%; larger text
   needs a layout that reflows). Fullscreen isn't covered by the self-test (it would flip the QA window) — check it by hand. Animations: scoring ring-up, NEW-tile pop-in, hand-reorder slide. Hand drag confirmed good by the user with a real mouse. The art direction (`art/art-direction.jpg`) is the target for the visual overhaul.
5. **Content hygiene for release:** ~~ENABLE slurs~~ ✅ denylist + clean definitions (2026-10-06). Borderline calls worth a second look: MULATTO, HEBE, HOMO, MIDGET, MICK, KRAUT, BLACKAMOOR, PAPIST are denied; GRINGO, REDNECK, CRACKER, SHIKSA, QUADROON, GYP, COOLY are kept. Old profiles may still list a now-denied word in their stats. "Q without U" is a dead tile (consider a "Qu" tile).
6. **Hand arrangement is UI-only** but saved with the run (pending tiles are not).
7. **~38% of ENABLE has no definition** (mostly obscure words, e.g. GLEY, the user's own example; 2–5-letter words ~73% covered, every 2-letter word covered). Options if it matters: extend `supplement.txt` for words that come up often, or add a second source after a license check (Wiktionary is CC BY-SA).
8. ~~The CLI has no Stationery commands~~ ✅ (`use`, `sellst`, `give`, plus `save`/`load`).
9. ~~No profile picker~~ ✅ (title menu → Profile). Profiles can't be deleted or renamed in-game yet; stats aren't
   shown in the CLI. Vocabulary grading not started.
17. **Self-test flakiness:** the first self-test run after a rebuild occasionally fails a few early hand click/drag
    checks (timing while Godot warms up); a rerun passes. Seen twice on 2026-10-10. If it gets worse, add a few
    warm-up frames before step 1.
10. **Wild items don't pay off for the bot**: wild tile / wild edit ±0.5 pts, Fountain Pen −1.5, all three −5 (200 paired runs), so its buying gains stay 0 — a human may value the flexibility more; check prices ($6 / $5 / $3) in playtests. The CLI plays a wild automatically for a missing letter; `?` selects one in discard/strike letters.
11. **Rerolls are ~30% of shop spending** for the bot ($2.11 of ~$7.30 per shop). Not a problem yet, but watch whether players feel rerolls are mandatory.
12. **Phase 2 items to watch in playtests:** Tile Rack (+1 hand size) is worthless to the bot — a human may value it
    more; if not, buff it (e.g. +2, or +1 hand size and +1 discard). Coffee Stain is the most-bought new item but its
    stained squares are a real cost on Tight Margins' 5×5 board. The Puzzle Master is softer than The Strict Grammarian;
    if the finale should be hard, give it a `TargetScale` above 1 or exclude its easiest pairs.
13. **Clue columns hidden**: the engine runs (stats keep updating) but `Main.ShowClueColumns = false`; the self-test checks they stay hidden. Restyle them as background before turning them on.
14. **The Tech Shorthand is worth little to the bot** (+2 pts), probably because most hands already have an ENABLE
    play nearly as good (not measured). A human may get more out of it (or find CPU/NASA-style
    plays jarring); check in playtests. The list (297 entries) was written by Claude — **the user should skim it**
    (`src/Crossword.Core/Lexicon/Data/tech-shorthand.tsv`), e.g. whether agency names (FBI, CIA, NSA, IRS) and chat
    shorthand (LOL, OMG-style; OMG itself was left out) belong.
15. **The Atlas Unlocked's list** (567 places, `src/Crossword.Core/Lexicon/Data/atlas.tsv`) was also written by Claude —
    **the user should skim it** for wrong facts and for political sensitivity (contested places, capitals; see §3).
    Crosswordese like ERIE, OSLO, ASIA, IOWA, OHIO, ELBA, ARAL makes it vowel-friendly; worth as much to the bot as
    the acronyms (§4).
16. **The Olde English Folio's bonus is small for the bot** (+3% round score); watch whether humans chase archaic
    words. If it feels flat, raise `Dictionaries.OldeFolioMult` (+5 or +8; re-measure). Skim its 106 words
    (`src/Crossword.Core/Lexicon/Data/olde-folio.tsv`) too.

## 6. Suggested next steps (offered to the user; they haven't picked yet)

Longer-term phases live in `ROADMAP.md` §11 (phases 0–4 ✅, phase 5 🟡: decks ✅ except Tabloid, dictionaries ✅
except Slang — both wait for a licensable slang list — denylist ✅), plus parallel tracks (CI, seed entry, Daily
Editorial, presentation, onboarding).

**Playtests (the user's side):**
0. **Title menu, profiles and Settings** (new 2026-10-10): launch normally — the title should show Continue for
   your saved run (Enter resumes it). Try Profile → create one and switch back, Settings → reduced motion, fullscreen
   and text size, and the seed box in New run (a seeded win should say it unlocks nothing).
0. **The Lexicographer's Deck** (`--dict=tech-shorthand` / `atlas` / `olde-folio`, or win 4–6 runs): do acronyms,
   place names and the archaic bonus feel fun or jarring; is ×0.85 enough to pay for the 4th slot; skim the lists
   (§5.14–5.16).
1. **Starting decks + Press Runs:** win a run to unlock The Crossword Draft Deck, check the New-run screen (deck cards,
   per-deck ladder), and play each deck — does Crossword Draft feel fair with ×0.7 deadlines and no 2-letter words?
   Does the Redactor's Q Z X J every round feel like a fun cost? Is Copy Editor's too strong (+7 at 0.9)? Also the
   Press Run ladder (Rush Job is the steepest step), Censored Press and a Final Print Run Sunday. Dev flags:
   `--deck=id`, `--press=N`.
2. **Save & resume:** play a submission, close the window, relaunch — the run should come back exactly (board, hand
   order, Desk Items, money, deck). Also the shop and the victory screen.
3. **General feel:** hands, money, difficulty, ring-up escalation (`Juice.cs` thresholds), week progress, the free Hint,
   Stationery value (Margin Clip $6; Scissors/White-Out worth $3?).

**Build next (Claude's recommendation first):**
0. **Queued by the user (§0): economy re-examination** (the dev-only Best button is done ✅ 2026-10-10).
1. ~~Cheap parallel tracks~~ ✅ 2026-10-10 (CI, seed box, profile picker, CLI save/load + Stationery) plus a title
   menu and Settings v1. Leftovers: copyable seed, profile delete/rename, volume (with audio).
2. **Visual overhaul** toward `art/art-direction.jpg` (ROADMAP §8): newsprint/mahogany theme centralized in `UiKit`,
   open-license fonts, legible premium labels, Desk Items as physical objects, then re-enable the clue columns as
   quiet background.
3. **Balance pass on outliers:** item pick rates at current targets (Pulitzer / Margin Notes / Word Count), deck edits
   worth buying, Margin Clip (give it a cost if it still dominates), Tile Rack.
4. **Deferred content:** Magnifying Glass (rework to "letters left in the bag"?), Brass Paperclip (keep-tiles picker),
   Highlighter, Correction Tape.
5. **UI polish:** tile placement animation, sound, deck viewer, tooltips for Desk Items/bosses; a reflowing layout so
   text can go past 120%.

## 7. How to work in this repo (practical tips learned the hard way)

- **Verify, don't assume.** After changes: `dotnet build wordgame.sln` (warnings are errors) → `dotnet test`. For UI changes also run the screenshot and self-test flags:
  - `"D:/Projects/Godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe" --path game -- --seed=42 --screenshot=<scratchpad>/shot.png` then view the PNG. Extra flags: `--give=red-pen,pulitzer,answer-key --autoplay=3 --hint --dev` (`--give` takes Desk Item or Stationery ids; `--dev` = the Best (dev) button that places the best play).
  - `... --path game -- --seed=42 --selftest` → PASS/FAIL lines, exit 1 on failure. Extend `game/Scripts/Main.SelfTest.cs` for new interactions.
  - QA flags (`--selftest`/`--screenshot`/`--autoplay`) skip the title menu, so `--screenshot` shows the game; to see the
    title, profiles or settings, call `ShowTitle()`/`ShowProfiles()`/`ShowSettings()` in a temporary self-test step and
    save the viewport there. Profile tests point `_profileRoot`/`_saveRoot` at `__selftest` folders and delete them.
    `TypeText("abc")` types into the focused `LineEdit`.
  - Build the Godot project (`dotnet build game/Wordgame.Godot.csproj`) before launching Godot; it loads assemblies from `game/.godot/mono/temp/bin`.
- **Desktop control (computer-use) can't target the portable Godot exe** — use `--selftest`/`--screenshot` instead.
- **Simulated input quirk:** under `Viewport.PushInput`, `_DropData`'s `atPosition` arrives in the wrong coordinate space. Don't base UI logic on it: hand reorder tracks the cursor in `Main._Input` (canvas coords for real and pushed input, matching `GetGlobalRect()`) and drops into the ghost's slot.
- **Mid-interaction screenshots:** `--screenshot` quits before any input; to see a drag mid-flight, temporarily add `GetViewport().GetTexture().GetImage().SavePng(...)` inside a self-test step.
- **Editing gotchas:** the Write/Edit tools turn `\uXXXX` escapes into literal characters, and bash heredocs can mangle `\n` (a Python `'\\0'` written into C# once became a literal NUL — use visible sentinels like `'_'`). For multi-file edits, write a Python script with raw strings (`r"""..."""`) to the scratchpad and run it; for C# char literals prefer `(char)0xFEFF` style.
- **Python on this machine prints with cp1252:** printing non-ASCII (e.g. ✅, →) from a script raises `UnicodeEncodeError` — write to files instead or set `PYTHONIOENCODING=utf-8`. Long bash heredocs containing quotes/apostrophes can also fail to parse; use the Write tool for data files.
- **Definitions data:** edit `tools/Crossword.DefinitionsBuilder/supplement.txt` (`WORD | pos | gloss`, replaces WordNet for that word), then `dotnet run -c Release --project tools/Crossword.DefinitionsBuilder` (needs `english-wordnet-2025.xml.gz` in gitignored `tools/data/`; download it from the OEWN GitHub 2025-edition release if missing). It prints coverage and warns about supplement words not in ENABLE.
- **Batch files must be CRLF** (`.gitattributes` enforces; normalize with `sed -i 's/\r*$/\r/'` after writing).
- Running `run_local_qa.bat` from a captured shell hangs because Godot inherits the pipe — expected; it's fine on double-click.
- Target sweeps: wrap a boss in a harness-side `BossModifier` subclass whose `ModifyRound` calls `Inner.Apply(config) with { Boss = null }` and rescales `TargetScore` — lets you test boss deadlines without touching Core. Flatten configs × seeds into one `.AsParallel()` query (20 cores: ~0.5 s per run).
- Stationery value: same harness pattern, one arm per item with `ShopConfig.StationeryIds = {id}` (the shop always
  offers it) and `ShopBotConfig.StationeryGain = {id: g}`, paired seeds against a no-Stationery baseline. A full
  5-item × 2-gain sweep at 200 runs/arm takes ~18 min on 20 cores.
- Scratch harnesses used on 2026-10-05 (not in the repo; the scratchpad is per-session — recreate from this pattern):
  `p2sim` (Phase 2: old vs new content arms via `ShopConfig.DeskItemIds` + old `BossTiers`, Tile Rack gains, boss
  loss rates and final-desk pick rates; `bosses <week>` lists each seed's boss), `stsim` (per-item Stationery value / prices), `drawsim` (letter-mix arms + hand quality per play), `targetsim`
  (`[runs] [skill] [scales…]` → win %, subs per won round, % won in 1–2), `wildsim` (wild buying gains), `econsim`
  (income per round by source + shop spending by category, from `SimulatedRunRound.Payout`/`InRoundMoney`/`Shop`).
  Each is a console app referencing `src/Crossword.Core` running `RunSimulator.PlayRun` over paired seeds with
  `.AsParallel()`; 200 runs × 4–8 arms ≈ 8–20 min on 20 cores. Harness baselines must set `StationeryGain = null`
  explicitly if they mean "no Stationery" (the default bot buys Margin Clips).
- Dictionary checks (2026-10-10): the whole-run numbers come straight from CLI `runsim 200 <skill> frac
  deck=lexicographer dict=<id>` (~3–5 min per arm). Scratch harness `themecheck` measures a theme bonus at the round
  level instead: `RoundSimulator.PlayRound` over 400 seeds with `ScoringConfig.Default with { Theme = … }` at several
  `MultPerWord` values vs no theme (seconds, not minutes). Word lists were drafted as `NAME | text` in the scratchpad
  and a Python script dropped ENABLE words / over-long names and wrote the sorted `.tsv` (header comments included).
- Balance experiments: a throwaway console project in the scratchpad referencing `src/Crossword.Core` (loop over configs, call `RunSimulator.PlayRun(seed, config, lexicon, skill, strategy, botConfig, model: SkillModel.ScoreFraction)` with `.AsParallel()`, build `-c Release`) is faster than editing defaults repeatedly. 100 runs ≈ 1 min with the evaluating bot. Note `RunConfig.Days` multipliers must be set explicitly in such harnesses. n=60 runs is too noisy (±6 pts) to compare close variants; use 150+.
- The user's machine has old Godot crash dumps; the project uses the **GL Compatibility** renderer, which has been stable.

## 8. Working with the user

- Wants Claude to **flag unsound decisions or anything against the spirit of the game** — give a recommendation, not just options.
- Prefers seeing things in the **real game UI** (disliked the console). Keep `run_local_qa.bat` double-clickable and working.
- Practice so far: work in focused commits on `main` with descriptive messages and push to `origin` after each verified chunk; the user has been fine with this.
- Starts new chats periodically to keep context small → **keep this file current**.

## 9. Commit history (newest first)

```
f3ecbbb Add a Settings page: reduced motion, fullscreen and text size
5d3d811 Open the game on a title menu with profile switching
256edf3 Start seeded runs from a seed box in the New-run picker
9d39c95 Use, sell and give Stationery and save/load runs in the CLI
c0da8b1 Build and test every push with GitHub Actions
afed9e0 Bring handoff up to date: phase 5 status, dictionary harness notes
a871547 Document The Olde English Folio in rules, roadmap and handoff
9eaafb2 Add The Olde English Folio as a bonus theme dictionary
a3bd257 Document The Atlas Unlocked in rules, roadmap and handoff
4a4f20d Add The Atlas Unlocked dictionary of place names
50ac131 Document dictionary overlays in rules, roadmap and handoff
2c251fc Tune The Lexicographer's Deck to the Standard Deck's win rate
75f37a4 Add dictionary overlays with The Tech Shorthand and The Lexicographer's Deck
d83734d Document the slur denylist in rules, roadmap and handoff
85a9054 Deny slurs in every word list and hide crude definition senses
ac624e6 Document starting decks in rules, roadmap and handoff
2618be7 Tune the Crossword Draft and Redactor decks to the Standard Deck's win rate
9463a16 Add starting decks with per-deck Press Run unlocks
b40f540 Document Press Runs and unlocks in rules, roadmap and handoff
7ecc743 Soften the Press Run ladder to an even descent
ac50688 Pick and unlock Press Runs in the game UI
5132a50 Add Press Run difficulty levels with unlock tracking
ef7b52a Document save and resume in rules, roadmap and handoff
29a50ef Save the run after every move and resume it on launch
293af88 Serialize a run in progress to JSON for save/resume
f5caf34 Document Phase 2: roadmap, rules, balance snapshot and handoff
9cb2d1c Let balance harnesses limit the shop's Desk Item pool
e3ee7c9 Add the Redundant Copy and The Puzzle Master bosses
af15c90 Add Tile Rack and Coffee Stain with a round-start Desk Item hook
367622e Add Etymology Tome, Rubber Stamp and Printing Press Roller
c825b27 Update CLAUDE.md balance numbers and bot notes
2a3001d Bring handoff fully up to date with this session
238db48 Fix self-test count in handoff
b267860 Record economy and clue commits in handoff
88faed4 Pay overkill every 25% and interest per $4; raise targets x1.15
2cc96fa Hide the clue columns until the visual overhaul
22de92e Add the art direction reference image
ff54932 Print ACROSS/DOWN clue columns beside the board
925442a Let the bots buy wild tiles and use the Fountain Pen; record money flow
fd41f74 Add wild tiles: deck blanks, shop offers, Fountain Pen, wild edit
b3df7b7 Raise week targets x1.3 after balanced draws
ab45824 Balance tile draws and raise the deck's vowel share
08ab013 Fade NEW tile tags after a few seconds or on first touch
a71d24f Document the playtest batch: progress, hand UX, ring-up, player stats
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
