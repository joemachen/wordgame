# wordgame — Roadmap & Feature Design

The intended design for features we plan to build. All effects and numbers marked *proposed* are first guesses;
they get tuned with simulations (`runsim`) and playtests before shipping.

| Document | Answers |
|---|---|
| **`ROADMAP.md`** (this file) | What we intend to build, and in what order |
| [`handoff.md`](handoff.md) | Where the project is right now: status, open decisions, next steps |
| [`CLAUDE.md`](CLAUDE.md) | How the code works: rules, architecture, conventions |

**Status legend:** ✅ Implemented · 🟡 Planned · ⏸ Parked (blocked on a decision or review)

When something lands, mark it ✅ here and record the details in `handoff.md` / `CLAUDE.md`.

---

## 0. Design guardrails

- **Unlocks add variety, not power.** Meta-progression opens up new ways to play. It never makes every future run
  stronger.
- **Every option has a cost.** Each dictionary, deck and stake gives an upside and takes something away.
- **Numbers live in config.** All tuning values go in config records and Desk Item constructor defaults, and are
  tuned by simulation (see `CLAUDE.md` → Balance Workflow).
- **Commercial-safe content.** Word lists must be public domain or permissively licensed. Item and content names
  must not use third-party trademarks.
- **Determinism.** Every new random element goes through `Rng` and is stored in run/round state.

---

## 1. Lexicon systems & meta-unlocks

### Base lexicon ✅

**ENABLE** (public domain, ~173k words). The game uses words of 2–15 letters. QI and ZA are not in it.

> **Release blocker:** a slur/offensive-word denylist is needed before shipping. It applies to ENABLE **and**
> every overlay below.

### `CustomLexicon` overlays (unlockable dictionaries) 🟡

A meta-unlock makes a dictionary **available**. Whether it's active is decided **per run**, through a starting deck
or a Desk Item, and each one comes with a tradeoff. Dictionaries are never permanently active everywhere.

| Dictionary | Theme title | Turned on by (*proposed*) | Tradeoff (*proposed*) | Source / license | Status |
|---|---|---|---|---|---|
| Slang | — | Tabloid Deck | Round targets ×1.1 | **Needs review.** Wiktionary is CC-BY-SA (share-alike); Urban Dictionary can't be licensed. Biggest denylist risk. | 🟡 |
| Proper Nouns & Toponyms | *The Atlas Unlocked* | Lexicographer's Deck, or an "Atlas" Desk Item | Uses a deck/Desk Item slot | GeoNames (CC-BY, credit required), Wikidata (CC0) | 🟡 |
| Acronyms & Initialisms | *The Tech Shorthand* | Lexicographer's Deck | Only entries of **3+ letters** count, so 2-letter acronyms (TV, PC) don't flood the board | Wikidata (CC0) or a hand-curated list | 🟡 |
| Archaic & Middle English | *The Olde English Folio* | Lexicographer's Deck | Uses the deck's dictionary choice | Webster's 1913 (public domain), Project Gutenberg texts | 🟡 |
| Trademarks & Brand Names | *The Commercial Registry* | — | — | **No public-domain source; trademark risk in a paid game** | ⏸ Needs legal review |

**Technical notes**
- An overlay is merged with ENABLE into one word graph (`IWordGraph`) built at run start, so `MoveGenerator`,
  `PlacementValidator` and the hint system stay unchanged. The ENABLE word graph currently builds in about 0.5 s.
- The active dictionaries are stored in `RunState`, so a seed + run setup still reproduces the run exactly.
- **Depends on:** save/load and a meta-progression profile (phase 4).

---

## 2. Unlockable starting decks 🟡

Each deck changes the starting setup, with an upside and a cost. All effects are *proposed*.

| Deck | Upside | Cost |
|---|---|---|
| **The Tabloid Deck** | Slang dictionary is legal | Round targets ×1.1 |
| **The Crossword Draft Deck** | +1 Mult per intersection | 2-letter words are illegal (minimum word length 3) |
| **The Redactor Deck** | Thin deck at the 30-tile minimum (more predictable draws) | −1 discard per round |
| **The Copy Editor's Deck** | Starts with Red Pen and +1 discard per round | Only 4 Desk Item slots |
| **The Lexicographer's Deck** | Pick one unlocked dictionary at run start | Only 4 Desk Item slots |

The current standard starting deck (`StartingDeck.Create`) stays as the default deck.

---

## 3. Expanded item pool

### Desk Items (passives)

These are in addition to the 18 Desk Items already in `DeskItemCatalog`. Rarity is a first guess.

| Desk Item | Effect (*proposed*) | Rarity | Status |
|---|---|---|---|
| **Red Pen** | +2 Mult | Common | ✅ |
| **Etymology Tome** | The longest word's letter chips count twice. Rewards rare letters and long words, which addresses the skill-gap concern. | Uncommon | 🟡 |
| **Coffee Stain** | +4 Mult, but one random empty cell is stained (blocked) each round | Common | 🟡 |
| **Tile Rack** *(was "Scrabble Board", renamed because Scrabble is a trademark)* | +1 hand size | Uncommon | 🟡 |
| **Rubber Stamp** | The first submission of each round scores twice | Uncommon | 🟡 |
| **Magnifying Glass** | See the next 3 tiles in the bag; +2 Mult | Common | 🟡 |
| **Brass Paperclip** | Keep up to 2 hand tiles between rounds; they start in your next hand | Uncommon | 🟡 |
| **Printing Press Roller** | Scaling: gains ×0.1 Mult each time a play forms 3+ words | Rare | 🟡 |

### Stationery (consumables) 🟡 — new system

One-shot items bought in the shop, held in **2 Stationery slots**, used during a round (the equivalent of
Balatro's Tarot cards). Needs new state (slots in `RunState`), shop offers, use actions in `RunRules`, and UI.

| Stationery | Effect (*proposed*) |
|---|---|
| **White-Out** | Remove one tile from the board |
| **Highlighter** | One tile's letter value ×3 on your next play |
| **Fountain Pen** | Rewrite one hand tile as any letter |
| **Scissors** | Swap up to 2 hand tiles for new draws without spending a discard |
| **Correction Tape** | Undo your last submission this round; its tiles return to your hand and you get the submission back |
| **Red Ink Bottle** | +3 Mult on every play for the rest of the round |
| **Margin Clip** | +1 submission this round |

### Style Guides (word-tier upgrades)

The mechanic is ✅ implemented: each Style Guide permanently levels one word-length tier (`StyleGuideOffer`,
`RunState.TierUpgrades`). What's 🟡 planned is giving each tier a named Style Guide:

| Style Guide | Upgrades |
|---|---|
| **Pulp Paperbacks** | 2-letter words |
| **The Pocket Dictionary** | 3-letter words |
| **Chicago Manual of Style** | 4-letter words |
| **Unabridged Dictionary** | 5-letter words |
| **Gridiron Gazette** | 6-letter words |
| **The Lexicographer's Omnibus** | 7+-letter words |

---

## 4. Boss Editors (tiered progression)

Bosses appear in the Sunday Edition (the third round of each week). Today `RunRules.BossFor` picks from all
bosses using only the seed and week. The plan is for it to pick from the **pool for that week's tier** instead,
still seeded and still visible in advance.

Week ranges don't overlap: **Early = Weeks 1–2, Mid = Weeks 3–4, Final = Week 5**. (The original draft had
overlapping ranges, 1–2 / 2–4 / 4–5.)

| Tier | Boss Editor | Effect | Relation to current bosses | Status |
|---|---|---|---|---|
| Early | **The Strict Grammarian** | Only 3+ letter words count | Rename of *Strict Editor* | ✅ effect · 🟡 rename |
| Early | **Ink Spill** | 6 symmetric blocked cells | Rename of *Black Squares* | ✅ effect · 🟡 rename |
| Mid | **Vowel Drought** | Vowels score −1 chip (*option:* rework to fewer vowels in the bag) | Rename of *Vowel Tax* | ✅ effect · 🟡 rename |
| Mid | **Redundant Copy** | Words already formed this round score 0 chips (*proposed*) | New | 🟡 |
| Mid | **Tight Deadline** | Only 3 submissions | Kept as is | ✅ |
| Final | **Tight Margins** | 5×5 board | Rename of *Pocket Edition* | ✅ effect · 🟡 rename |
| Final | **The Puzzle Master** | Applies two boss effects at once (*proposed*) | New. Renamed from "Saturday Stumper", which is already the name of the middle round of each week | 🟡 |

---

## 5. Difficulty stakes — "Press Runs" (8 tiers) 🟡

Unlocked one at a time by winning a run at the previous tier. Each tier **includes all tiers below it**.
All effects are *proposed*. The ladder copies Balatro's stake structure (a mechanic, which is fine to reuse);
the effects are our own.

| # | Press Run | Color | Adds (*proposed*) |
|---|---|---|---|
| 1 | **Proofreader** | White | Base game |
| 2 | **First Edition** | Red | Daily rounds pay no base pay |
| 3 | **Late Edition** | Green | Targets grow faster each week |
| 4 | **Rush Job** *(was "Tight Deadline", which is already a boss name)* | Blue | −1 submission per round |
| 5 | **Ink Shortage** | Purple | −1 discard per round |
| 6 | **Heavy Printing** | Yellow | Desk Items cost +$1; rerolls start $1 higher |
| 7 | **Censored Press** | Orange | One random letter can't be played each round |
| 8 | **Final Print Run** | Gold | Boss Editors apply a second modifier |

---

## 6. Engine, infrastructure & simulation

| Feature | Notes | Status |
|---|---|---|
| **Headless core** (`src/Crossword.Core`) | Engine-agnostic rules, BCL only (enforced by an architecture test). Immutable state records, DAWG word graph, pooled Chips × Mult scoring with an `EffectEvent` log the UI animates from. | ✅ |
| **Balance simulator** | `RoundSimulator` / `RunSimulator` + CLI `sim` / `runsim`. Evaluating shop bot. Reports win rate, week reached, boss loss rates, unspent money. | ✅ |
| Simulator throughput | Target: **~10k runs in minutes** (agreed). Today a run takes ~0.5 s (move generation dominates), so 100 runs take ~1 minute on all cores. Get there by profiling + optimizing `MoveGenerator`/scoring (caching anchors and cross-checks, cutting allocations) and a `--fast` bot that only examines the top-N moves. 1k runs per tuning question is already enough statistically (±3 pts). | 🟡 |
| Simulator coverage | Per-deck and per-stake win rates, item pick/win rates, shop economy reports, target-curve sweeps across Weeks 1–5 (needs decks/stakes first). | 🟡 |
| **Local QA runner** | `run_local_qa.bat`: build → tests → game window (`--cli`, `--ci`, seed). UI self-test (`--selftest`) and screenshot flags. | ✅ |
| **Unit tests** | 251 xUnit tests incl. scoring edge cases, determinism and architecture rules. | ✅ |
| **CI pipeline** | GitHub Actions: build (warnings as errors) + `dotnet test` on every push. Later: headless Godot `--selftest`. | 🟡 |

---

## 7. Game modes & seeding

Everything random goes through the seeded `Rng`, so a seed reproduces a run exactly. That makes all of these
modes cheap to build on the core.

| Mode | Design (*proposed*) | Status |
|---|---|---|
| **Endless Mode** | After Week 5, targets ×2 per week (`RunConfig.EndlessGrowth`). Offered on the victory screen in the game and CLI. | ✅ |
| **Custom / seeded runs** | Seeds already work (`--seed=` dev flag; shown in the game footer). Planned: a seed entry box on the new-run screen and a copyable seed. Note: two players get the same boards, bosses and **first** shop, but later shop offers drift once their purchases differ (offers exclude owned items), as in Balatro. | 🟡 partial |
| **Daily Editorial** (daily seeded puzzle) | One seed per UTC date, one attempt per day, with a **local** best score and streak. Online leaderboards are out of scope for now. Name check: distinct from the "Daily" round, but keep the UI wording clear. | 🟡 |

---

## 8. Presentation, UI/UX & "juice"

| Feature | Design (*proposed*) | Status |
|---|---|---|
| **Visual design system: 1950s newsroom** | Newsprint paper canvas `#F6F4EE`, Printer's Ink `#111111`, Red Pen correction marks `#C53030`, mahogany desk surround. Replaces the current dark first-pass theme. Centralize colors in `UiKit` so the whole theme can be swapped in one place. Check text contrast (WCAG AA) and keep Chips/Mult distinguishable for colorblind players. **Don't use NYT branding** (name, masthead, fonts) in the game or any marketing; "NYT-style" is internal shorthand only. | 🟡 |
| **Audio** | Typewriter clacks on tile placement, pencil scribbles, rubber-stamp approval on round win, printing-press roll for big Mult. Sources must be CC0 or bought with a commercial license. | 🟡 |
| **Animation** | Tile placement, score count-up driven by the existing `EffectEvent` log, stamp/press effects scaled to Mult. | 🟡 (scoring playback ✅ first pass) |
| Shop card color-coding | Distinct colors per category: Desk Items / deck edits / Style Guides / Stationery. | 🟡 |
| Score breakdown tooltips | Hover a pending play to see its Chips × Mult math step by step (live preview ✅ already shows the totals). | 🟡 |
| Deck View & Style Guide levels modals | Popups that keep the board uncluttered: full deck with enhancements; tier levels with chips/mult. | 🟡 |

---

## 9. Persistence & meta-progression

| Feature | Design (*proposed*) | Status |
|---|---|---|
| **In-run save & resume** | Save on exit, resume on launch: board, hand, bag order, deck, Desk Items (with scaling state), shop offers and every RNG state. All state is immutable records, so this is mostly serialization: `System.Text.Json` (in the BCL, so Core rules allow it) with a **type discriminator** for polymorphic Desk Items/bosses/offers, plus a save **version number** for migrations. Hand arrangement is UI-only today; save it alongside. | 🟡 |
| **Profile & unlock tracking** | Local JSON profile: unlocked decks, dictionaries, item-pool additions, highest Press Run cleared per deck, stats. Kept separate from run saves. | 🟡 |

---

## 10. Suggested additions (not yet requested; Claude's recommendations)

- **Tutorial / onboarding:** with current targets an average player (sim skill 0.7) loses in Week 1 about 65% of the
  time, so a guided first round and better word hints would matter before external playtests.
- **Settings & accessibility:** volume, text size, colorblind-safe palette, reduced motion, key rebinding.
- **Localization:** UI text can be translated, but **gameplay in another language needs its own licensed word
  list and letter values**, which is a major project per language. Plan for English-only at launch.
- **Release legal checklist:** word-list licenses/credits, font and audio licenses, trademark sweep of all names.

---

## 11. Build order

Core phases run in order. The **parallel tracks** can be picked up between phases. Core features come with unit
tests and a `runsim` balance check.

| Phase | Work | Depends on |
|---|---|---|
| **0** ✅ | Retune week targets against the evaluating shop bot → 225/800/2400/6500/16000 (skill 0.9 wins ~39%) | — |
| **1** | Naming pass (boss and Style Guide renames) + tiered boss pools in `BossFor` | — |
| **2** | New Desk Items + new bosses (Redundant Copy, The Puzzle Master) | 1 |
| **3** | Stationery consumable system (state, shop, actions, UI) | — |
| **4** | In-run save/resume → profile & unlock tracking (§9) | — |
| **5** | Starting decks, dictionary overlays (+ denylist), Press Run stakes | 4 |
| **Release hygiene** | Slur denylist for every word list; "Qu" tile (a Q without U is a dead tile); legal checklist (§10) | before shipping |

| Parallel track | Work | Best time |
|---|---|---|
| Infra | CI pipeline (cheap, do early); simulator throughput work when sims become the bottleneck | anytime |
| Modes | Seed entry box for custom runs (small); Daily Editorial | seed box anytime; Daily Editorial after phase 4 |
| Presentation | Newsroom visual system → audio/animation → shop colors, tooltips, Deck View / Style Guide modals | after phases 1–3 settle the content |
| Onboarding | Tutorial and settings (§10) | before external playtests |

---

## 12. Open questions

- **Trademarks dictionary:** legal review before any work, or drop it.
- **Slang dictionary source:** find a word list we can legally ship (or build our own), and plan how to keep slurs out of it.
- **Vowel Drought:** keep the Vowel Tax effect, or rework it to fewer vowels in the bag?
- **Press Run effects:** confirm after playtesting; check each tier's difficulty step with `runsim`.
- **Early-game difficulty:** keep the harsh Week 1 (skill 0.7 loses ~65%), or soften it and let Press Runs carry
  the challenge? Decide after playtests.
- **Balance outliers** found by the evaluating shop bot: Pulitzer, Margin Notes and Word Count are picked in almost
  every run, and deck edits are never worth buying (see `handoff.md` §4).
