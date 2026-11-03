# Junk Food for RimWorld 1.6 and the RimRound redesign

RimRound includes Oinky's 93 foods and their original artwork. Enabling RimRound
loads the bundled content. The original credits are in `CREDITS.txt`.

## Food and crafting

Research **Junk Foods** on the **RimRound** tab (500 points), then use a fueled
or electric stove. Drinks and frozen desserts also have brewery recipes. Cooking
requirements range from 3 to 6. Recipes use rice or corn, berries or agave, milk,
native chocolate and, for some foods, potatoes, meat or fish. Each ingredient
group uses one type per job. Recipe tooltips show whole-item costs. For example,
a large cola bottle takes 94 corn.

There are 90 ordinary foods with single and four-portion recipes and three large
packages. Six additional historical batch sizes preserve old bills, for 189 total
production recipes. Existing recipe IDs retain their original output counts.
Opening a whole pizza yields four slices, a large cola six drinks, and a KitKat pile
four bars. Pawns can open packages when looking for food. To open one manually,
select a colonist and right-click the package. Opening retains age, ingredients,
poisoning and infection ownership.

Nutrition per item and calories per stomach space meet or exceed the original
add-on's values. Density ranges from 4 to 7.5 times an ordinary meal. RimRound
handles digestion and the resulting energy balance. Food does not apply an extra
weight-gain pulse. No food rots sooner than four normal-temperature days; freezing
pauses ordinary rot. Milk-heavy treats cost more to supply than crop-only foods.

## Colony settings and availability

Open **Options → Mod settings → Junk Food** while a colony is loaded. These
settings are saved with that colony.

- **Enable Junk Food** controls new crafting, trader stock, gifts, cargo and
  optional snacks. Existing food stays edible and opening existing packages remains
  available. Existing bills and already-consumed calories are retained.
- **Enable mild cravings** controls habit acquisition and craving effects. With
  the shared policy off, adults consuming at least 0.45 useful junk nutrition on
  three days within five may develop the existing mild habit. It can give a −2
  craving after two days. Four days of abstinence clear the habit.
- **Use shared Junk Food addiction and snack policy** defaults off. Turn it on
  to group all treats in one drug-policy row for recreation, habit relief,
  scheduled snacks and carrying. Adults can develop a shared habit with
  −1/−2/−3 cravings and occasional junk-only binges. Treats give modest
  chemical-interest credit and are exempt from anti-drug beliefs and
  teetotaler restrictions.

Ordinary meals and assisted feeding use food restrictions in both modes. Turning
the shared setting on or off clears the outgoing habit. A save without the new
setting keeps its existing eating history in the default mode.

Snacks share one +3 taste memory and the normal Gluttonous recreation tolerance.
Savoury meals use the normal fine-meal memory instead.

RimRound food caravans and orbital suppliers carry rotating selections across
snacks, drinks and richer foods. Gifts can contain snacks within the normal gift
budget. Resource-pod crashes have a 10% chance of junk cargo within the normal
150–600 silver budget; this does not increase the event's frequency. Selection
chooses families before brands, so adding varieties does not multiply their chance.

These foods are excluded from both RimRound liquid-processing systems, which
cannot yet preserve all their eating effects.

## Saves and testing

Original food and recipe IDs are retained. Checks on RimWorld 1.6.4871 rev591
covered ordinary eating and assisted feeding in both modes, optional snacks
and carrying, and reloading saves that lack the shared-policy setting.

## Rebuilding

In the development workspace, start in `Addons/JunkFood`.
The authored catalogue is `tools/balance.json`. Run `python tools/generate_content.py`
then `python tools/validate_content.py --game <RimWorld-1.6-root>`. Build
`Source/JunkFood.csproj` against the exact game, Workshop Harmony and frozen
RimRound `1.6` reference directory, with private output/intermediate paths.
`tools/package.py` accepts the built DLL, reference manifest and a fresh destination.
It never overwrites an existing candidate. Source, references and package manifests
make the candidate reproducible. See `CREDITS.txt` for original attribution.
