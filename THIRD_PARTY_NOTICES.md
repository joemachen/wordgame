# Third-party notices

Data shipped with wordgame that comes from other sources. Keep this file in step with anything added to
`src/Crossword.Core/Lexicon/Data/`, and reproduce the attributions in the game's credits screen before release.

## ENABLE word list

`src/Crossword.Core/Lexicon/Data/enable1.txt`: the ENABLE (Enhanced North American Benchmark Lexicon) word list.
It is in the public domain.

## Open English WordNet

`src/Crossword.Core/Lexicon/Data/definitions.tsv.gz` contains word definitions adapted from
**Open English WordNet 2025** (https://en-word.net, https://github.com/globalwordnet/english-wordnet), licensed under
the Creative Commons Attribution 4.0 International License (https://creativecommons.org/licenses/by/4.0/).

Changes made: limited to words in the ENABLE list, reduced to one or two senses per word, glosses shortened, and
inflected forms linked to their base words. The file also includes definitions written for this project
(`tools/Crossword.DefinitionsBuilder/supplement.txt`), which are not part of Open English WordNet.

Open English WordNet is derived from Princeton WordNet 3.0 (Princeton University, https://wordnet.princeton.edu),
used under the WordNet 3.0 license.
