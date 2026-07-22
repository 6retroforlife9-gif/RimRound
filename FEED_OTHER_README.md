# RimRound Feed Other v1.0.69-beta.10 — Corrected south-facing oversized-bed texture slot

## Current development status

**Current stable build:** v1.0.60 Manual sleeping-recipient bed preservation  
**Current beta build:** v1.0.69-beta.10 Corrected south-facing oversized-bed texture slot

This beta continues from v1.0.69-beta.1 and still branches from the fully working v1.0.60 package. The v1.0.60 ZIP remains the stable package.

To roll back after installing the beta, first delete these beta-only files, then extract v1.0.60 over the RimRound folder:

- `1.6/Defs/TraitDefs/RimRound_FeedOtherBetaTraitDefs.xml`
- `1.6/Patches/RimRound_FeedOtherBetaTraitPatches.xml`
- `Source/RimRoundFeedOther/RimRoundTraitGenerationBeta.cs`
- `Source/RimRoundFeedOther/PostMealSocialUtility.cs`
- `1.6/Defs/GenStepDefs/RimRound_GluttoniumAdditionalLumps.xml`
- `1.6/Patches/RimRound_GluttoniumOreCompatibility.xml`
- `1.6/Defs/RimRound_FeedOtherMainButtonDef.xml`
- `Source/RimRoundFeedOther/FeedOtherSettings.cs`
- `Source/RimRoundFeedOther/MainTabWindow_FeedOtherSettings.cs`
- `Source/RimRoundFeedOther/GluttoniumGenerationSettingsPatch.cs`
- `1.6/Patches/RimRound_FoodNetworkV2.xml`
- `Source/RimRoundFeedOther/FoodNetworkV2Core.cs`
- `Source/RimRoundFeedOther/FoodNetworkV2Machines.cs`
- `Source/RimRoundFeedOther/FoodNetworkV2Serving.cs`
- `Source/RimRoundFeedOther/FoodNetworkV2AutoFeeders.cs`
- `1.6/Patches/RimRound_NotRegalBedReliability.xml`
- `Source/RimRoundFeedOther/NotRegalBedReliabilityFix.cs`
- `Textures/UI/Buttons/MainButtons/RRFeedOtherSettingsButton.png`

The stable archive then restores the DLL and all modified stable files. Removing the beta-only XML files is necessary because an ordinary archive overwrite does not delete files that exist only in the beta. To restore all original oversized-bed artwork after beta.10, verify or reinstall the original mod files through Steam before reinstalling the stable patch.

## Corrected south-facing oversized-bed texture

- Beta.9 wrote the rotated artwork into RimRound's `south` texture slot, which is the slot this bed displays for the visually north-facing orientation. Beta.10 restores that slot from the original RimRound texture and mask.
- The original working `south`-slot artwork is rotated exactly 180 degrees and written into the `north` texture slot, which this bed displays when its head points visually south/downward.
- The material mask is restored and rotated in the same way, preserving stuff colour and shader behaviour.
- The visually south-facing bed now places its solid headboard over the sleeper's head, while the visually north-facing bed is returned to its original appearance. East and west artwork, sleeping-cell geometry, ownership and automatic-feeder behaviour are unchanged.

## Oversized single-bed reliability

- RimRound's `NotRegalBed` remains a physically 3x3 bed for one pawn. Its sleeping and foot cells are now centred on the correct lane in north, east, south and west rotations instead of applying the original east-only offset.
- The original generic assignment component is replaced with RimWorld's real bed-specific ownership behaviour while remaining capped at one owner. Colonist/slave eligibility, bedroom ownership, assignment, unassignment, reinstalling and room notifications therefore follow normal bed rules.
- Legacy saved assignments are reconciled safely. A valid unowned saved assignment becomes the bed owner; stale assignments to pawns who already own another bed are discarded rather than stealing that ownership.
- New beds start as ordinary owner-assignable beds instead of being forced medical. The normal medical toggle, prisoner/slave controls, healing, comfort, quality, facilities and RimRound resting-weight bonus remain available.
- Normal `PassThroughOnly` bed pathing replaces the standable-furniture workaround, preventing pawns from treating the unused eight footprint cells as chairs or ordinary standing positions.
- A dedicated 3x3 perimeter interaction pattern supports tending, rescue, childbirth, feeding and other bedside work. It replaces the vanilla interaction search that throws for every bed deeper than two cells.
- Head-only facilities use the one centred sleeping position instead of treating the bed as three separate sleepers.
- When an old save loads, a pawn whose active lying job targets this bed is moved from the former incorrect tile to the correct centred slot. Normal future jobs use the repaired slot directly.
- Food Network v2 continues linking to the persistent bed object, so the basic/advanced automatic feeder, tube lock and cleanup rules remain unchanged.

## Food Network v2 overhaul

- The food processor, pipes, valves, tanks, dispenser, automatic feeders and nutrient distiller now use a standalone map-level network instead of RimRound's original shared-float implementation. Topology is rebuilt only when a connector changes, not on every game tick.
- Storage consists of conserved FIFO batches. Every batch keeps exact nutrition, exact physical fullness volume, nutrition density and ingredient definitions. Compatible adjacent batches are compacted to prevent long-running colonies from accumulating excessive save entries.
- Store, draw and distillation operations are transactional. A processor consumes feedstock only after exact output capacity is reserved; a failed dispenser carry returns its serving; and a failed distiller output restores the original input batch.
- Existing tanks migrate their saved volume and density once. Existing defNames, research, buildings and the first legacy feeder target remain compatible. The master setting can return to classic RimRound and safely re-import the live classic tank amounts when v2 is enabled again.
- Food processors respect each attached hopper's storage filter, accept raw plant food, optionally accept prepared meals, preserve ingredient contamination, process exact whole stack counts and show a specific blocked status.
- Tanks show network nutrition, fullness volume, capacity, density, FIFO batch count and ingredients. Separate confirmed commands purge one tank or the entire connected network.
- Pawn-operated food dispensers create one genuine vanilla nutrient-paste meal at collection time. Its per-instance component carries the network batch's exact nutrition and density, so several pawns can safely eat differently sized paste meals from differently distilled networks without mutating a shared ThingDef.
- Basic automatic feeders retain one adjacent humanlike bed. Advanced feeders retain several beds within the configurable hose range and bed limit. Empty linked beds remain linked; tube hediffs follow current occupants and are removed when an occupant leaves, changes maps, dies, or the feeder is removed.
- Feeder Survival, Maintain, Gain and Maximum Gain modes use real serving ingestion, the configurable feeding target, the 95% rupture ceiling, live power/breakdown/network checks, ingredients and RimRound's normal fullness/mood handling. The basic feeder is restricted to Survival and Maintain.
- The distiller has separate rotation-aware blue input and red output ports. It refuses a same-network loop, conserves nutrition and ingredients, changes only fullness volume/density, and displays both network states in its inspector.
- Pipe overlays connect only members of the same live network. Valve changes immediately dirty topology and graphics. Selecting the distiller displays its two coloured ports.
- A seventh **Food network** settings page controls the overhaul, prepared meals, processor/auto-feeder/distiller amounts, separate maintenance and gain-mode feeder flow, feeder interval, Maximum Gain target, advanced range/bed limit and overlay visibility. Tank spoilage remains disabled for this beta.

## Food dispenser and valve follow-up

- Food Network v2 no longer inserts its non-ingestible dispenser building into `FoodUtility.BestFoodSourceOnMap`. That global result is also used by wardening, patient feeding, inventory transfer and several modded think nodes which legitimately assume it contains an edible Thing; returning a building there caused the repeated `ThinkNode_PrioritySorter.TryIssueJobPackage` null-references reproduced by the Red Basin test save.
- Vanilla now completes its ordinary self-feeding search unchanged. Only `JobGiver_GetFood` then checks for a ready Food Network v2 dispenser, compares it with a normal ingest job, and substitutes the explicitly supported dispenser job when paste is the better choice.
- A ready dispenser is compared with ordinary food using its paste-meal preference and walking distance. If vanilla has no ordinary food job, the same guarded step creates the dispenser ingest job directly.
- Non-ingest jobs such as harvesting, hunting, hopper filling and taking food from another inventory are never replaced. If another mod produces a food job that cannot be scored safely, that job is preserved.
- The pawn walks to the dispenser's interaction cell without exclusively reserving the machine, withdraws one diet-bar-sized paste meal, carries it, eats it through normal ingestion and returns the serving to the network if pickup fails.
- Candidate and job-selection checks fail safely instead of cancelling the pawn's entire think cycle. Food policy, power, switch state, breakdown, faction, forbidden state, social propriety, reachability and network supply remain respected.
- The feeding-tube valve now uses the vanilla power-switch artwork in the Architect menu instead of the power-conduit icon, making its function visually distinct from an ordinary pipe.

## Double-bed automatic-feeder reliability

- Food Network v2 now treats every occupant of a linked bed as an independent connection. RimRound's legacy `_currentPawn` field is cleared while v2 is active instead of making the first sleeper authoritative.
- Both sleeping slots of a double bed receive the movement-lock condition, their own feeding checks and their own visible hose. Empty beds remain linked and connect each later occupant normally.
- Advanced-feeder **Listen to radio** recreation no longer reserves the pawn's bed cell as a one-person chair. It reserves only a normal sleeping slot, so two occupants can remain in the same bed without `TryMakePreToilReservations` errors.
- A connected pawn who briefly loses bed posture because a job is interrupted is restored to `LayDown` without being moved from their sleeping cell. A pawn genuinely removed from the bed, carried away, transferred to another map, killed, or disconnected still has the tube condition cleaned at the next feeder check.
- Feeder power, food and mode do not release linked occupants. **Unlink beds**, destruction of the bed/feeder, or physically removing the pawn from the bed remains the release path.

## Diet-bar nutrient-paste dispenser

- A pawn now targets the dispenser building, waits through vanilla's 50-tick collection action, receives a genuine `MealNutrientPaste`, carries it to a normal eating spot/table and ingests it through the ordinary nutrient-paste path.
- Nutrition mode dispenses the amount needed to raise current food plus digesting nutrition to the upper nutrition-bar marker. Fullness mode dispenses the network-density-adjusted amount needed to reach the upper fullness marker. Hybrid mode uses its nutrition marker to trigger eating and its fullness marker to size the paste meal. Disabled diet mode uses vanilla `NutritionWanted`.
- Meal size is calculated only when the pawn reaches the dispenser, so changes caused by walking, digestion or another pawn using the network cannot leave a stale pre-planned portion.
- If the network has less food than the requested target, the dispenser creates one smaller conserved paste portion instead of refusing to serve or inventing nutrition. The pawn may return later if their dietary trigger still calls for food.
- The paste meal preserves FIFO ingredient history, dietary contamination and exact fullness density. Ordinary nutrient-paste thoughts, food policy, ideology checks, table behaviour, eating speed and ingredient reactions now apply naturally.
- Vanilla-style non-exclusive access allows several pawns to approach the same dispenser. Each actual collection is an atomic network draw, so the last pawn safely receives a smaller meal or an incompletable job if the earlier pawns emptied the network.
- The previous serving-size value remains save-compatible and is now labelled **Survival/Maintain nutrition per pulse**. Gain and Maximum Gain instead use a physical fullness-flow percentage, while pawn-operated dispensers continue sizing one paste meal directly from the eater's bars.

## Density-independent automatic-feeder flow

- Gain and Maximum Gain no longer cap each pulse at a fixed nutrition amount. That made twice-distilled liquid, with three nutrition per fullness volume, raise a pawn's fullness only one third as quickly as ordinary liquid.
- Each gain-mode pulse now pumps a configurable percentage of the recipient's hard stomach capacity. The default is 10%, so an empty pawn approaches the default 90% Maximum Gain target in roughly nine checks (about 30 seconds at the default interval).
- The FIFO transaction calculates the nutrition required from the live batch density. Ordinary liquid and distilled liquid therefore move at the same physical fullness rate while retaining their real nutrition difference.
- The remaining target, personal fullness-gain multiplier, network supply and 95% hard safety ceiling still limit every pulse exactly. A partial network supply produces only the conserved amount available.
- Survival and Maintain retain the existing small nutrition pulse because those modes are intended to satisfy hunger without rapidly filling the stomach.


## Full configurable settings tab

- The bottom-bar **RimRound Patch** window now contains seven scrollable pages: **Feeding**, **Cooldowns**, **Social**, **Pawn behaviour**, **Prisoners**, **World & generation**, and **Food network**. The same interface remains available through RimWorld's normal Mod Settings list.
- Every new option loads with the exact v1.0.67-beta.1 behaviour as its default. Existing saves therefore behave identically until the player changes a value.
- Feature switches cover autonomous shared meals, autonomous one-way feeding, manual right-click feeding, bedside/sleeping-recipient feeding, dialogue, post-meal social recreation, idle underweight eating, automatic milk expression, weight-opinion persuasion, prisoner feeding, Fatten, trait generation, Gluttonium generation, and orbital movement relief.
- Numeric controls cover starting fullness, feeding target, session timeout, event frequency, all feeding cooldowns, recreation target, chat limit, idle trigger/delay, persuasion chances/cooldown, prisoner delivery cooldown, Fatten stop/resume thresholds, Gluttonium frequency, and orbital relief percentage.
- Dependent controls are disabled when their parent feature is off. Biotech and Odyssey controls are disabled when their DLC is absent. Every page has **Reset this page**, and **Reset all** restores the complete v1.0.67 behaviour.
- Settings are saved globally. Gameplay controls marked **Live** are read immediately; trait-distribution controls affect new pawns, and Gluttonium controls affect newly generated maps only.
- Disabling autonomous features stops new sessions without cancelling one already in progress. Disabling milk expression preserves each pawn's saved toggle. Disabling trait generation never removes an existing trait.
- Hoverchair recovery, fullness-overflow protection, sleeping/bed preservation and Mine Vein compatibility remain always-on reliability fixes.
- The patch tab now has its own blue-and-gold 64×64 icon, while remaining directly beside RimRound's original settings button.

## Hoverchair startup correction

- RimWorld 1.6 moved apparel-node creation from `PawnRenderTree.ProcessApparel` to `DynamicPawnRenderNodeSetup_Apparel`. The old target no longer existed and caused Harmony to abort the companion assembly's static initializer during startup.
- Hoverchair lying-render suppression now patches RimWorld 1.6's Boolean apparel-node gate. A worn hoverchair is excluded only while its wearer is actually lying; all other apparel and standing chair users continue through vanilla rendering unchanged.
- The render hook is installed defensively. If a later RimWorld build moves the gate again, the patch logs one warning and skips only this visual suppression instead of preventing the complete Feed Other patch from loading.

## Configurable feeding target

- A new bottom-bar **RimRound Patch** settings tab appears directly beside RimRound's own tab. The same page is also available through RimWorld's normal mod-settings list.
- **Feed Other stop fullness** can be set in one-percent steps from **60% to 90% of current hard stomach capacity**. The saved default is **70%**, matching RimRound's actual Very Full boundary.
- The selected target controls Shared Meal, autonomous Feed Other, manual right-click feeding and both bedside variants. Meal allocation, completion latches, repeat searches and the final-ingestion clamp all read the same live setting.
- At the default, the right-click command remains `Feed [name] until very full`. At another value it displays the exact target, such as `Feed [name] until 80% fullness`.
- RimRound's underlying health stages are not redefined. Its real **Very Full** stage and this patch's positive/negative Very Full mood memories still begin at **70%**, even when feeding continues to a higher target. A target below 70% therefore does not award a Very Full mood from that session.
- Prisoner **Fatten** remains a separate system, defaulting to exact **80% Painfully Full** with a 10% resume threshold; both values now have their own Prisoners-page controls.
- Targets of 80% or more intentionally enter severe fullness stages and may make heavily penalised pawns collapse or become immobile. The settings page shows that warning before use. The 90% maximum remains below stomach rupture.
- Changing the setting while a session is active takes effect on its next fullness/completion check. Lowering it never forcibly removes existing fullness; a pawn already above the new target simply finishes the feeding phase.


## Post-meal social recreation

- When **Share Meal**, **Feed Other**, or either bedside-feeding variant completes its feeding target, the two participants remain together if at least one of them is still below the configured recreation target (**95% by default**).
- The post-meal phase grants the same dedicated `shared indulgence` recreation at **0.75x** the standard Feed Other rate. By default it ends when both applicable recreation needs reach 95%, after **2,500 ticks / one in-game hour**, or immediately if the pair can no longer safely socialise; the target and time cap are now configurable.
- Both pawns stop moving and continue facing one another or their shared dining position. Drafting, mental states, separation onto different maps, despawning, sleeping, or moving more than six cells apart ends the extra phase cleanly. Awake immobile, downed, and bed-resting recipients may participate without being moved.
- A healthy pawn who was asleep when manual or autonomous bedside feeding began returns to the suspended sleep job immediately after feeding; the patch never keeps that sleeper awake merely for the optional conversation.
- Post-meal dialogue is now split into **PostSharedMeal** and **PostFeeding** contexts. Shared Meal still lets either eater discuss personal fullness because both pawns ate. One-way Feed Other and bedside feeding use a separate role-aware pool.
- In one-way post-feeding conversation, fullness and positive/negative Very Full mood are read **only from the fed pawn**. A feeder can no longer receive lines claiming that they personally ate, digested the meal, became stuffed, or struggled to stand.
- Every one-way post-feeding entry is locked to **Feeder**, **FedPawn**, or **MutualNarration**. Feeder lines check comfort, discuss future portions, tease the recipient, or respond to the recipient's weight goals; personal appetite and digestion lines are restricted to the fed pawn.
- Neutral one-way feeders use moderate, practical dialogue. They help the recipient and discuss what the recipient wants without speaking as though they personally want to gain weight or were filled by the meal.
- Dialogue selection still reads weight opinion, broad current body-size stage, fullness, Very Full mood, speech capability, and relationship. Recipient goals drive gain, maintenance, loss, or stay-lean discussion, while current size and relationship alter the wording safely.
- Each post-meal phase still allows up to two normal exchanges plus one closing exchange; recreation gain, duration, mood reward, cooldowns, food use, and all event completion rules are unchanged from v1.0.64-beta.1.
- A post-meal phase that lasts at least **600 ticks** grants both eligible pawns the non-stacking **Enjoyed an after-meal chat** memory: **+2 mood for four in-game hours**.
- The new phase does not restart feeding, fetch more food, alter existing feeding/opinion rewards, or bypass session cooldowns. Existing v1.0.63 ore generation and Mine vein fixes remain included.


## Gluttonium ore generation and Mine Connected compatibility

- Gluttonium ore no longer participates in RimWorld's shared weighted surface-ore pool. Its `mineableScatterCommonality` is set to zero before vanilla generates ordinary mineable lumps, so steel, compacted machinery, silver, gold, uranium, plasteel and jade retain their normal selection chances and counts.
- A separate `RR_ScatterGluttoniumLumps` map-generation step runs immediately after ordinary `RocksFromGrid`. Its configurable default is **1.2 Gluttonium veins per 10,000 map cells**, or approximately **7–8 additional veins on a standard 250×250 map**. Each vein keeps RimRound's original 3–6 tile size and 2 Gluttonium yield per tile.
- The independent step is added to every map-generator base that explicitly uses normal `RocksFromGrid`, including player colony maps, faction-base maps and the landed escape-ship map. Child generators inheriting those bases receive it automatically. Generators using `RocksFromGrid_NoMinerals`, such as vanilla encounter maps, remain mineral-free exactly as they are in the base game.
- Existing maps are not retroactively repopulated. The change applies when a new qualifying map is generated. Deep-scanner Gluttonium remains unchanged.
- `RR_GluttoniumOre` now explicitly receives vanilla's missing `<veinMineable>true</veinMineable>` flag. Ordinary Mine already worked through `RockBase`; this additional field is what makes RimWorld 1.6's selected-object **Mine vein** command appear and flood-fill the connected Gluttonium deposit.
- The ore-generation and Mine vein corrections remain narrowly scoped XML patches. v1.0.65 changes the companion DLL only for role-aware post-meal conversation routing.

## Beta trait-generation rules

- RimWorld first generates the pawn's ordinary vanilla, DLC and other-mod personality traits.
- RimRound then assigns its weight-opinion trait using the balanced beta distribution. A recognised opinion already supplied by a pawn editor is preserved and safely synchronised to RimRound's attitude component instead of being replaced or converted to `None`.
- This beta runs after RimRound's trait postfix and guarantees exactly one **eating-style** trait and exactly one **stomach-elasticity** trait. These two dedicated categories no longer consume vanilla personality-trait slots.
- Existing or pawn-editor-selected weight-opinion, eating-style and elasticity traits are preserved. Missing eating/elasticity categories are generated for existing living pawns when a game is started or loaded. When a pawn editor leaves duplicates, the newest deliberate non-average eating/elasticity selection is preferred over an automatically generated average marker; duplicate weight opinions similarly preserve the newest recognised selection.
- SwellGlow digestion and metabolism traits remain optional members of vanilla's normal trait pool; they are not guaranteed or rerolled by this beta.

### Weight-opinion generation

Newly generated pawns use this neutral-heavy, symmetric distribution. Existing opinions are not rerolled.

| Weight opinion | Chance |
|---|---:|
| Hate | 3% |
| Dislike | 7% |
| Neutral− | 12% |
| Neutral | 36% |
| Neutral+ | 20% |
| Like | 12% |
| Love | 7% |
| Fanatical | 3% |

### Guaranteed eating style

| Trait | Chance | Actual Eating Speed | Existing fullness-penalty effect |
|---|---:|---:|---|
| Very slow eater | 5% | ×0.75 | Retained |
| Slow eater | 20% | ×0.90 | Retained |
| Average eater | 50% | ×1.00 | Neutral |
| Speed eater | 20% | ×1.10 | Retained |
| Inhales food | 5% | ×1.25 | Retained |

The four existing SwellGlow eating traits now modify RimWorld's real `EatingSpeed` stat as their labels imply. Feed Other's self/shared/assisted eating durations therefore use these traits automatically.

### Guaranteed stomach elasticity

| Trait | Chance | RimRound stomach stretching |
|---|---:|---:|
| Rigid stomach | 20% | 50% of normal |
| Average stomach elasticity | 60% | 100% of normal |
| Flexible stomach | 15% | 150% of normal |
| Elastic stomach | 5% | 200% of normal |

Elasticity controls how quickly RimRound's personal stomach limit stretches; it does not directly change a pawn's initial stomach capacity.

## Beta feeding eligibility and cooldown rules

- By default, a new shared meal or feeding session may start when every eating participant is at **exactly 50% of hard capacity or less**. A value strictly above the configured cutoff blocks selection. This replaces the stable build's 65% cutoff and is now adjustable below the selected feeding target.
- Manual one-way feeders still do not need to be below the cutoff because they do not eat; the clicked recipient must be at 50% or less.
- Default saved cooldowns remain three hours for a one-way feeder, six hours for a recipient/shared participant and six hours for the same pair; all three durations are now configurable.
- For a specific autonomous pairing, all participant and pair cooldown checks are bypassed when **either pawn has Like, Love, Fanatical or Extreme weight opinion**. This allows a Like-or-higher pawn to feed or be fed repeatedly whenever the recipient has returned to 50% fullness or less.
- Cooldown timestamps are still recorded. A lower-opinion pawn who just participated with a Like+ pawn remains blocked from immediately starting an unrelated pairing with another lower-opinion pawn.
- Manual right-click feeding remains cooldown-free for every opinion by default; disabling **Manual commands ignore feeding cooldowns** makes it check and record the configured cooldowns.

## Eating duration in this revision

- Normal self-eating remains RimWorld's baseline: `baseIngestTicks / Eating Speed`.
- Shared-meal eating now takes **90% of normal self-eating time**, while still dividing by the individual eater's live Eating Speed.
- Being fed now takes **85% of normal self-eating time**, while still dividing by the fed pawn's live Eating Speed. This applies to one-way Feed Other, bedside feeding, prisoner Fatten and ordinary vanilla patient/prisoner feeding.
- The multiplier is calculated when chewing actually starts, after a dispenser or split stack has produced the real meal. Mouth/tongue injuries, consciousness, genes, equipment and RimRound fullness penalties therefore affect the correct serving in real time.
- Food definitions that explicitly disable `useEatingSpeedStat` keep that exemption, but still receive the modest shared/assisted activity bonus.

## Sleeping-pawn bed behaviour in this revision

- Autonomous Feed Other and Shared Meal searches still try every valid **awake** recipient first. A healthy sleeping pawn is considered only when no awake candidate can form a valid session with available food.
- The autonomous fallback requires a mobile, eating-capable pawn who is genuinely asleep in a usable bed under an interruptible non-player-forced `LayDown` job. Drafted, downed, mentally broken, non-interruptible or movement-incapable sleepers are not selected automatically.
- Manual right-click `Feed [name] until very full` now detects a healthy sleeping mobile recipient before pre-start preparation. Their normal interruptible `LayDown` job is preserved instead of being ended.
- When either route starts, the sleeper's existing `LayDown` job is suspended and `RR_BeFedOtherPartner` holds them in the same bed. Their posture remains `LayingInBed`; they cannot walk to a chair, food storage, table or the feeder.
- The temporary recipient driver forces its internal asleep state off at start and every waiting tick, keeping the pawn awake for feeding, conversation and recreation until the event succeeds, times out, runs out of food or otherwise ends.
- Session cleanup interrupts the temporary recipient job and immediately resumes the suspended `LayDown` job, restoring normal sleep without a standing transition. This cleanup also runs when the feeder job fails or is cancelled.
- Genuinely downed or immobile bed recipients continue to keep their native patient job untouched rather than receiving the temporary mobile bed-lock job.

## Prisoner feeding in this revision

- The patch now fully replaces `WorkGiver_Warden_DeliverFood.JobOnThing` for prisoners, bypassing both vanilla's room-food heuristic and RimRound's original Fatten delivery prefix.
- On **Maintain only, Recruit, Reduce resistance, Enslave, Convert and other ordinary interaction modes**, a hungry mobile prisoner receives exactly one controlled food serving. A prepared meal or nutrient-paste meal counts as one; stackable food uses one normal eating portion.
- By default, the next ordinary delivery cannot occur until **5,000 ticks / two in-game hours** after the previous serving was successfully dropped. The saved per-prisoner cooldown is configurable, and an active-delivery check prevents multiple wardens from starting duplicate deliveries.
- Food already present in the prison room no longer permanently suppresses an urgent delivery. If the prisoner remains hungry after two hours, another single serving may be supplied.
- Downed or medically resting non-Fatten prisoners remain on vanilla `WorkGiver_Warden_Feed` and are fed directly when vanilla considers them hungry.

## Fatten interaction in this revision

- Selecting `RR_Fatten` immediately assigns the prisoner a dedicated permanent bed-rest job. The bed driver never searches for another job, and a saved game component reasserts the bed lock every 60 ticks while Fatten remains selected.
- Switching away from Fatten immediately releases the dedicated bed-lock job.
- Wardens directly feed a bed-locked Fatten prisoner **one serving at a time**. After every serving, the live physical fullness value is checked again. The configurable target defaults to exact **Painfully Full at 80% of current hard stomach capacity**.
- The existing fullness setter discards excess from the final indivisible serving above exact 80%, so the programme enters Painfully Full but cannot drift deeper into that stage from an oversized final meal.
- Reaching the selected target sets a saved per-prisoner Fatten latch. By default no further special Fatten feeding can begin until physical fullness falls **strictly below 10% of the current hard limit**; this resume threshold is also configurable. The prisoner remains locked in bed throughout this digestion period.
- Fatten prisoners cannot start their own eating jobs; the controlled warden route is the only feeding route while the interaction is selected.
- RimRound's `WorkGiver_Warden_ReduceReluctanceChat` is disabled for Fatten prisoners, so the special weight-gain chat no longer reduces reluctance/resistance.
- `PrisonBreakUtility.CanParticipateInPrisonBreak` returns false for Fatten prisoners. They cannot initiate or join a prison break while the bed lock is active.
- Invalid legacy prisoner diet ranges are still repaired safely. Their UI range remains capped at the non-warning 70% default, while the dedicated Fatten job uses its separate exact 80% physical target and 10% restart threshold.

Apart from the beta trait generation, 50% starting threshold and Like+ pairing-specific cooldown bypass described above, the stable v1.0.60 feeding, food-selection, sleeping-bed, prisoner and chance mechanics remain unchanged.

---

# RimRound Feed Other (RimWorld 1.6)

This patch adds vanilla-style paired recreation activities to RimRound. It is implemented as a `JoyGiverDef`, linked job pairs, and a small companion assembly; it does not replace `RimRound.dll`.

Version 1.0.1 removes a reverse partner reservation that could prevent the linked partner job from starting in RimWorld 1.6.

Version 1.0.2 stores the ingestible in vanilla's required `targetA` slot. This prevents stack pickup from replacing the linked partner target and prematurely ending the session.

Version 1.0.3 synchronizes meal pickup and meal completion for both pawns, raises the target to **About to Burst** (90% of hard capacity), and clamps the final meal at that target so an indivisible meal cannot cause a stomach rupture.

Version 1.0.4 gives the activity a much higher recreation-selection weight, prevents either participant from starting at 65% hard fullness or above, and fills both recreation needs on successful completion.

Version 1.0.5 adds a one-way feeding variant for adult RimRound-exempt pawns with Neutral-, Dislike, or Hate weight opinions. It also latches session completion at 90%, preventing a finished pawn from fetching and wasting another meal if digestion drops them slightly below the cap while their partner catches up.

Version 1.0.6 allows an exempt pawn to be the recipient of the one-way feeding variant. This fixes valid Neutral- feeder and Like recipient pairs being rejected when Personal Exemption was active on both pawns.

Version 1.0.7 allows the Neutral-/Dislike/Hate feeder to be either RimRound-enabled or exempt. This fixes ordinary adult colonists, such as globally enabled women without Personal Exemption, being incorrectly rejected as one-way feeders.

Version 1.0.8 makes meal selection capacity-aware and adds graceful completion. Pawns prefer the largest meal that fits their remaining room below the 90% target, fall back to the smallest overshoot, and use distance only as a tie-breaker. Sessions end successfully for recreation after 15,000 ticks (six in-game hours), or when stored suitable food runs out, even if a participant cannot reach the cap.

Version 1.0.9 adds delivery and bedside feeding for immobile recipients. An awake Neutral+ non-exempt pawn who can eat no longer needs Moving or Manipulation and remains at their bed/current position. Neutral-/Dislike/Hate feeders deliver without eating, while a normal Neutral+ mobile participant can feed the recipient and then eat their own capacity-matched meals beside them.

Version 1.0.10 allows any adult personally or categorically exempt pawn to take the delivery-only feeder role regardless of weight opinion, while RimRound-enabled pawns still qualify at Neutral-, Dislike, or Hate. This fixes globally exempt males with positive opinions, such as Klem. It also reduces the session safety limit to 7,500 ticks (three in-game hours).

Version 1.0.11 fixes bed-bound recipients trying to crawl to food storage when their previous bed-rest job is interrupted. The linked recipient job now snapshots the pawn's current bed cell before the interruption and keeps them there while the mobile pawn retrieves and delivers every meal.

Version 1.0.12 adds bulk meal collection and proper dining locations. Pawns calculate the servings needed for the remaining session capacity and collect the largest reservable amount allowed by the source stack and their carry space. Mobile shared-meal pairs use two available chairs at the same nearby table and wait until both are seated. A mobile one-way recipient goes to a nearby dining chair while the feeder delivers the stack. Bed-bound recipients remain in place while enough compatible servings for the recipient—and, where possible, the mobile eater—are carried bedside in one trip.

Version 1.0.13 hardens bedside classification and posture. A pawn who is lying down, downed, unable to move, or physically occupying a bed cell is excluded from every mobile shared-meal role, even when RimWorld still reports their Moving capacity as technically capable. The linked recipient job now carries a persisted no-movement marker, never creates a path for that pawn, and preserves the pawn's lying-in-bed posture while meals are delivered.

Version 1.0.14 fixes feed/share events disappearing from recreation selection after ordinary eating made every pawn bored of the vanilla Gluttonous recreation category. Feed Other now uses a dedicated `shared indulgence` recreation category whose tolerance is not accumulated. Fullness, opinion, exemption, scheduling, and partner-availability rules still control whether the activity can start, but unrelated Gluttonous boredom can no longer suppress it before those rules are evaluated.

Version 1.0.15 adds a `satisfyingly very full` mood memory. When an eater in a feed/share session reaches RimRound's exact **Very Full** threshold (70% of hard stomach capacity), a pawn with `WeightOpinion.NeutralPlus` or higher receives +5 mood for six in-game hours. It does not stack, delivery-only feeders do not receive it unless they separately eat in an eligible session, and reaching the higher 90% session target is not required.

Version 1.0.16 scales the six-hour Very Full mood memory by weight opinion: `NeutralPlus` and `Like` grant +5, `Love` grants +10, and `Fanatical` or `Extreme` grant +15. Opinions below Neutral+ still receive no mood memory.

Version 1.0.17 makes every shared-meal, one-way feeding, and bedside feeding search prefer the initiator's current spouse, fiance, or lover. The romantic partner must still pass all normal willingness, availability, fullness, exemption, diet, reachability, and food checks; if they do not, the event falls back to another valid pawn. Colonies with multiple current love partners randomize among those partners before considering anyone else.

Version 1.0.18 rebalances recreation selection into close priority tiers instead of giving Feed Other a weight of 100. Walking and swimming receive a small 0.95x energetic-activity multiplier; reading, chess, prayer, and other ordinary activities retain their normal RimWorld chances; and an eligible feed/share event receives approximately 1.10x to 1.18x according to the initiator's weight opinion. Normal availability, recreation tolerance, and boredom rules continue to influence the final selection.

Version 1.0.19 adds the opposite reaction for pawns who dislike weight gain. Whenever a pawn crosses upward into RimRound's **Very Full** stage at 70% of hard stomach capacity, `NeutralMinus` receives -5 mood, `Dislike` receives -10, and `Hate` receives -15 for six in-game hours. The discomfort applies regardless of how the pawn became full, does not stack, and leaves `Neutral` unchanged. Existing positive Feed Other memories remain +5/+10/+15 for eligible Neutral+ eaters.

Version 1.0.20 allows the positive six-hour **Very Full** memory to trigger when a Neutral+ or higher pawn crosses 70% fullness through ordinary eating or any other source, not only during Feed Other. It also gives underweight positive-opinion pawns a 20% chance, whenever RimWorld reaches its genuine idle-job fallback, to seek a normal meal while below 25% of hard fullness. The pawn must currently be in the early negative portion of their own weight-opinion thought; later high-weight penalties do not encourage more eating. Normal diet, food-policy, reachability, reservation, and ingestion checks still apply.

Version 1.0.21 adds a relationship-only right-click order. Select a pawn, right-click their current spouse, fiance, or lover, and choose `Feed [name] until very full` beside RimWorld's normal romance option. The command targets that exact partner, automatically selects shared-meal, delivery-only, or bedside feeding as appropriate, and uses a player-forced job while preserving all existing opinion, exemption, availability, food, and 65% starting-fullness checks. The existing session still continues to its 90% **About to Burst** cap despite the shorter menu wording.

Version 1.0.22 fixes low-fullness idle eating appearing never to trigger during unlucky chains of `GotoWander` jobs. A qualifying pawn now receives a randomized 300-900 tick idle delay and is then guaranteed to attempt a normal food job at the next idle-job selection. If no acceptable food is currently available, another randomized attempt is scheduled rather than scanning continuously. This remains idle-only and cannot interrupt work, sleep, drafted orders, forced jobs, or mental states.

Version 1.0.23 moves the low-fullness eating trigger onto RimWorld's actual `JobGiver_Wander` path while retaining the generic idle fallback. Colony pawns receive `GotoWander` and `Wait_Wander` from this earlier job giver, so the v1.0.22 fallback hook could remain unreachable indefinitely. The randomized 300-900 tick schedule and all eligibility and normal food-selection rules are unchanged.

Version 1.0.24 lets low-fullness eating retrigger without the player breaking idle first. A pawn can remain inside one continuing idle-tagged wander/wait job while fullness falls through 25%, so no job giver is invoked at the crossing. The patch now checks that active idle job on RimWorld's normal job-tracker tick and safely replaces only an interruptible, non-player-forced idle job when the randomized meal timer matures. Work, recreation, sleep, drafted behavior, mental states, and player orders remain untouched.

Version 1.0.25 makes the relationship right-click command behave like a true two-pawn player order. The exact clicked partner may now suspend an ordinary interruptible work job, such as Research, to join the manual feeding session; RimWorld's existing suspended-job mechanism resumes that work afterward. Autonomous feed/share selection still refuses to interrupt work. Drafting, mental states, non-interruptible jobs, existing player-forced orders, fullness limits, eligibility, and food rules remain protected.

Version 1.0.26 adds a saved, per-pawn **Express milk automatically** toggle for player-controlled lactating women. It is off by default and appears only while the selected pawn has RimWorld's Lactating hediff. At full milk charge it creates the largest whole stack of vanilla Milk items that the stored milk nutrition can supply and places it beside the pawn. With vanilla values this produces two Milk items from a 0.125 charge, consumes their combined 0.10 nutrition from milk fullness, and preserves the remaining 0.025. Milk expression and breastfeeding therefore compete for the same supply without duplicating nutrition. RimRound's existing weight-based lactation multiplier naturally controls how quickly the next stack becomes available.

Version 1.0.27 adds Odyssey orbital-map mobility support. While a pawn is on a real `SpaceMapParent`, negative **Moving** offsets from RimRound's Weight and Fullness hediffs are reduced by 75%, so only 25% of their normal planetary penalty remains. For example, a -0.80 Weight movement offset becomes -0.20 in orbit. The relief applies throughout the orbital map, including pressurized rooms, rather than only to exposed vacuum cells. It does not alter manipulation, eating speed, pain, hunger, ordinary injuries, or non-RimRound health conditions, and it remains inactive on planetary maps and when Odyssey is not in use.

Version 1.0.28 allows a sleeping selected pawn to receive the relationship right-click `Feed [name] until very full` order. Sleep and the temporary lying-in-bed posture are ignored only while validating this direct player order; accepting it wakes the pawn through RimWorld's normal ordered-job handling. Automatic events still require awake participants, and drafted, downed, mentally broken, immobile, or otherwise genuinely ineligible feeders remain blocked.

Version 1.0.29 fixes Odyssey movement relief on claimed asteroids and other settled orbital colonies. RimWorld converts a claimed asteroid's map parent into a normal settlement, so the previous `SpaceMapParent` test no longer recognized it. The patch now checks the map tile's actual planet layer, covering gravships, asteroids, platforms, and settlements on Orbit or another space layer while remaining inactive on planetary maps.

Version 1.0.30 adds two normal gameplay social abilities: **Increase weight opinion** and **Decrease weight opinion**. Every player-controlled humanlike adult with a RimRound weight opinion receives the buttons after loading or spawning. Targeting another awake adult moves their opinion exactly one level in the chosen direction, synchronizes RimRound's stored opinion and trait, and is capped at Hate/Fanatical. Both directions share a one-day cooldown, require a Social-capable initiator, cannot target self or hostile pawns, and work without Ideology despite using its touch-conversion presentation.

Version 1.0.31 turns those weight-opinion abilities into persuasion attempts instead of guaranteed changes. Social skill is clamped to levels 1-10: level 1 starts at 20%, each additional effective level adds 6.5 percentage points, and levels above 10 receive no further increase. The target's social opinion of the speaker contributes from -15 to +15 percentage points; a current spouse, fiance, or lover adds another +15, while a parent, child, sibling, or half-sibling adds +8. The final chance is capped between 5% and 95%, is shown while targeting, and a failed attempt still consumes the shared one-day cooldown.

Version 1.0.32 expands the right-click **Feed [name] until very full** command from romantic partners to any non-hostile humanlike pawn who can participate in RimRound feeding. The selected feeder and clicked recipient are forcibly undrafted, woken, stopped, and made to drop carried items before the one-way feeding order begins. A mobile recipient uses a nearby dining chair when one is close; the feeder follows without prematurely starting while the recipient is still walking. If no nearby chair is practical, the recipient approaches the feeder instead. Once both pawns are adjacent and stationary, the recipient waits in place through the feeding session. Genuinely downed or immobile recipients remain where they are.

Version 1.0.33 fixes a RimRound 1.6 binary-compatibility regression in the v1.0.32 rebuild. `PersonallyExempt` and `CategoricallyExempt` are compiled as RimRound `ExemptionReason` properties with implicit boolean conversion, rather than nonexistent bool fields. This removes the repeating `MissingFieldException` pawn-scan error spam while preserving all v1.0.32 manual-feeding and movement behaviour.

Version 1.0.34 raises autonomous shared-meal and feeding recreation to a competitive base weight of **3.0** and validates a real partner and reachable meal before offering that weight. Positive weight opinions now provide meaningful multipliers: Neutral+ 1.00x, Like 1.15x, Love 1.30x, Fanatical 1.45x, and Extreme 1.50x. Current romantic partners add up to 1.25x (1.35x for bedside care), while close friends at +40 social opinion add 1.10x. Low-fullness and convenient nearby dining situations add small flat bonuses, distant food is slightly penalized, and the final autonomous chance is capped at 5.0. Successful autonomous starts give both participants a saved six-hour participation cooldown and the same pair a twelve-hour cooldown; bedside pairs use six hours. Manual right-click feeding ignores all cooldowns.

Version 1.0.35 replaces the end-of-session recreation refill with continuous, activity-based gain. Shared meals use 0.80x the standard recreation rate, one-way feeding uses 0.60x, and bedside feeding uses 0.70x. Recreation is gained only while the pawns are at the social feeding stage rather than while travelling or collecting meals. Completing, timing out, or running out of food no longer sets either recreation bar to full; each pawn keeps only the recreation earned during the session.

Version 1.0.36 adds capacity-aware food delivery for RimRound's **Fatten** prisoner interaction. Every warden delivery recalculates the prisoner's current hard limit, upper dietary target, food nutrition-density ratio, and personal fullness multiplier. A whole prepared meal is kept when it safely fits; otherwise the warden selects a counted stack of allowed raw or other stackable food that approaches the target without crossing it. The maximum automatic target is 95% of the pawn's current hard limit, food already in the cell only blocks delivery when at least one unit safely fits, and fullness increases during Fatten are clamped at the same safety cap unless RimRound's explicit above-hard-limit override is active. Because the hard limit is read on every job and fullness update, stomach growth and capacity-changing perks automatically increase later safe portions.

Version 1.0.37 makes all Feed Other variants understand RimRound's dynamically sized meals and select food from the recipient's current remaining capacity. Complete prepared meals are preferred while they fit; stackable foods are used only as exact top-ups. Collection counts use whole safe servings rather than rounding upward, and unused carried food is dropped intact instead of partially consumed and destroyed at the session limit.

Version 1.0.38 consumes each selected stackable top-up batch in one feeding action instead of looping through berries or similar items one at a time. Prepared meals remain individual servings. Bedside jobs no longer combine both pawns' raw top-up portions into one carried batch. Automatic food selection also rejects kibble, baby food, desperate-only foods, and any candidate that would currently give the recipient a negative ingestion mood thought, while still allowing acceptable raw foods such as berries.

## Behaviour

- A pawn can initiate the activity while seeking recreation.
- The recreation chance is evaluated only after a valid partner and suitable reachable meal have been found, so the higher priority does not waste recreation selections on impossible sessions.
- Autonomous shared-meal participants normally receive a saved six-hour participant cooldown. In autonomous one-way feeding, the non-eating feeder normally receives a three-hour feeder cooldown and the fed recipient receives a six-hour participant cooldown. Every autonomous same-pair cooldown is six hours. A pairing involving Like/Love/Fanatical/Extreme bypasses all of those checks for that pairing; manual right-click feeding bypasses them for every opinion.
- A current spouse, fiance, or lover is checked before unrelated candidates. If that partner is unavailable, unwilling under the existing eligibility rules, or lacks suitable reachable food, another valid pawn can be selected normally.
- For a direct order, select a player pawn and right-click any non-hostile humanlike pawn with an active RimRound fullness system. At the default target, `Feed [name] until very full` targets exactly that pawn; other settings show the chosen percentage in the command. It uses one-way feeding and does not require romance, a positive weight opinion, exemption status, or hunger on the feeder.
- Accepting the direct order undrafts and wakes both participants, interrupts their current jobs, and drops anything they are carrying so the forced feeding sequence can take control immediately. Autonomous feed/share selection remains non-disruptive.
- A healthy sleeping mobile recipient selected through the autonomous fallback or manual right-click route wakes but remains locked in their current bed until the event ends, then resumes the suspended `LayDown` job. Downed or genuinely immobile recipients retain their native patient job in place.
- A mobile recipient reserves a usable dining chair only when it is within six cells of their starting position. The feeder follows them but cannot begin chewing merely by touching them while they are still walking. Without a practical nearby chair, the recipient dynamically approaches the feeder. Feeding starts only when both pawns are adjacent and stationary, after which the recipient stops pathing and waits for the session.
- A player-controlled lactating woman has an **Express milk automatically** pawn toggle. When enabled, reaching full milk charge drops the largest nutrition-conserving stack of vanilla Milk beside her. The preference is saved per pawn, defaults off, works while she remains player-controlled and spawned on a map, and does not create a separate milk reserve from breastfeeding.
- On every Odyssey map on the Orbit or another space planet layer—including gravships, asteroids, platforms, and settled space colonies—RimRound Weight and Fullness reduce a pawn's Moving capacity by only 25% of their usual planetary amounts. This is map-wide orbital relief, not a vacuum-cell check; other health effects and ordinary injuries are unchanged.
- Player-controlled humanlike adults receive **Increase weight opinion** and **Decrease weight opinion** social ability buttons. Each three-second conversation attempts to shift another eligible adult by one level, shares a one-day cooldown between both directions even on failure, and stops at Hate or Fanatical. Success uses effective Social 1-10, the target's opinion of the speaker, and romantic/close-family relationship bonuses; the exact chance appears on the target tooltip. The target must be awake, conscious, mentally stable, non-hostile, and cannot be the caster. The initiator must be capable of Social work. These abilities use RimWorld's base ability framework and do not require Ideology.
- Autonomous shared-meal participants must have RimRound `WeightOpinion.NeutralPlus` or higher. The direct one-way order bypasses weight-opinion requirements.
- Autonomous shared-meal participants must both be at exactly 50% of hard fullness or less. For a direct one-way order, only the clicked recipient must begin at 50% or less; the feeder does not eat.
- Autonomous mobile participants must be humanlike, awake, spawned, able to move/manipulate/eat, and have food and recreation needs. Direct orders may wake sleepers and support immobile recipients, while the feeder must still be able to move and manipulate.
- Autonomous events reject drafted, downed, mentally broken, personally exempt, categorically exempt, and RimRound-disabled pawns. Direct orders undraft ordinary participants and bypass exemption/opinion rules, but still reject dead, hostile, mentally broken, or physically incapable feeders and recipients who cannot eat through RimRound.
- The partner must be idle or already recreating. Player-forced and ordinary work jobs are not interrupted.
- The pair uses human-edible food currently held in valid storage. Forbidden, unacceptable, socially improper, politically improper, and drug foods are rejected. Kibble, baby food, desperate-only food, and any food that would currently produce a negative ingestion mood thought are also rejected.
- Each pawn estimates fullness from the food's nutrition, nutrition-density ratio and personal fullness multiplier. Proper prepared meals are searched first and the required serving count is rounded upward, so the final whole meal may exceed the remaining gap. Excess fullness is discarded at the selected exact feeding target. Only when no complete prepared-meal allocation is available does selection fall back through higher-preferability processed foods, treats, milk and then acceptable raw foods.
- Before travelling to dine, each collector must reserve and pick up their complete calculated allocation from one suitable stack. Partial reservations are rejected. Once eating begins, the job never returns to storage; an unexpectedly lost or unusable allocation ends the session cleanly.
- If two usable chairs at the same table are within the meals' normal chair-search range, the pair reserves those seats, travels there, and waits until both have arrived before eating. If no suitable shared table exists, they use the existing adjacent social-meal fallback.
- After each meal, they wait for one another to finish before starting another meal round. If only one pawn still needs food, the full pawn waits in place for them.
- Prepared meals are consumed one serving per synchronized eating round. A selected stackable allocation is consumed as one calculated portion per eater, so berries and similar foods do not create dozens of one-item loops. Any unused carried food is dropped intact at the dining location when the event ends.
- Reaching the selected feeding target is remembered for the rest of the session. The final small-food portion is clamped at that exact percentage before RimRound processes the fullness increase, then that eater is latched complete. Later digestion cannot restart feeding or trigger another food search.
- Crossing upward into RimRound's **Very Full** stage at 70% of hard capacity by any source grants eligible eaters a non-stacking six-hour mood memory: +5 at Neutral+/Like, +10 at Love, and +15 at Fanatical/Extreme.
- Crossing upward into **Very Full** by any source grants a separate non-stacking six-hour discomfort memory to low-opinion pawns: -5 at Neutral-, -10 at Dislike, and -15 at Hate. Neutral has no fullness mood reaction.
- While RimWorld is assigning or continuing an idle-tagged `GotoWander`, `Wait_Wander`, or generic idle job, a non-exempt Neutral+ or higher pawn who is below 25% hard fullness and currently has a negative **underweight** weight-opinion thought receives a randomized 300-900 tick delay, then attempts to seek a normal meal. The active check can replace an interruptible idle job, so fullness crossing the threshold does not require the player to draft or order the pawn first. A failed food search schedules another randomized attempt. This cannot interrupt work, recreation, sleep, drafted/forced orders, or mental states, and all ordinary food-selection restrictions remain active.
- They aim for the configured **60–90%** target, with **70% Very Full** as the recommended default. Selecting 80% or 90% intentionally permits Painfully Full or About to Burst respectively, but the final serving is still clamped exactly and never allowed to overshoot the selected target.
- The session grants dedicated `shared indulgence` recreation and Social skill experience to both pawns. This category deliberately does not accumulate recreation tolerance, so unrelated food recreation cannot disable the event.
- Recreation is gained continuously instead of being filled at completion. Shared meals use 0.80x the standard rate (about 28.8 percentage points per in-game hour), one-way feeding uses 0.60x (about 21.6 points per hour), and bedside feeding uses 0.70x (about 25.2 points per hour).
- Reaching the cap normally gives each pawn a `shared an indulgent meal` social memory about the other: +4 opinion for 10 days, stackable three times per pair at diminishing strength.
- A session has a 7,500-tick safety limit (three in-game hours). If the limit expires, the initial allocation is lost, or a carried portion becomes unusable, the activity ends cleanly without another storage trip and retains only the recreation already earned. The social memory is reserved for sessions where both pawns actually reached the cap.

## One-way feeding

- A humanlike feeder must be biologically age 18 or older. Personally or categorically exempt pawns qualify regardless of weight opinion. A RimRound-enabled pawn qualifies with `WeightOpinion.Neutral`, `NeutralMinus`, `Dislike`, or `Hate`; `WeightOpinion.None` alone does not qualify.
- The feeder only needs to be awake, spawned, mentally stable, undrafted, and capable of moving and manipulation.
- The feeder's own fullness, food need, diet mode, and Eating capacity are ignored. They carry and administer the meals but never consume them.
- The recipient must be non-exempt, have an active RimRound fullness/diet component, `WeightOpinion.NeutralPlus` or higher, be able to eat, and be at exactly 50% hard fullness or less when selected.
- A mobile recipient uses an available chair at a table within 30 cells when one is in range; otherwise they wait near the selected food storage. An immobile or bed-resting recipient stays in place. Their linked job is explicitly marked no-movement, preserves its lying posture, and cannot create a path to food storage or a dining table. The feeder makes one pickup containing the recipient's complete allocation to the selected target: prepared meals are fed one serving at a time, while a calculated small-food portion is eaten in one action and clamped exactly at the configured percentage.
- One-way feeding uses the recipient's remaining capacity when selecting the one complete carried allocation. If that allocation cannot be reserved or carried in full, the job does not substitute a partial pickup.
- One-way feeding gives both participants recreation gradually at 0.60x the standard rate and grants the same mutual social-opinion memory as the shared-meal activity only when the recipient reaches the cap. A time-limited or food-limited session keeps its earned recreation but does not receive a completion refill.

## Bedside and immobile feeding

- An awake recipient may be unable to move, lack Manipulation, be resting in bed, or be downed while still capable of Eating. They remain at their current position instead of being ordered to walk to food storage.
- Meal acceptability is checked against the recipient, while reachability and reservation are checked against the mobile feeder. This allows stored meals to be delivered to a pawn who cannot path to them.
- The mobile pawn makes one collection trip with a combined stack calculated for both participants. Prepared servings remain individual. For small/raw/stackable food, the recipient consumes only their calculated portion and the feeder then consumes their separately calculated portion from the same carried stack. No second food search is permitted. Unused food is left intact beside the bed when the session ends.
- An adult who is personally/categorically exempt, or who has Neutral/Neutral-/Dislike/Hate weight opinion, uses the delivery-only path and never eats, so their own fullness and diet mode remain irrelevant.
- A normal non-exempt Neutral+ or higher pawn at 50% hard fullness or less can initiate a bedside shared meal. They feed the immobile recipient first, then fetch and eat their own capacity-matched meals beside the recipient.
- Both bedside paths use the configured 60–90% target, capacity-aware one-collection allocation, completion latch, three-hour limit, gradual 0.70x recreation gain and the same social-memory rules as the other variants.

Recreation selection gives Feed Other a competitive base weight of **3.0** after first confirming that a valid partner and reachable meal actually exist. Neutral+ uses 1.00x, Like 1.15x, Love 1.30x, Fanatical 1.45x, and Extreme 1.50x. A current romantic partner adds 1.25x, or 1.35x for bedside care; a close friend at +40 social opinion adds 1.10x. Both participants below 40% fullness add +0.50, a recipient below 25% adds +0.75, and a nearby practical meal/dining setup adds +0.25, while food over 30 cells away applies -0.50. The result is capped at 5.0. Walking and swimming remain at 0.95x. After an autonomous shared-meal session starts, both participants normally wait six in-game hours before initiating another. After an autonomous one-way session starts, the non-eating feeder normally waits three hours and the fed recipient waits six hours. Every same-pair cooldown is six hours. A proposed pairing involving Like/Love/Fanatical/Extreme ignores participant and pair cooldowns, while the saved timestamps continue to govern unrelated lower-opinion pairings. Direct right-click feeding ignores cooldowns for every opinion.

## Installation

Extract this archive into the existing `RimRound` mod directory so that the included `1.6` and `Source` folders merge with the existing folders. Keep RimRound and its normal dependencies enabled. Start RimWorld after extraction; no new mod-list entry is required.

This patch targets RimWorld 1.6 and the supplied RimRound 1.6 beta assembly. Its automatic milk toggle uses RimWorld's built-in lactation class when Biotech is active, but remains safe to load without Biotech: when the Lactating def is unavailable, the toggle and production logic simply remain inactive. Its orbital movement relief recognizes Odyssey space maps when present and otherwise remains inactive. No other feature requires a DLC.

## Main tuning points

- Fullness threshold, food search radius, and timeout: `Source/RimRoundFeedOther/FeedOtherUtility.cs`
- Recreation frequency: `1.6/Defs/JoyGiverDefs/RimRound_FeedOtherJoyGiver.xml`
- Recreation kind and Social XP: `1.6/Defs/JobDefs/RimRound_FeedOtherJobDefs.xml`
- Opinion strength, duration, and stacking: `1.6/Defs/RimRound_FeedOtherThoughtDefs.xml`
- Conversation text and filters: `1.6/Defs/RimRound_FeedOtherConversations.xml`
- Beta trait probabilities and save migration: `Source/RimRoundFeedOther/RimRoundTraitGenerationBeta.cs`
- Beta trait definitions and real Eating Speed factors: `1.6/Defs/TraitDefs/RimRound_FeedOtherBetaTraitDefs.xml` and `1.6/Patches/RimRound_FeedOtherBetaTraitPatches.xml`



## v1.0.43 — expanded food and one-way chair searches

- Food storage searches now reach up to 30 cells instead of 15 for shared meals, one-way feeding, manual feeding and feeding-in-place food collection.
- Mobile one-way/manual feeding recipients now look for a usable dining chair within 30 cells instead of 6.
- Shared-meal seating is unchanged: both pawns still use paired seats at the same table and the two selected seats must remain within 6 cells of each other.
- The one-way chair search remains pathing-, reservation- and table-aware; when no practical chair is found, the recipient still meets the feeder instead.

## v1.0.41 — repeated small-food top-up completion

- Shared meals, one-way feeding and feeding-in-place now count successful non-meal top-up rounds separately for each eater.
- The normal completion point remains about 88% of the current hard limit (90% target minus the normal 2% tolerance).
- This version originally used an approximately 85% fallback after two top-up rounds. v1.0.45 supersedes that rule with a firmer final-top-up stop: the first top-up may complete at Very Full, and the second completed top-up always ends that pawn's feeding phase.
- Prepared meals still use the normal completion threshold, and stomach growth/current hard capacity remain respected when choosing portion sizes.

## v1.0.40 — context-aware feeding conversations

- Shared meals, one-way feeding and feeding-in-place sessions now generate their
  own themed social conversations instead of relying on unrelated random vanilla
  chitchat.
- Conversation context is selected from the current activity, Starting/Ongoing/
  Finishing phase, relationship (general, friend, family, romantic or strained),
  both pawns' RimRound weight opinions, and whether both, one or neither pawn can
  speak.
- A pawn unable to speak may still be spoken to and responds through fitting
  gestures. When neither pawn can speak, the interaction is described entirely
  through non-verbal behaviour.
- Feeding-in-place dialogue is ordinary recreation and bonding; it does not
  assume illness, hospital care or a medical setting.
- Shared-meal tone considers both pawns. For one-way and feeding-in-place
  sessions, positive/neutral/reluctant dialogue follows the fed pawn's RimRound
  weight opinion so the feeder cannot inherit the recipient's reluctance.
- Dialogue uses a short opening delay, random 900–1500 tick intervals, per-session
  line caps, recent-line avoidance and one immediate finishing line. The three
  phases are state-based, so an enlarged RimRound meal may naturally skip the
  middle phase without causing message spam.
- Text is stored in the single editable file
  `1.6/Defs/RimRound_FeedOtherConversations.xml`. Lines can be changed or added
  without recompiling the DLL.
- Social-log entries store the rendered sentence rather than a temporary runtime
  definition, so existing saves remain valid after ordinary dialogue edits.
- The controlled feeding toils no longer run `RandomSocialMode.SuperActive`,
  preventing unrelated vanilla interactions from firing over the custom dialogue.
- Conversation lines are flavour only: they add no repeated mood or opinion
  bonuses and cannot create social fights.


## v1.0.44 — conversation role correction

- One-way feeding dialogue now distinguishes the **Feeder** from the **FedPawn**.
- Role-locked XML entries prevent a line written from the fed pawn's perspective from being spoken by the feeder, and vice versa.
- The reluctant tone for one-way feeding is now based on the fed pawn's RimRound weight opinion rather than combining both pawns' attitudes.
- Both-speaking sessions still alternate naturally, but role-correct lines take priority when the preferred speaker has a valid line.
- One-speaking sessions have separate feeder-speaking and fed-pawn-speaking pools.
- Fully nonverbal entries use stable feeder/fed-pawn roles.
- New editable XML filter: `<speakerRole>Either|Feeder|FedPawn|MutualNarration</speakerRole>`.
- New stable text tokens: `[FEEDER_nameDef]`, `[FEDPAWN_nameDef]`, `[FEEDER_possessive]`, and `[FEDPAWN_possessive]`.
- Corrected lines such as the fed pawn learning to understand the feeder's interest, preventing output like “Sparkles said Sparkles understood her interest.”


## v1.0.45 — cooldown and eligibility revision

- Expanded non-eating autonomous feeder eligibility to include exact `WeightOpinion.Neutral` as well as Neutral−, Dislike and Hate.
- Reduced the autonomous one-way feeder cooldown from six hours to three hours.
- Kept the fed recipient cooldown at six hours.
- Reduced the autonomous mobile same-pair cooldown from twelve hours to six hours; bedside pairs remain six hours.
- No Feed Other chance values or multipliers were changed.
- Existing cooldown expiry timestamps already present in a save are left intact; new autonomous starts use the revised durations.


## v1.0.45 — final small-food top-up stop

- Small/raw/stackable food batches are now treated as the final top-up phase rather than an endlessly repeatable feeding source.
- A counter advances only after an entire fetched top-up batch has been successfully ingested; individual berries inside that batch do not count as separate runs.
- After the first top-up batch, reaching RimRound's Very Full threshold immediately latches that pawn complete.
- The second completed top-up batch always latches that pawn complete, even when digestion has already lowered the displayed fullness bar.
- Prepared meals are unaffected and continue using the normal 90% target / approximately 88% completion band.
- Completion remains separate for each eater in shared meals and feeding-in-place sessions.
- Any unused carried food is still dropped intact when the session ends.

## v1.0.46 — safe Very Full and single collection

- Replaced the Feed Other 90% About to Burst target with RimRound's exact 70% Very Full threshold.
- The CurrentFullness prefix clamps Feed Other increases before RimRound observes them, so a final small-food overshoot cannot momentarily enter Painfully Full or collapse the pawn.
- Small/raw/stackable foods use the minimum whole-item count required to reach Very Full. The item is consumed normally, but excess fullness from the final unit is intentionally discarded.
- Shared-meal pawns and one-way feeders reserve and collect each eater's full calculated allocation before eating starts. Partial reservations and later storage returns are disabled.
- Bedside shared feeding collects one combined stack for recipient and feeder. A small-food stack can be split into two calculated portions while remaining one pickup.
- If a complete allocation cannot fit in the source stack or the collector's carry capacity, or if it becomes unavailable after collection, the session ends cleanly instead of fetching again.
- Existing recreation chances, relationship/opinion multipliers, cooldowns and unrelated RimRound eating remain unchanged.

## v1.0.47 — initial bedside posture restoration

- Added an initial attempt to restore `PawnPosture.LayingInBed` after the temporary linked recipient job ended.
- Later testing showed posture restoration alone was insufficient because the patient's actual `LayDown` job had already been replaced. v1.0.49 supersedes this approach by leaving the native bed-rest job running throughout feeding.


## v1.0.48 — prepared-meal priority and safe final serving

- Fixed prepared meals being rejected whenever one whole serving was larger than the pawn's remaining gap to Very Full. This rejection was why large stacks of milk or other small foods could be selected despite stored meals being available.
- Required counts now round upward for prepared meals as well as smaller food items. A final 0.90-nutrition meal may be consumed even when only a fraction is needed.
- The existing Feed Other fullness prefix clamps the applied increase at exact 70% Very Full before RimRound observes it, so the unused portion of the final meal is intentionally wasted without entering Painfully Full.
- Candidate order remains prepared meals first across the complete search radius. If no meal stack can supply the full one-pickup allocation, selection falls back by RimWorld food preferability through sensible snacks, chocolate, milk and acceptable raw foods.
- Kibble, baby food, desperate-only foods, drugs and foods that would currently give the eater a negative ingestion mood thought remain rejected.
- Shared meals, one-way feeding and bedside feeding retain the v1.0.46 single-collection rule; no route returns to storage after eating begins.
- The v1.0.47 bedside posture correction, current cooldowns, eligibility rules, chance values and multipliers are unchanged.


## v1.0.49 — native bedside patient jobs

- Reworked bed-bound and remain-in-place feeding to match vanilla doctor feeding and tending: only the caregiver runs a Feed Other job; the patient retains their normal `LayDown` job.
- The patient's bed reservation, `LayingInBed` posture, rest gain, healing, sleep state and bed ownership remain authoritative for the entire interaction.
- Removed meal-target writes into a bed-bound patient's current job, preventing Feed Other from overwriting the bed target of `LayDown`.
- Feeder synchronization now approaches a stationary native-bed recipient directly without requiring `RR_BeFedOtherPartner`.
- Mobile dining recipients continue to use the linked recipient job and retain existing chair/meeting behaviour.
- Both participants continue gaining the intended gradual recreation during bedside feeding.
- Already-running legacy sessions saved with `RR_BeFedOtherPartner` use interruption cleanup so RimWorld immediately chooses the real bed-rest job instead of passing through the successful-job posture transition.
- Prepared-meal priority, safe Very Full clamping, single collection, cooldowns, eligibility, chance values and multipliers are unchanged.


## v1.0.51 — clean bedside runtime repair

- Rebuilt the complete assembly from source against the exact RimWorld 1.6, RimRound, Harmony and Unity references supplied with the main RimRound archive.
- Removed the malformed binary-patched IL in `FindBedsideRecipientMeal`; the bedside food-selection closure is now normal compiler-generated IL.
- Stopped calling `JoyUtility.JoyTickCheckEnd` on recipients who correctly retain native `LayDown` or `Wait_Downed` jobs. That vanilla helper expects the current job to define a joy kind and was throwing a `NullReferenceException` at one-way feeding toil index 6.
- Added null-safe direct Feed Other recreation gain for both participants without reading or replacing the recipient's current job.
- Preserved the v1.0.50 manual-start protection: downed or movement-incapable recipients keep their native bed-rest job and only the feeder receives the custom job.
- The previously planned healthy-sleeper fallback behaviour remains paused and is not included in this repair build.

## v1.0.50 — manual bedside start hotfix

- Fixed the right-click manual feeding command cancelling an immobile/downed recipient's native `LayDown` job during pre-start preparation.
- Remain-in-place recipients are no longer undrafted, woken, stopped, queue-cleared or retasked by the float-menu command.
- Only the selected feeder is prepared before `RR_FeedOtherOneWay` starts. The feeder-side job then approaches and feeds the recipient while their native bed-rest job remains authoritative.
- Mobile recipients continue to be prepared and use the linked dining-chair/meet-feeder job as before.
- The supplied before/after saves showed the old failure as `LayDown`/`LayingInBed` changing to `Wait_Downed` with no queued bed job and no feeder job start.
- Sleeping-mobile-recipient fallback remains deferred and was not added in this hotfix. Food priority, exact Very Full clamping, single collection, cooldowns, eligibility, chance values and multipliers are unchanged.

## v1.0.52 — reliable Very Full completion and repeat collection

- Removed the two-top-up close-enough completion rule; every participant must genuinely cross 70% of current hard capacity.
- Rechecks live fullness after each serving and returns for additional food when the carried allocation was insufficient.
- Allows partial collections from short stacks or limited carry space and adds one spare prepared serving when possible.
- Extends session nutrition and fullness clamping to native-bed recipients by resolving the active meal from the caregiver-side job.
- Immediately grants the Very Full mood/discomfort thought and sets the appropriate saved driver latch when the threshold is crossed.
- Snaps epsilon-close results to exact 70%, preventing missed buffs from floating-point rounding.
- Applies consistently to normal shared meals, one-way feeding and bedside shared feeding.


## v1.0.53 — prisoner food and Fatten work repair

- Repairs prisoner diet ranges that were saved as invalid `0 / 0` or unavailable negative values. The normal 30% lower and 90% upper defaults are restored only when both targets are genuinely uninitialised; valid player-selected ranges are preserved.
- Runs the range repair after prisoner spawning/status changes and immediately before RimRound's self-eating and patient-hunger checks, preventing zero ranges from suppressing both prisoner eating and bedside feeding.
- Normal prisoner interaction modes remain vanilla: mobile prisoners receive food deliveries when hungry, while downed or medically resting prisoners are fed directly.
- Replaces the non-functional Fatten delivery-only route with `RR_FattenPrisonerDirectFeed`, a registered warden job that picks up acceptable food and feeds the prisoner directly.
- Fatten begins when the configured lower target is crossed and continues across additional warden jobs until the configured upper target is reached.
- Prepared meals remain preferred. Short source stacks and limited carrying capacity are supported; the continuation state requests another feeding job instead of abandoning the target.
- Mobile prisoners are briefly held in place only after the warden reaches them. Downed and sleeping prisoners retain their native bed/downed job and posture.
- Fullness and Hybrid modes clamp the final whole serving to the configured upper fullness target. Nutrition mode remains protected by the 95% current-hard-limit safety cap.
- Prisoner food policy, edibility, reachability, reservations, prison security and disliked-food checks remain respected.

## v1.0.54 — bounded prisoner Fatten sessions

- Suppresses vanilla/RimRound `DeliverFood` results while the prisoner interaction is `RR_Fatten`, preventing wardens from leaving an additional uncontrolled meal stack in the prison cell.
- Keeps normal/Maintain prisoner behaviour unchanged. Downed, movement-incapable, non-standing or bed-resting prisoners are excluded from the special Fatten route and may only receive vanilla medical feeding when genuinely hungry.
- Prepared meals are limited to exactly one serving per direct-feed job. Fullness and configured target state are recalculated after that serving before another job can start.
- Stackable small foods may still use a calculated bounded count, but never exceed the remaining physical session requirement.
- Every Fatten eating route is clamped to the lower of the configured upper fullness target and exact Very Full at 70% of current hard stomach capacity. Nutrition mode therefore stops at Very Full rather than the former 95% safety cap.
- A Fatten programme remains active across sessions until its configured upper diet target is reached, but reaching the physical session target starts a saved six-hour prisoner cooldown.
- While that cooldown is active, no new special Fatten job can start. Digestion immediately lowering fullness cannot cause another feeding session.
- Prisoner self-eating is suppressed while a warden is actively feeding them or once the physical session target has been reached, preventing existing cell food from stacking on top of the controlled serving.


## v1.0.55 — prisoner target warning fix

- Prevented automatic prisoner range repair from assigning a physically dangerous 90% fullness target.
- Fullness and Hybrid Fatten ranges migrate to the safe 70% hard-capacity maximum without opening RimRound's lethal-fullness confirmation dialog during UI drawing.

## v1.0.56 — deterministic delivery and locked-bed Fatten

- Replaces vanilla/RimRound prisoner food-delivery decisions with one serving per successful delivery and a saved two-hour cooldown for every non-Fatten interaction mode.
- Ignores the vanilla room-food suppression heuristic, so a genuinely hungry prisoner cannot remain unfed because stale or unusable food was counted in the room.
- Adds `RR_PrisonerMealDelivery`, `RR_FattenPrisonerBedLock` and the revised caregiver-only `RR_FattenPrisonerDirectFeed` jobs.
- Locks Fatten prisoners into a valid prisoner bed, feeds one serving at a time until exact Very Full, disables RimRound reluctance chat, suppresses self-eating and blocks prison-break participation.

## v1.0.57 — Painfully Full Fatten cycle with fullness reset

- Raises the dedicated prisoner Fatten stop point from exact Very Full at 70% to exact **Painfully Full at 80% of current hard stomach capacity**.
- Keeps one serving per warden job and clamps the final indivisible serving at exact 80%, so an oversized meal cannot push the prisoner farther through the Painfully Full band.
- Replaces immediate retriggering with a saved fullness-hysteresis latch. Once 80% is reached, all further Fatten feeding remains disabled until the prisoner falls **strictly below 10%** of their current hard capacity.
- The prisoner remains bed-locked, cannot self-eat, cannot receive RimRound reluctance chat and cannot participate in prison breaks throughout the waiting period.
- Maintain/default interaction deliveries remain one serving every two in-game hours while hungry and are otherwise unchanged.

## v1.0.58 — Eating Speed-aware assisted and shared meals

- Normal self-eating remains `baseIngestTicks / Eating Speed`.
- Shared-meal eating uses 90% of the eater's live stat-adjusted duration.
- Assisted feeding uses 85% of the recipient's live stat-adjusted duration across Feed Other, bedside feeding, prisoner Fatten and vanilla patient/prisoner feeding.
- Fullness penalties, consciousness, mouth/tongue injuries, genes, equipment and other Eating Speed modifiers are recalculated when chewing begins.

## v1.0.59 — sleeping-pawn bed fallback

- Adds healthy sleeping mobile pawns as the final autonomous recipient fallback only after no valid awake recipient and meal combination exists.
- Supports both one-way Feed Other and shared bedside meals without changing the existing awake-candidate priority.
- Suspends the sleeper's current `LayDown` job, keeps them awake and locked to their existing bed through the full event, and prevents pathing to food, chairs or the feeder.
- Restores the in-bed posture and resumes the suspended `LayDown` job whenever the event succeeds, times out, fails or is cancelled.
- Downed/immobile native patient jobs and manual right-click targeting remain unchanged.

## v1.0.60 — manual sleeping-recipient bed preservation

- Extends the v1.0.59 awake-in-bed recipient path to the manual right-click `Feed [name] until very full` command.
- Detects the clicked pawn while their sleeping `LayDown` job is still active, so float-menu preparation no longer wakes them, ends the job or sends them toward a dining table.
- Suspends the existing `LayDown` job with its bed reservation intact, keeps the recipient awake and locked in that same bed for the full one-way feeding session, and brings every serving to the bedside.
- Uses the same safe mobile-bed lock used by the autonomous fallback for an ordinary interruptible sleeping `LayDown` job.
- On success, timeout, food exhaustion, failure or cancellation, the temporary recipient job ends and the original `LayDown` job resumes immediately without a visible standing transition.
- Awake manual recipients continue to use the normal nearby-chair or meet-feeder behaviour; downed and genuinely immobile recipients continue to retain their native patient job.



## v1.0.61-beta.1 — generated trait categories and Like+ repeat feeding

- Branches from the stable v1.0.60 manual sleeping-bed fix without modifying that rollback archive.
- Rebalances newly generated weight opinions to 3/7/12/36/20/12/7/3 percent from Hate through Fanatical. Existing pawns keep their current opinion.
- Removes the four SwellGlow eating traits and three elasticity traits from vanilla random trait slots, then guarantees one eating style and one elasticity marker after ordinary traits and RimRound's weight opinion are assigned.
- Adds `Average eater` and `Average stomach elasticity` neutral traits. Existing/editor-selected category traits are preserved; missing categories are added once to living pawns on new-game start or save load.
- Gives the existing eating traits real `EatingSpeed` factors of 0.75, 0.90, 1.10 and 1.25 while retaining their existing RimRound fullness-penalty effects.
- Changes new-session eligibility from below 65% to **50% hard fullness or less**, allowing exact 50% and blocking only values above it.
- Makes autonomous cooldown checks pairing-specific: any pairing involving Like, Love, Fanatical or Extreme ignores participant and pair cooldowns, but cooldown timestamps remain saved for unrelated lower-opinion pairings.

Version 1.0.62-beta.1 removes Gluttonium from the shared vanilla surface-ore lottery and generates it in a separate post-rock pass, preserving normal steel, compacted machinery and precious-ore counts. Qualifying map generators receive approximately 1.2 Gluttonium veins per 10,000 cells. Its attempted Mine vein compatibility change was incomplete because the required vanilla `building.veinMineable` field had not yet been identified.

## v1.0.64-beta.1 — post-meal social recreation

- Successful Share Meal, one-way Feed Other, and bedside feeding sessions continue as social recreation while either participant remains below 95% recreation.
- Adds a one-hour safety cap, sleeping-recipient return-to-sleep protection, context-aware post-meal dialogue, and the four-hour +2 `Enjoyed an after-meal chat` memory after 600 ticks.
- Dialogue now understands broad RimRound size, gain/lose/maintain goals inferred from weight opinion and current size, Very Full reactions, fullness level, speech capability and relationship.
- Keeps all v1.0.63 Mine vein and independent Gluttonium-generation fixes unchanged.

## v1.0.63-beta.1 — vanilla Mine vein compatibility

- Adds `<veinMineable>true</veinMineable>` directly to `RR_GluttoniumOre` through a patch operation.
- Fixes the exact 1.5/1.6 vanilla eligibility check that allowed ordinary Mine but hid the selected-rock **Mine vein** reverse designator.
- Existing Gluttonium ore gains Mine vein after restarting RimWorld with this patch; no new map or save migration is required.
- Removes the unrelated v1.0.62 explicit inherited mining-field and glow-control overrides, preserving RimRound's original fixed definition behaviour while keeping the separate ore-generation pass.
- Surface generation remains 1.2 additional Gluttonium veins per 10,000 cells and no longer reduces vanilla ore selections. Deep scanning remains unchanged.


## v1.0.66-beta.1 — Hoverchair reliability fixes

- Fixes the RimRound hoverchair movement override affecting pawns who are not wearing a chair.
- Missing RimRound perk/component data now correctly defaults to the normal 50% chair speed, rather than 100%.
- Keeps vanilla posture handling instead of forcing every awake chair user to Standing.
- Hides the hoverchair render node whenever the pawn's real posture is lying.
- When a chair user becomes downed, their hoverchair is moved into their personal inventory instead of being dropped and forbidden.
- The stored chair is marked persistently and automatically re-equipped after the pawn is no longer downed, awake, has at least 10% Manipulation, and can wear it without displacing other apparel.
- The chair stays safely in inventory until those conditions are met.

## v1.0.67-beta.1 — Configurable feeding target

- Adds a saved **RimRound Patch** settings tab beside RimRound's bottom-bar settings button.
- Adds a 60–90% Feed Other stop target with a 70% default and one-percent steps.
- Routes shared, one-way, manual and bedside meal planning, completion and final fullness clamping through the selected value.
- Keeps RimRound's real Very Full stage and mood crossing fixed at 70%, and keeps prisoner Fatten fixed at its separate 80% target.
- Shows the chosen percentage in manual feed commands when it differs from the default and warns that 80%+ can collapse or immobilise heavily penalised pawns.

## v1.0.68-beta.1 — Full configurable settings tab

- Expands the RimRound Patch window into six scrollable pages with persistent feature switches, bounded sliders, tooltips, dependency disabling, page resets and a complete reset.
- Makes feeding start/stop values, session duration, autonomous frequency, participant/feeder/pair cooldowns, post-meal recreation, idle eating, persuasion, prisoner feeding, Gluttonium generation and orbital movement relief configurable.
- Preserves v1.0.67-beta.1 behaviour as every default and lets already-running activities finish safely when an autonomous feature is switched off.
- Keeps per-pawn milk choices, existing pawn traits and saved cooldown timestamps intact when their global feature is disabled.
- Adds a distinct patch settings icon and retains access through both the bottom bar and RimWorld's normal Mod Settings list.

## v1.0.68-beta.2 — Hoverchair startup fix

- Fixes the red Harmony `Undefined target method` startup error caused by the removed `PawnRenderTree.ProcessApparel` method in RimWorld 1.6.
- Moves lying-hoverchair suppression to `DynamicPawnRenderNodeSetup_Apparel.ShouldAddApparelNode` and safely skips only that visual hook if a future game update removes the target.
- Restores successful initialization of every other patch feature, including the configurable settings pages.

## v1.0.69-beta.1 — Food Network v2

- Replaces the original shared-float food pipe system with transactional FIFO batches, map-local topology, migrated storage, working processor/tank/dispenser integration, bed-linked automatic feeders and separate distiller ports.
- Adds the seventh Food network settings page and retains a save-compatible classic-network fallback.

## v1.0.69-beta.2 — Dispenser food-AI and valve icon fix

- Fixes hungry pawns receiving no job and a repeated `ThinkNode_PrioritySorter` null-reference while a network-fed dispenser reports ready.
- Adds a guarded no-other-food fallback that issues one normal, reservable dispenser ingestion job.
- Changes the feeding-tube valve's Architect icon from a conduit to the familiar power-switch graphic.

## v1.0.69-beta.3 — Diet-bar nutrient paste dispenser

- Replaces the dispenser's imitation liquid-food job with a genuine `MealNutrientPaste` collected and eaten through vanilla-style toils.
- Sizes each meal from the eater's active Nutrition, Hybrid or Fullness bar and the withdrawn batch's true density, with partial serving when network supply is insufficient.
- Covers the outer inventory-versus-map food-optimality comparison that could still score the non-ingestible dispenser building and fail inside pawn food AI.
- Changes dispenser access to vanilla-style non-exclusive collection while retaining atomic network withdrawal and ingredient/fullness conservation.

## v1.0.69-beta.4 — Isolated self-feeding selection

- Removes the liquid-food dispenser from general wardening, patient-feeding, inventory and animal food searches.
- Lets vanilla finish choosing ordinary pawn food first, then compares a ready Food Network v2 dispenser only for the pawn's normal self-feeding job.
- Keeps diet-bar-sized real nutrient-paste meals and all transactional storage behaviour from beta.3.

## v1.0.69-beta.5 — Food-source list repair

- Fixes existing and newly built liquid-food dispensers remaining registered in vanilla's global food-source lists after the original RimRound search patches were removed.
- Repairs both map and region food lists on save load and when switching between Food Network v2 and the classic network.
- Prevents vanilla from scoring the non-ingestible dispenser building before the dedicated Food Network v2 self-feeding job can run, eliminating the repeated `ThinkNode_PrioritySorter` null-reference path.

## v1.0.69-beta.6 — Double-bed automatic-feeder fix

- Replaces the legacy first-pawn feeder authority with independent tracking, feeding, tube drawing and movement locking for every occupied sleeping slot.
- Fixes advanced-feeder radio recreation reserving a double-bed cell as a one-person chair, which caused the shown `Could not reserve RoyalBed` and `TryMakePreToilReservations returned false` errors.
- Repairs transient bed-posture loss back to the pawn's existing linked bed while retaining safe cleanup when the pawn or bed truly leaves the link.

## v1.0.69-beta.7 — Density-independent automatic-feeder flow

- Changes Gain and Maximum Gain from a fixed-nutrition pulse to a configurable percentage of each pawn's hard stomach capacity.
- Defaults to 10% capacity per pulse and preserves exact target clamping, personal fullness multipliers, FIFO density, ingredients and network conservation.
- Keeps the existing 0.10-nutrition pulse exclusively for Survival and Maintain, preventing twice-distilled food from making fullness gain three times slower.

## v1.0.69-beta.8 — Oversized single-bed reliability

- Rebuilds RimRound's 3x3 `NotRegalBed` as a complete one-occupant bed contract: one centred rotation-aware head/foot lane, one real vanilla bed owner and normal bed pathing.
- Adds 3x3-safe bedside interaction cells and centred head-facility validation, removing the unsupported-size exception used by tending, childbirth and other interaction jobs.
- Stops new beds defaulting to medical while retaining the normal toggle, and migrates valid legacy assignments and sleeping positions without changing the bed defName or save identity.

## v1.0.69-beta.9 — South-facing oversized-bed texture

- Replaces only the south-facing `NotRegalBed` artwork with the north-facing artwork rotated 180 degrees.
- Rotates the matching material mask identically so stuff colouring and shader behaviour remain aligned.
- Keeps the sleeper behind the solid south headboard while leaving all beta.8 bed mechanics and every other orientation untouched.

## v1.0.69-beta.10 — Corrected south-facing texture slot

- Rolls back beta.9's override of the visually north-facing bed by restoring RimRound's original `south` texture and material mask.
- Applies the required 180-degree rotated artwork to the opposite `north` file slot, which the bed uses for the visually south/downward orientation.
- Leaves all beta.8 bed mechanics and the east/west artwork unchanged.
