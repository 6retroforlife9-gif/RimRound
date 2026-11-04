# RimRound Rework WIP

Substantially redesigned mod for RimWorld 1.6.

## Latest update - 4 November 2026

- **RimVore-2 and RJW compatibility.** The heavy RimRound rework now works with the custom **RimVore-2 1.16.1.3** companion and optional **RJW 6.2.2** on **RimWorld 1.6**.
- **Passenger space and digestion.** A living swallowed pawn temporarily takes up eating room. Transfers and release do not award calories. Eligible digestion contributes to RimRound's energy and weight system once, including after saving and loading.
- **Transfers and save/load.** The companion preserves the actual pawn and carried items through supported transfers and releases. Newly saved pending proposals retain their participants when reloaded.
- **Dispenser feeding fix.** A helper feeding a hungry patient can collect an ordinary serving when food is available but colony reserves prevent optional extra feeding. This fixes the reproduced waiting loop with adjacent or separated buildings.

Install the [custom RimVore-2 companion](CompanionMods/RimVore-2-1.16.1.3.zip) as a separate mod folder alongside RimRound. Its adapters are included. Load RimVore-2 after RimRound, then optional RJW. RJW is a separate download. Keep one installed copy of each mod.

Tested on **RimWorld 1.6.4871 rev591**; this update does not qualify 1.7 gameplay.

## Previous update - 2 November 2026

- **Blob beds and partners.** Guests can use a free spot beside a large supporting bed, get Masterwork-equivalent base rest and keep their sleeping direction after a reload. Assigned adult partners get the shared-bed mood benefit and can qualify for intimacy under the game's normal rules.
- **Optional Junk Food habit and snack policy.** The new colony setting defaults off. Turning it on adds one policy for all treats, covering recreation, craving relief, scheduled snacks and carrying. Pawns can develop a shared habit with mild cravings and occasional junk-only binges. Ordinary meals and assisted feeding still follow food restrictions.
- **Junk Food research and recipes.** Find Junk Foods on the RimRound research tab. All 189 recipes show whole-item ingredient costs. A large cola bottle lists 94 corn. Each recipe still consumes the same ingredients.
- **XL hoppers and food reserves.** XL hoppers can take selected prepared meals and reject hemogen packs even if an old filter allows them. Liquid-food reserves now account for which servings a pawn or helper can actually collect.
- **Body discussions and encouragement.** Colonists who cannot do Social work can now receive body-preference discussions and encouragement from a moral guide. The speaker or guide still needs Social capability.

Tested on **RimWorld 1.6.4871 rev591**, including cooking and feeding jobs, a discussion and a guide cast on a copy of an existing colony, and save/reload.

## Previous update - 1 November 2026

- **Junk Food is now included:** Oinky's Junk Food content is built into the RimRound rework, with 93 foods, 189 crafting recipes, its research, artwork and original credits. No separate Junk Food download is needed.
- **Compact, calorie-dense snacks:** junk foods provide more calories for the stomach space they occupy, making them useful for weight gain. They work with ordinary eating, shared meals and assisted feeding while respecting food restrictions and the recipient's limits.
- **Automatic unpacking:** pawns can open whole pizzas, large cola bottles and KitKat piles into servings when food is needed. They collect the servings for eating or feeding, and helpers can serve several portions in succession when the recipient can use more.
- **Android helpers:** capable Vanilla Races Expanded: Androids pawns can feed humans, and socially capable Androids can discuss another pawn's body preference. They remain exempt from RimRound eating and weight gain. Non-eating Androids no longer create impossible food-reserve requirements that pause optional meals.
- **NovaTech appearance fix:** the belt, collar, gloves and hyperbelt no longer produce an unwanted shirt outfit when worn. Their equipment bonuses remain intact, and ordinary clothing keeps its intended appearance.
- **Updated defaults:** the Body Perk Tree is enabled for new colonies. The default age range is 18–200, with body features enabled for male and female pawns, friendly and hostile NPCs, prisoners and slaves. Existing saved choices are respected; the old automatic increase from a 200-year maximum to 1000 is removed.
- **Food category icons:** restored the missing RimRound category icons in the resource list and storage filters.
- **Medical incapacitation:** fixed RimRound movement rules, hoverchairs and Legacy perks allowing pawns to walk despite paralytic abasia, coma or other incapacitating conditions. Loading also repairs stale saved mobility state. Valid native crawling and movement after recovery remain available.

Original documentation and a new integration changes document are included in the bundled folder.

Testing covered real cooking, automatic package opening, eating and feeding, saved settings, switching from the tested standalone Junk Food version, and save/reload. Android checks used **Vanilla Races Expanded: Androids 1.1.0**. Medical checks covered the base game and all five DLCs, including paralytic abasia, anesthesia, coma, pain shock and limb loss. This remains a **RimWorld 1.6 WIP**, tested on **1.6.4871 rev591**; 1.7 compatibility is unverified.

## Earlier update - 1 November 2026

- **Modded feast food and furniture:** feasts recognise suitable prepared meals, dining tables and seats through their game definitions. Multi-seat benches and supported serving storage are included. Food restrictions, ingredients, access, reservations and storage limits still apply.
- **Liquid meals in the ritual room:** a working supported liquid-food dispenser in the same room as the feast tables can supply meals. RimRound's classic and V2 networks, vanilla nutrient paste and Medieval Overhaul stew pots use their own food supplies and accounting.
- **Batch collection and consecutive servings:** collectors carry as many planned meals as their carrying capacity permits. Helpers can bring several portions for a diner and feed them one after another, including ordinary standard-size meals. Distinct ingredient, quality and food-effect combinations stay separate.
- **Generous meals for leaders and moral guides:** selected officers receive assisted meals and extra portions when food, helpers and their physical capacity allow. Bedbound officers can remain in their beds. Eligible immobile guests can be carried into the ceremony and returned afterwards; the aim is a generous meal.
- **Occasional Feeder visits:** Feeder specialists can bring extra bedside meals to willing bedbound leaders and moral guides pursuing an unreached gain goal, including after eating, feeding another pawn or sharing a meal. Food reserves, personal limits and restrictions still apply; hunger, urgent care and player orders take priority.
- **Prisoner and patient feeding:** fixed eligible bedside care rejecting the meal at the final bite because the recipient's Lose goal was waiting for greater hunger. Maintain's temporary Lose action is covered too. Ordinary hunger and food restrictions remain in force.
- **Embodied Abundance tastes:** newly initialised adult followers start with a positive taste for larger bodies. Existing saved tastes remain unchanged, and converting an adult does not reroll their taste.
- **Further fixes:** corrected temporary weight-effect reversals at body limits, delayed weight-change ordering, caravan state and cooldowns between games, and pawn-name text in body descriptions.

The feast checks used **Medieval Overhaul 1.6.2.2**, **Vanilla Cooking Expanded**, **Vanilla Furniture Expanded**, **Medieval Overhaul: Adaptive Storage** and **Adaptive Storage Framework**, with their required frameworks. Base-game feeding, minimal Ideology and targeted checks with all five DLCs were also covered. A controlled medieval colony completed twelve game days and two feasts; collection, assisted attendance, repeated servings and save/reload were checked in the recorded scenarios.

This remains a **RimWorld 1.6 WIP**, tested on **1.6.4871 rev591**. These checks do not establish universal mod compatibility, 1.7 support or long-session performance in large colonies. Unfamiliar custom dispensers or food effects may need their own integration. Keep backups before updating an existing colony.

## Previous update - 25 October 2026

- **Embodied Abundance and feasts:** Abundant Table is now a shared feast ritual available to any ideoligion. Embodied Abundance adds Ceremonial Feeding for a leader, moral guide or permanent Feedee in their own designated large RimRound single bed. Separate bedrooms and a shared ritual hall are supported.
- **Ritual beds and attendance:** the Ideology Buildings list now includes the Ritual bed, with its icon, name and build controls. Guests can use ordinary chairs, pews, kneel sheets or pillows. Selected helpers carry eligible immobile guests to the ceremony and return them to a suitable owned or medical bed afterwards. Selected food collectors gather real meals before dining, within carrying limits and food policies.
- **Three new specialists:** Feeders feed other pawns 25% faster; Feedees gain 20% more body mass from eligible positive ordinary-food gain; Provisioners produce 25% more eligible food at its source. Cooking, dispensing and trading do not award that food bonus again. Embodied Abundance also leaves room for two other eligible specialist types.
- **The large bed's bonus now works:** an eligible adult resting in it receives an extra 25% of base weight-gain conversion at Normal quality, rising to a maximum of 50% at Legendary. It works while awake or asleep, including bedside feeding, requires digested surplus energy and ends when the pawn gets up. Item information shows the actual percentage.
- **Kitchen workwear:** research Kitchen workwear after Complex Clothing, then make the kitchen work smock at either tailoring bench. It appears in clothing filters and grants a quality-based cooking-speed bonus: +15% at Normal and +30% at Legendary.
- **Equipment quality:** the six NovaTech garments now scale their special bonuses with quality while preserving Normal values. Both hoverchair appearances gain quality-based propulsion, from 55% at Awful through 65% at Normal to 80% at Legendary in Balanced/Fantasy. Legacy chair perks keep their own rules. Existing worn equipment updates without re-equipping.
- **Xenotype artwork override:** **Override xenotype body artwork** in **Body appearance** is a cosmetic override that replaces supported body/fur layers with the chosen RimRound art. It is **not a dedicated race adapter or compatibility patch** and does not make every race mod fully compatible. Custom body layers may remain visible, and heads, tails or wings may not line up with larger bodies. The option defaults off, is saved per pawn and can be switched off again. Genes and metabolism are unchanged.
- **Food suppliers:** new orbital and visiting food traders sell curated junk food, gain products, imported dense meals and occasional hoverchairs. Caravan stock travels on caloyaks. Food and consumables use the native Cheap price band; equipment keeps normal prices. Food-policy, drug and colony-preset restrictions still apply.
- **Further fixes:** repaired rejected-pawn reference cleanup and corrected the nutrient distillery's port overlay. Existing feeding, body, settings and vehicle fixes below are retained.

This is a **RimWorld 1.6 WIP**, tested on 1.6.4871 rev591. Keep backups before updating an existing colony. Testing does not establish compatibility with every save or mod list, or performance in large colonies.

## Previous update - 19 October 2026 (c24)

- **Vanilla nutrient paste:** repairs paste-only food-policy reserve checks, counts accessible ingredients in working hoppers once, and checks exact ingredient use when dispensing meals.
- **Food selection and collection:** rechecks prepared food during raw-food pickup and repairs liquid-outlet reservations, portion precision and stock accounting.
- **Shared and assisted meals:** keeps the 75%-useful-food safeguard active for optional Gain meals, including resumed shared jobs, with current food-policy and stock checks.
- **Malnutrition recovery:** stops nearly-full recovering pawns requesting unnecessary medical meals while keeping urgent feeding available when they are hungry.
- **Save and pawn references:** fixes retired Vanilla Expanded Framework group references, weight history between games, book-author ownership and incompatible Humanoid Alien Races tribal-chief biographies.
- **Inventory and research:** repairs inventory food-packing behavior and places kitchen-equipment research in the correct position.
- **Audio:** buffers RimRound's affected sound effects to address the reproduced streaming errors.

## Previous update - 17 October 2026

- **One settings window:** Options > Mod options > RimRound replaces the old bottom-bar settings tabs. The controls were audited and the layout checked from 720p through 4K with supported UI scales; 720p/1x is an extra compatibility case below the native resolution menu minimum.
- **Body and meal fixes:** missing clothing artwork keeps the correctly sized nude RimRound body; social/assisted feeding, reservations and interrupted collection are repaired. Suitable prepared meals take priority over raw food, with raw fallback when no suitable prepared serving is available.
- **Mobility:** only the extra weight-related rest drain is reduced: 75% in an operating hoverchair, and 100% while stationary, immobile/bedbound, carried or seated in a vehicle. Normal rest needs still apply.
- **Vehicles:** repaired the obsolete Vehicle Framework food hook and checked actual Vanilla Vehicles Expanded travel, passenger feeding and occupied-caravan reload in the tested framework versions.
- **Automatic stage benefits:** Obese+ natural/tended injury recovery reaches 110% of the ordinary rate after compensating for enlarged body-part health. Lardy+ prevents new food poisoning. Both benefits follow the current eligible adult weight stage and disappear below their thresholds; existing poisoning continues normally.
- **Defaults:** male/female bodies and friendly/hostile/prisoner/slave eligibility are enabled at ages 18-1000. Existing choices are retained, with a one-time migration of the old 200-year upper limit. This historical default and migration are superseded by the 1 November update above.
- **Settings repairs:** feeder flow, prisoner feeding resume, separate mood/target controls, cooldown validation and feeder interval rounding now use the audited settings behavior; obsolete controls are hidden.

## Previous update - 15 October 2026

**Anthrosonae is now fully supported.** Base-mod adult appearances retain their coats, colours, head features and fitted tails/wings across supported RimRound body artwork and weight stages. Third-party addon content retains native fallback.

This build also includes reworked food density and digestion, complete Gain meals, and updated food machinery etc etc etc tons of things dig deep into the code if you like :P

Anthrosonae:
https://steamcommunity.com/sharedfiles/filedetails/?id=2902258418
