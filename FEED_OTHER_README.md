# RimRound 1.6 Feed Other and Reliability Update

Current build: **v1.0.69.22**

This update is integrated into the RimRound 1.6 mod tree. It keeps the existing
RimRound package ID, saved definitions and building identities while adding the
`RimRoundFeedOther.dll` companion assembly and its supporting definitions.

## v1.0.69.22 release note

- Autonomous underweight idle eating is now restricted to player-faction
  humanlike colony pawns.
- Visitors, quest guests, prisoners and enemies no longer receive the scheduled
  eat-to-fullness job through idle or wander think nodes.
- A pawn who stops belonging to the colony cannot continue looping through a
  previously created idle-eating stack.
- Full feeding-stack collection, concentrated-food portion clamps and the
  complete Food Network reliability fixes are included.
- Temporary beta testing notes and generated source-diff files have been removed
  from the mod root. This file is the maintained feature guide; `changelog.txt`
  contains the consolidated release history.

## Feed Other and Share Meal

- Pawns can choose Share Meal or Feed Other as social recreation.
- Current spouses, fiances and lovers are preferred before other valid partners.
- A player-controlled pawn can be ordered to feed a non-hostile humanlike pawn
  through the right-click menu.
- Manual feeding can wake an ordinary sleeper while keeping the recipient in
  their existing bed. Downed or immobile recipients retain their patient job.
- Mobile recipients use a nearby dining chair when practical and fall back to a
  safe standing interaction when no chair remains available.
- Each eater collects the full calculated allocation on the initial trip.
  Prepared meals are then consumed one serving per round with a live fullness
  check after every meal.
- Follow-up food collection is allowed only from a nearby source once the
  feeding session has begun, preventing repeated long storage trips.
- Food selection respects policies, reservations, reachability, forbidden food,
  social propriety, dietary thoughts, concentration and the configured target.
- A planned automatic portion may waste no more than 1.0 nutrition at the active
  target. Smaller stackable portions are selected when possible.
- Successful sessions may continue as post-meal social recreation. Romantic
  partners display visual heart flecks during that conversation without invoking
  Lovin', pregnancy or Lovin' memories.

## Idle underweight eating

Idle Eat to Fullness is available only when all of the following are true:

- The pawn is a spawned, awake, undrafted and mentally stable player-faction
  humanlike.
- The pawn has working Food and Mood needs and an enabled RimRound fullness
  component.
- The pawn is not personally or categorically exempt.
- Weight-opinion moodlets are enabled.
- The pawn has Neutral+ or higher weight opinion and is currently receiving a
  negative mood stage for being too thin.
- Their fullness is below the configured idle-eating trigger, 25% of current
  hard stomach capacity by default.
- Their current job is an interruptible idle or wander job.

The pawn uses normal food selection but eats the selected safe stack where it is
collected. Only this dedicated idle action suppresses `Ate without table`;
ordinary hunger jobs and player-forced meals retain normal table behaviour.

## Food Network v2

- Processors, pipes, valves, tanks, dispensers, automatic feeders and nutrient
  distillers use map-local network state.
- Stored food is represented as FIFO batches preserving nutrition, physical
  fullness volume, concentration and ingredients.
- Processor, dispenser and distiller transactions either complete or roll back;
  blocked output does not consume or duplicate food.
- Existing tank amounts migrate into the new network state on first load.
- XL hoppers use the vanilla nutrient-paste hopper food category: raw food only,
  including vanilla special-filter exclusions.
- Food pipes remain hidden in the world while blueprints, frames and the network
  overlay remain visible.
- Valves use the familiar power-switch Architect icon and split or reconnect
  networks immediately.
- Tanks report stored nutrition, fullness volume, concentration, batch count and
  ingredients.
- Pawn-operated dispensers create genuine 0.90-nutrition nutrient-paste meals.
  The pawn collects enough complete meals for the active diet-bar target and
  available carry space.
- Food Network paste is not registered as an ordinary edible building in
  vanilla's global food-source lists, avoiding failures in wardening, patient
  feeding and unrelated food searches.
- Distillers use separate rotation-aware input and output ports. Same-network
  loops are blocked, nutrition is conserved, and only fullness volume changes.

## Automatic feeders and beds

- Basic automatic feeders retain one adjacent humanlike bed.
- Advanced feeders retain several beds within the configured hose range and bed
  limit.
- Bed links persist while empty. Tube conditions follow each current occupant
  and are cleaned when the pawn, bed or feeder leaves the valid link.
- Both occupants of a double bed are tracked, locked, displayed and fed
  independently.
- Listen to Radio recreation no longer reserves a shared double bed as a
  one-person chair.
- Survival and Maintain use a conservative nutrition pulse.
- Gain and Maximum Gain use a configurable percentage of the pawn's hard stomach
  capacity per pulse, independent of liquid concentration.
- The default Gain flow is 10% capacity per pulse. Maximum Gain defaults to a
  90% target and retains the 95% rupture-safety ceiling.
- RimRound's 3x3 `NotRegalBed` remains a one-person bed with one centred,
  rotation-aware sleeping lane and one normal bed owner.
- The oversized bed supports normal assignment, medical use, rescue, tending,
  childbirth, facilities, feeder links and save migration.
- Its south/downward visual uses the corrected opposite texture slot so the
  headboard covers the sleeper correctly.

## Prisoners

- Maintain/default interactions deliver one controlled serving every two
  in-game hours while the prisoner is hungry.
- Fatten assigns a valid prisoner bed, prevents uncontrolled self-feeding and
  direct meal delivery, and uses one safe serving at a time.
- Fatten stops at exactly 80% of current hard stomach capacity.
- Once that target is reached, the next Fatten cycle remains latched off until
  fullness falls strictly below 10%.
- The final serving is clamped to the target, and concentration-aware food
  selection retains the 1.0 maximum projected nutrition-waste rule.

## Other integrated fixes

- Newly generated pawns receive balanced weight opinions plus one eating-style
  and one stomach-elasticity category without consuming ordinary vanilla trait
  slots.
- Player-controlled adults can attempt one-step weight-opinion changes through
  configurable social abilities.
- Hoverchairs move to personal inventory when the wearer is downed and
  automatically re-equip when the wearer safely recovers.
- Orbital maps apply reduced RimRound weight/fullness movement penalties.
- Gluttonium uses an additional generation pass so it does not replace vanilla
  surface ore selections, and its ore supports the vanilla Mine Vein command.
- Player-controlled lactating pawns have an optional saved automatic milk
  expression toggle.
- A seven-page RimRound Patch settings window controls feeding, social,
  prisoner, idle-eating, Food Network and reliability behaviour.

## Important defaults

| Setting | Default |
| --- | ---: |
| Autonomous feeding start limit | 50% hard fullness |
| Feed Other target | 70% hard fullness |
| RimRound Very Full stage | 70% hard fullness |
| Idle underweight trigger | Below 25% hard fullness |
| Participant cooldown | 6 in-game hours |
| One-way feeder cooldown | 3 in-game hours |
| Same-pair cooldown | 6 in-game hours |
| Prisoner Fatten target | 80% hard fullness |
| Fatten reset threshold | Below 10% hard fullness |
| Gain feeder flow | 10% capacity per pulse |
| Maximum Gain target | 90% hard fullness |
| Feeder safety ceiling | 95% hard fullness |
| Maximum projected nutrition waste | 1.0 nutrition |

Like-or-higher pairings bypass autonomous feeding cooldown checks while the
recipient still has to satisfy the configured start-fullness rule. Manual
right-click feeding bypasses autonomous cooldowns.

## Installation

### Full repository

Place the complete repository checkout in the RimWorld `Mods/RimRound` folder.
The mod root must directly contain `About`, `1.6`, `Textures` and
`loadFolders.xml`.

### Update overlay

Extract the update archive directly over an existing matching RimRound folder
and allow files to merge and overwrite. Do not leave an extra version-named
directory between `RimRound` and `1.6`.

Back up important saves and fully restart RimWorld after replacing assemblies or
textures.

## Building

The companion project is:

`Source/RimRoundFeedOther/RimRoundFeedOther.csproj`

It targets .NET Framework 4.8 and expects the RimRound, RimWorld, Harmony and
Unity reference assemblies already present under the RimRound 1.6 tree. A
Release build writes `RimRoundFeedOther.dll` to `1.6/Assemblies`.

## Save compatibility

- Existing Feed Other settings, cooldowns, pawn toggles, feeder modes, bed links
  and Food Network state keep their saved identities.
- Existing Food Network tanks migrate without changing their building defNames.
- Existing oversized beds retain their building identity and reconcile stale
  ownership or sleeping positions on load.
- Removing this update requires a clean matching RimRound installation; copying
  an older DLL alone does not remove definitions introduced by the update.
