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

## 6. Build order

Each phase builds on the ones before it. Core features come with unit tests and a `runsim` balance check.

| Phase | Work | Depends on |
|---|---|---|
| **0** | Retune week targets against the evaluating shop bot (it wins ~97% at skill 0.9 with the current targets). *Waiting on user approval.* | — |
| **1** | Naming pass (boss and Style Guide renames) + tiered boss pools in `BossFor` | — |
| **2** | New Desk Items + new bosses (Redundant Copy, The Puzzle Master) | 1 |
| **3** | Stationery consumable system (state, shop, actions, UI) | — |
| **4** | Save/load → meta-progression profile (tracks unlocks) | — |
| **5** | Starting decks, dictionary overlays (+ denylist), Press Run stakes | 4 |
| **Release hygiene** | Slur denylist for every word list; "Qu" tile (a Q without U is a dead tile) | before shipping |

---

## 7. Open questions

- **Trademarks dictionary:** legal review before any work, or drop it.
- **Slang dictionary source:** find a word list we can legally ship (or build our own), and plan how to keep slurs out of it.
- **Vowel Drought:** keep the Vowel Tax effect, or rework it to fewer vowels in the bag?
- **Press Run effects:** confirm after playtesting; check each tier's difficulty step with `runsim`.
- **Balance outliers** found by the evaluating shop bot: Pulitzer, Margin Notes and Word Count are picked in almost
  every run, and deck edits are never worth buying (see `handoff.md` §4).
