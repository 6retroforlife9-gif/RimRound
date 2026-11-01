# Junk Food for RimWorld 1.6 and the RimRound redesign

Oinky's original 93 foods and artwork, updated as a separate optional add-on.
Requires Harmony and the current RimRound redesign with its companion assembly.
Use only one copy of `Oinky.JunkFood`; this replaces the original 1.5 package.

## Food and crafting

Research **Junk Foods** (500 points), then use a fueled or electric stove. Drinks
and frozen desserts also have brewery recipes. Cooking requirements range from
3 to 6. Recipes use rice/corn, berries/agave, milk, native chocolate and, where
appropriate, potatoes or meat/fish. No flour, sugar, oil or cheese production chain
is needed. Each ingredient group uses a single type per job.

There are 90 ordinary foods with single and four-portion recipes and three large
packages. Six additional historical batch sizes preserve old bills, for 189 total
production recipes. Existing recipe IDs retain their original output counts.
Opening a whole pizza yields four slices, a large cola six drinks, and a KitKat pile
four bars. Select a colonist and right-click the package to open it. This operation
retains age, ingredients, poisoning and infection ownership; it does not cook food.

Nutrition per item and calories per stomach space meet or exceed the original
add-on's values. Density ranges from 4 to 7.5 times an ordinary meal. RimRound
handles digestion and the resulting energy balance. Food does not apply an extra
weight-gain pulse. No food rots sooner than four normal-temperature days; freezing
pauses ordinary rot. Milk-heavy treats cost more to supply than crop-only foods.

## Colony settings and availability

Open **Options → Mod settings → Junk Food** while a colony is loaded. Both switches
belong to that save:

- **Enable Junk Food** controls new crafting, trader stock, gifts, cargo and junk
  recreation. Existing food stays edible and opening existing packages remains
  available. Existing bills and already-consumed calories are retained.
- **Enable mild cravings** controls the optional habit. Adults consuming at least
  0.45 useful junk nutrition on three days within five may develop a habit. After
  48 hours without a qualifying snack, it gives only −2 mood. Four days of abstinence
  clear the habit. Switching cravings off clears it immediately.

Snacks share one +3 taste memory and the normal Gluttonous recreation tolerance.
Savoury meals use the normal fine-meal memory instead. No chemical dependency,
intoxication, forced binge or child weight-gain logic is added.

RimRound food caravans and orbital suppliers carry rotating selections across
snacks, drinks and richer foods. Gifts can contain snacks within the normal gift
budget. Resource-pod crashes have a 10% chance of junk cargo within the normal
150–600 silver budget; this does not increase the event's frequency. Selection
chooses families before brands, so adding varieties does not multiply their chance.

These foods are excluded from both RimRound liquid-processing systems, which
cannot yet preserve all their eating effects.

## Candidate and testing

Compatibility depends on the exact RimRound redesign build. Use an isolated test
profile and a copied save. The workspace's `docs/JUNK_FOOD_IMPLEMENTATION_PLAN.md`
and `docs/JUNK_FOOD_NATIVE_RESULTS.md` record the native matrix, completed results,
tested package identities and remaining scope limits. Compilation alone does not
establish native job flow, migration or long-term balance.

Original food, recipe and legacy chemical/gene IDs are retained for migration.
The obsolete chemical habit is retired in favour of optional mild cravings.
The native results identify which serialized legacy states were tested; they do
not certify every historical player save. Turning the feature off is different
from uninstalling the package from a save.

## Rebuilding

The authored catalogue is `tools/balance.json`. Run `python tools/generate_content.py`
then `python tools/validate_content.py --game <RimWorld-1.6-root>`. Build
`Source/JunkFood.csproj` against the exact game, Workshop Harmony and frozen
RimRound `1.6` reference directory, with private output/intermediate paths.
`tools/package.py` accepts the built DLL, reference manifest and a fresh destination.
It never overwrites an existing candidate. Source, references and package manifests
make the candidate reproducible. See `CREDITS.txt` for original attribution.
