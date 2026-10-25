# RimRound Rework WIP

Substantially redesigned mod for RimWorld 1.6.

## Latest update - 25 October 2026

- **Embodied Abundance and feasts:** Abundant Table is now a shared feast ritual available to any ideoligion. Embodied Abundance adds Ceremonial Feeding for a leader, moral guide or permanent Feedee in their own designated large RimRound single bed. Separate bedrooms and a shared ritual hall are supported.
- **Ritual beds and attendance:** the Ideology Buildings list now includes the Ritual bed, with its icon, name and build controls. Guests can use ordinary chairs, pews, kneel sheets or pillows. Selected helpers carry eligible immobile guests to the ceremony and return them to a suitable owned or medical bed afterwards. Selected food collectors gather real meals before dining, within carrying limits and food policies.
- **Three new specialists:** Feeders feed other pawns 25% faster; Feedees gain 20% more body mass from eligible positive ordinary-food gain; Provisioners produce 25% more eligible food at its source. Cooking, dispensing and trading do not award that food bonus again. Embodied Abundance also leaves room for two other eligible specialist types.
- **The large bed's bonus now works:** an eligible adult resting in it receives an extra 25% of base weight-gain conversion at Normal quality, rising to a maximum of 50% at Legendary. It works while awake or asleep, including bedside feeding, requires digested surplus energy and ends when the pawn gets up. Item information shows the actual percentage.
- **Kitchen workwear:** research Kitchen workwear after Complex Clothing, then make the kitchen work smock at either tailoring bench. It appears in clothing filters and grants a quality-based cooking-speed bonus: +15% at Normal and +30% at Legendary.
- **Equipment quality:** the six NovaTech garments now scale their special bonuses with quality while preserving Normal values. Both hoverchair appearances gain quality-based propulsion, from 55% at Awful through 65% at Normal to 80% at Legendary in Balanced/Fantasy. Legacy chair perks keep their own rules. Existing worn equipment updates without re-equipping.
- **Xenotype artwork override:** **Override xenotype body artwork** in **Body appearance** is a cosmetic override that replaces supported body/fur layers with the chosen RimRound art. It is **not a dedicated race adapter or compatibility patch** and does not make every race mod fully compatible. Custom body layers may remain visible, and heads, tails or wings may not line up with larger bodies. The option defaults off, is saved per pawn and can be switched off again. Genes and metabolism are unchanged.
- **Food suppliers:** new orbital and visiting food traders sell curated junk food, gain products, imported dense meals and occasional hoverchairs. Caravan stock travels on caloyaks. Food and consumables use the native Cheap price band; equipment keeps normal prices. Food-policy, drug and colony-preset restrictions still apply.
- **Further fixes:** repaired rejected-pawn reference cleanup and corrected the nutrient distillery's port overlay. Existing feeding, body, settings and vehicle fixes below are retained.

This is a **RimWorld 1.6 WIP**, tested on 1.6.4871 rev591. New colonies and their later saves are the main target; keep backups before updating an existing colony. Testing does not establish compatibility with every save or mod list, or performance in large colonies.

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
- **Defaults:** male/female bodies and friendly/hostile/prisoner/slave eligibility are enabled at ages 18-1000. Existing choices are retained, with a one-time migration of the old 200-year upper limit.
- **Settings repairs:** feeder flow, prisoner feeding resume, separate mood/target controls, cooldown validation and feeder interval rounding now use the audited settings behavior; obsolete controls are hidden.

## Previous update - 15 October 2026

**Anthrosonae is now fully supported.** Base-mod adult appearances retain their coats, colours, head features and fitted tails/wings across supported RimRound body artwork and weight stages. Third-party addon content retains native fallback.

This build also includes reworked food density and digestion, complete Gain meals, and updated food machinery etc etc etc tons of things dig deep into the code if you like :P

Anthrosonae:
https://steamcommunity.com/sharedfiles/filedetails/?id=2902258418
