using RimRound.FeedingTube;
using RimRound.FeedingTube.Comps;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimRound.FeedOther
{
    internal static class FoodNetworkV2Constants
    {
        public const float Epsilon = 0.0001f;
        public const float DispenserMealNutrition = 0.90f;
        public const int MaximumDispenserMealsPerTrip = 4;
        public const string PipeDefName = "RR_TD_FeedingTubeConduit";
        public const string ValveDefName = "RR_TD_FeedingTubeValve";
        public const string SmallTankDefName = "RR_TD_FoodStorageVat_Small";
        public const string LargeTankDefName = "RR_TD_FoodStorageVat_Large";
        public const string MassiveTankDefName = "RR_TD_FoodStorageVat_Massive";
        public const string ProcessorDefName = "RR_TD_FeedingTubeFoodProcessor";
        public const string FaucetDefName = "RR_TD_FoodFaucet";
        public const string BasicFeederDefName = "RR_AutoFeeder";
        public const string AdvancedFeederDefName = "RR_AdvancedAutoFeeder";
        public const string DistillerDefName = "RR_TD_NutrientDistillery";

        public static bool IsStorage(Thing thing)
        {
            return thing != null && thing.TryGetComp<FoodNetStorage_ThingComp>() != null;
        }

        public static bool IsDistiller(Thing thing)
        {
            return thing != null && thing.def != null &&
                thing.def.defName == DistillerDefName;
        }

        public static bool IsFoodNetworkThing(Thing thing)
        {
            return thing != null &&
                thing.TryGetComp<FoodTransmitter_ThingComp>() != null;
        }
    }

    /// <summary>
    /// One conserved FIFO portion of liquid food. Nutrition is the useful food
    /// energy; fullness is the physical stomach volume. Their ratio is never
    /// inferred from a later transfer, so distillation and mixed tanks cannot
    /// create or erase food.
    /// </summary>
    public sealed class FoodBatchV2 : IExposable
    {
        public float nutrition;
        public float fullness;
        public int createdTick;
        public List<ThingDef> ingredients = new List<ThingDef>();

        public FoodBatchV2()
        {
        }

        public FoodBatchV2(
            float nutrition,
            float fullness,
            IEnumerable<ThingDef> ingredients,
            int createdTick)
        {
            this.nutrition = Mathf.Max(0f, nutrition);
            this.fullness = Mathf.Max(0f, fullness);
            this.createdTick = createdTick;
            if (ingredients != null)
            {
                this.ingredients = ingredients
                    .Where(delegate(ThingDef ingredient) { return ingredient != null; })
                    .Distinct()
                    .ToList();
            }
        }

        public float FullnessToNutritionRatio
        {
            get
            {
                return nutrition > FoodNetworkV2Constants.Epsilon
                    ? fullness / nutrition
                    : 1f;
            }
        }

        public bool Empty
        {
            get
            {
                return nutrition <= FoodNetworkV2Constants.Epsilon ||
                    fullness <= FoodNetworkV2Constants.Epsilon;
            }
        }

        public FoodBatchV2 Copy()
        {
            return new FoodBatchV2(nutrition, fullness, ingredients, createdTick);
        }

        public FoodBatchV2 Portion(float portionNutrition, float portionFullness)
        {
            return new FoodBatchV2(
                portionNutrition,
                portionFullness,
                ingredients,
                createdTick);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref nutrition, "nutrition", 0f);
            Scribe_Values.Look(ref fullness, "fullness", 0f);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Collections.Look(
                ref ingredients,
                "ingredients",
                LookMode.Def);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (ingredients == null)
                {
                    ingredients = new List<ThingDef>();
                }
                else
                {
                    ingredients.RemoveAll(delegate(ThingDef ingredient)
                    {
                        return ingredient == null;
                    });
                }

                nutrition = Mathf.Max(0f, nutrition);
                fullness = Mathf.Max(0f, fullness);
            }
        }
    }

    public sealed class FoodTankStateV2 : IExposable
    {
        public int thingId;
        public bool legacyMigrated;
        public List<FoodBatchV2> batches = new List<FoodBatchV2>();

        public FoodTankStateV2()
        {
        }

        public FoodTankStateV2(int thingId)
        {
            this.thingId = thingId;
        }

        public float StoredFullness
        {
            get
            {
                EnsureCollections();
                return batches.Sum(delegate(FoodBatchV2 batch)
                {
                    return batch == null ? 0f : batch.fullness;
                });
            }
        }

        public float StoredNutrition
        {
            get
            {
                EnsureCollections();
                return batches.Sum(delegate(FoodBatchV2 batch)
                {
                    return batch == null ? 0f : batch.nutrition;
                });
            }
        }

        public float AverageFullnessToNutritionRatio
        {
            get
            {
                float nutrition = StoredNutrition;
                return nutrition > FoodNetworkV2Constants.Epsilon
                    ? StoredFullness / nutrition
                    : 1f;
            }
        }

        public void Add(FoodBatchV2 batch)
        {
            if (batch == null || batch.Empty)
            {
                return;
            }

            EnsureCollections();
            FoodBatchV2 last = batches.Count == 0 ? null : batches[batches.Count - 1];
            if (last != null && CanMerge(last, batch))
            {
                last.nutrition += batch.nutrition;
                last.fullness += batch.fullness;
                last.createdTick = Mathf.Min(last.createdTick, batch.createdTick);
                return;
            }

            batches.Add(batch.Copy());
        }

        public void Purge()
        {
            EnsureCollections();
            batches.Clear();
        }

        public void RemoveEmptyBatches()
        {
            EnsureCollections();
            batches.RemoveAll(delegate(FoodBatchV2 batch)
            {
                return batch == null || batch.Empty;
            });
        }

        private static bool CanMerge(FoodBatchV2 first, FoodBatchV2 second)
        {
            if (!Mathf.Approximately(
                    first.FullnessToNutritionRatio,
                    second.FullnessToNutritionRatio))
            {
                return false;
            }

            if (first.ingredients.Count != second.ingredients.Count)
            {
                return false;
            }

            return first.ingredients.All(delegate(ThingDef ingredient)
            {
                return second.ingredients.Contains(ingredient);
            });
        }

        private void EnsureCollections()
        {
            if (batches == null)
            {
                batches = new List<FoodBatchV2>();
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref thingId, "thingId", 0);
            Scribe_Values.Look(ref legacyMigrated, "legacyMigrated", false);
            Scribe_Collections.Look(
                ref batches,
                "batches",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureCollections();
                RemoveEmptyBatches();
            }
        }
    }

    public sealed class AutoFeederLinkStateV2 : IExposable
    {
        public int feederThingId;
        public bool legacyTargetMigrated;
        public List<int> bedIds = new List<int>();
        // Current occupants are cached only so stale tube hediffs can be
        // removed when an occupant leaves a linked bed.
        public List<int> pawnIds = new List<int>();

        public AutoFeederLinkStateV2()
        {
        }

        public AutoFeederLinkStateV2(int feederThingId)
        {
            this.feederThingId = feederThingId;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref feederThingId, "feederThingId", 0);
            Scribe_Values.Look(
                ref legacyTargetMigrated,
                "legacyTargetMigrated",
                false);
            Scribe_Collections.Look(ref bedIds, "bedIds", LookMode.Value);
            Scribe_Collections.Look(ref pawnIds, "pawnIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                bedIds = bedIds ?? new List<int>();
                pawnIds = pawnIds ?? new List<int>();
            }
        }
    }

    /// <summary>
    /// Saved state is game-owned rather than map-owned so minified or moved
    /// tanks retain their exact batches and advanced feeders retain bed links.
    /// </summary>
    public sealed class FoodNetworkV2GameComponent : GameComponent
    {
        private List<FoodTankStateV2> tankStates = new List<FoodTankStateV2>();
        private List<AutoFeederLinkStateV2> feederStates =
            new List<AutoFeederLinkStateV2>();

        [Unsaved]
        private Dictionary<int, FoodTankStateV2> tankById;

        [Unsaved]
        private Dictionary<int, AutoFeederLinkStateV2> feederById;

        public FoodNetworkV2GameComponent(Game game)
        {
        }

        public static FoodNetworkV2GameComponent Instance
        {
            get
            {
                return Current.Game == null
                    ? null
                    : Current.Game.GetComponent<FoodNetworkV2GameComponent>();
            }
        }

        public FoodTankStateV2 GetTankState(Building tank, bool create)
        {
            if (tank == null)
            {
                return null;
            }

            EnsureLookups();
            FoodTankStateV2 state;
            if (tankById.TryGetValue(tank.thingIDNumber, out state))
            {
                return state;
            }

            if (!create)
            {
                return null;
            }

            state = new FoodTankStateV2(tank.thingIDNumber);
            tankStates.Add(state);
            tankById.Add(state.thingId, state);
            return state;
        }

        public AutoFeederLinkStateV2 GetFeederState(
            Building_AutoFeeder feeder,
            bool create)
        {
            if (feeder == null)
            {
                return null;
            }

            EnsureLookups();
            AutoFeederLinkStateV2 state;
            if (feederById.TryGetValue(feeder.thingIDNumber, out state))
            {
                return state;
            }

            if (!create)
            {
                return null;
            }

            state = new AutoFeederLinkStateV2(feeder.thingIDNumber);
            feederStates.Add(state);
            feederById.Add(state.feederThingId, state);
            return state;
        }

        public IEnumerable<AutoFeederLinkStateV2> FeederStates
        {
            get
            {
                EnsureLookups();
                return feederStates;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving &&
                !FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                // Keep the shadow state current while the player is using the
                // classic fallback. This makes a later config-file re-enable
                // safe even if it happens before that save is loaded.
                FoodNetworkV2MapComponent.ImportLegacyStorageFromAllMaps();
                FoodNetworkV2AutoFeederUtility.ImportLegacyLinksFromAllMaps();
            }
            Scribe_Collections.Look(
                ref tankStates,
                "rrFoodNetworkV2Tanks",
                LookMode.Deep);
            Scribe_Collections.Look(
                ref feederStates,
                "rrFoodNetworkV2Feeders",
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                tankStates = tankStates ?? new List<FoodTankStateV2>();
                feederStates = feederStates ?? new List<AutoFeederLinkStateV2>();
                RebuildLookups();
            }
        }

        private void EnsureLookups()
        {
            if (tankById == null || feederById == null)
            {
                RebuildLookups();
            }
        }

        private void RebuildLookups()
        {
            tankStates = tankStates ?? new List<FoodTankStateV2>();
            feederStates = feederStates ?? new List<AutoFeederLinkStateV2>();

            tankStates.RemoveAll(delegate(FoodTankStateV2 state)
            {
                return state == null || state.thingId == 0;
            });
            feederStates.RemoveAll(delegate(AutoFeederLinkStateV2 state)
            {
                return state == null || state.feederThingId == 0;
            });

            tankById = new Dictionary<int, FoodTankStateV2>();
            foreach (FoodTankStateV2 state in tankStates)
            {
                tankById[state.thingId] = state;
            }

            feederById = new Dictionary<int, AutoFeederLinkStateV2>();
            foreach (AutoFeederLinkStateV2 state in feederStates)
            {
                feederById[state.feederThingId] = state;
            }
        }
    }

    internal sealed class FoodNetworkTankBindingV2
    {
        public Building building;
        public FoodNetStorage_ThingComp legacyComp;
        public FoodTankStateV2 state;

        public float Capacity
        {
            get { return legacyComp == null ? 0f : legacyComp.Capacity; }
        }

        public float Remaining
        {
            get { return Mathf.Max(0f, Capacity - state.StoredFullness); }
        }
    }

    internal sealed class FoodNetworkDrawSliceV2
    {
        public FoodNetworkTankBindingV2 tank;
        public FoodBatchV2 source;
        public float nutrition;
        public float fullness;
    }

    public sealed class FoodNetworkV2
    {
        private readonly List<Thing> nodes;
        private readonly List<FoodNetworkTankBindingV2> tanks;

        internal FoodNetworkV2(
            int id,
            IEnumerable<Thing> nodes,
            IEnumerable<FoodNetworkTankBindingV2> tanks)
        {
            Id = id;
            this.nodes = nodes == null
                ? new List<Thing>()
                : nodes.Where(delegate(Thing thing) { return thing != null; }).ToList();
            this.tanks = tanks == null
                ? new List<FoodNetworkTankBindingV2>()
                : tanks.Where(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank != null && tank.state != null;
                }).ToList();
        }

        public int Id { get; private set; }

        public IReadOnlyList<Thing> Nodes
        {
            get { return nodes; }
        }

        public float StoredFullness
        {
            get
            {
                return tanks.Sum(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.state.StoredFullness;
                });
            }
        }

        public float StoredNutrition
        {
            get
            {
                return tanks.Sum(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.state.StoredNutrition;
                });
            }
        }

        public float Capacity
        {
            get
            {
                return tanks.Sum(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.Capacity;
                });
            }
        }

        public float RemainingCapacity
        {
            get { return Mathf.Max(0f, Capacity - StoredFullness); }
        }

        public float AverageFullnessToNutritionRatio
        {
            get
            {
                float nutrition = StoredNutrition;
                return nutrition > FoodNetworkV2Constants.Epsilon
                    ? StoredFullness / nutrition
                    : 1f;
            }
        }

        public int BatchCount
        {
            get
            {
                return tanks.Sum(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.state.batches == null
                        ? 0
                        : tank.state.batches.Count;
                });
            }
        }

        public bool HasStorage
        {
            get { return tanks.Count > 0 && Capacity > FoodNetworkV2Constants.Epsilon; }
        }

        public bool CanStore(FoodBatchV2 batch)
        {
            return batch != null && !batch.Empty &&
                RemainingCapacity + FoodNetworkV2Constants.Epsilon >= batch.fullness;
        }

        public bool TryStore(FoodBatchV2 batch)
        {
            if (!CanStore(batch))
            {
                return false;
            }

            float nutritionBefore = StoredNutrition;
            float fullnessBefore = StoredFullness;
            float remainingNutrition = batch.nutrition;
            float remainingFullness = batch.fullness;
            List<FoodNetworkTankBindingV2> orderedTanks = tanks
                .OrderBy(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.Capacity <= FoodNetworkV2Constants.Epsilon
                        ? 1f
                        : tank.state.StoredFullness / tank.Capacity;
                })
                .ThenBy(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.building.thingIDNumber;
                })
                .ToList();

            foreach (FoodNetworkTankBindingV2 tank in orderedTanks)
            {
                if (remainingFullness <= FoodNetworkV2Constants.Epsilon)
                {
                    break;
                }

                float portionFullness = Mathf.Min(tank.Remaining, remainingFullness);
                if (portionFullness <= FoodNetworkV2Constants.Epsilon)
                {
                    continue;
                }

                float fraction = portionFullness / remainingFullness;
                float portionNutrition = remainingNutrition * fraction;
                tank.state.Add(batch.Portion(portionNutrition, portionFullness));
                remainingNutrition -= portionNutrition;
                remainingFullness -= portionFullness;
            }

            SyncLegacyStorageComps();
            bool stored = remainingFullness <= FoodNetworkV2Constants.Epsilon;
            if (stored)
            {
                ValidateConservation(
                    "store",
                    StoredNutrition - nutritionBefore,
                    StoredFullness - fullnessBefore,
                    batch.nutrition,
                    batch.fullness);
            }
            return stored;
        }

        public bool CanDrawNutrition(float nutrition)
        {
            return nutrition > FoodNetworkV2Constants.Epsilon &&
                StoredNutrition + FoodNetworkV2Constants.Epsilon >= nutrition;
        }

        public bool CanDraw(float maximumNutrition, float maximumFullness)
        {
            if (maximumNutrition <= FoodNetworkV2Constants.Epsilon ||
                maximumFullness <= FoodNetworkV2Constants.Epsilon)
            {
                return false;
            }

            return BuildDrawPlan(maximumNutrition, maximumFullness)
                .Any(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.nutrition > FoodNetworkV2Constants.Epsilon &&
                        slice.fullness > FoodNetworkV2Constants.Epsilon;
                });
        }

        public bool TryPreviewExactNutrition(float nutrition, out FoodBatchV2 batch)
        {
            batch = null;
            if (nutrition <= FoodNetworkV2Constants.Epsilon)
            {
                return false;
            }

            List<FoodNetworkDrawSliceV2> plan = BuildDrawPlan(
                nutrition,
                float.MaxValue);
            float plannedNutrition = plan.Sum(delegate(FoodNetworkDrawSliceV2 slice)
            {
                return slice.nutrition;
            });
            if (plannedNutrition + FoodNetworkV2Constants.Epsilon < nutrition)
            {
                return false;
            }

            float plannedFullness = plan.Sum(delegate(FoodNetworkDrawSliceV2 slice)
            {
                return slice.fullness;
            });
            int createdTick = plan.Count == 0
                ? CurrentTick
                : plan.Min(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.source.createdTick;
                });
            List<ThingDef> ingredients = plan
                .SelectMany(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.source.ingredients ?? new List<ThingDef>();
                })
                .Where(delegate(ThingDef ingredient) { return ingredient != null; })
                .Distinct()
                .ToList();

            batch = new FoodBatchV2(
                plannedNutrition,
                plannedFullness,
                ingredients,
                createdTick);
            return !batch.Empty;
        }

        public bool TryDrawExactNutrition(float nutrition, out FoodBatchV2 batch)
        {
            return TryDraw(
                nutrition,
                float.MaxValue,
                true,
                out batch);
        }

        public bool TryDraw(
            float maximumNutrition,
            float maximumFullness,
            bool requireMaximumNutrition,
            out FoodBatchV2 batch)
        {
            batch = null;
            if (maximumNutrition <= FoodNetworkV2Constants.Epsilon ||
                maximumFullness <= FoodNetworkV2Constants.Epsilon)
            {
                return false;
            }

            float nutritionBefore = StoredNutrition;
            float fullnessBefore = StoredFullness;
            List<FoodNetworkDrawSliceV2> plan = BuildDrawPlan(
                maximumNutrition,
                maximumFullness);
            float plannedNutrition = plan.Sum(delegate(FoodNetworkDrawSliceV2 slice)
            {
                return slice.nutrition;
            });

            if (plannedNutrition <= FoodNetworkV2Constants.Epsilon ||
                (requireMaximumNutrition &&
                 plannedNutrition + FoodNetworkV2Constants.Epsilon < maximumNutrition))
            {
                return false;
            }

            float drawnNutrition = 0f;
            float drawnFullness = 0f;
            int createdTick = int.MaxValue;
            List<ThingDef> ingredients = new List<ThingDef>();

            foreach (FoodNetworkDrawSliceV2 slice in plan)
            {
                slice.source.nutrition = Mathf.Max(
                    0f,
                    slice.source.nutrition - slice.nutrition);
                slice.source.fullness = Mathf.Max(
                    0f,
                    slice.source.fullness - slice.fullness);
                drawnNutrition += slice.nutrition;
                drawnFullness += slice.fullness;
                createdTick = Mathf.Min(createdTick, slice.source.createdTick);
                foreach (ThingDef ingredient in slice.source.ingredients)
                {
                    if (ingredient != null && !ingredients.Contains(ingredient))
                    {
                        ingredients.Add(ingredient);
                    }
                }
            }

            foreach (FoodNetworkTankBindingV2 tank in tanks)
            {
                tank.state.RemoveEmptyBatches();
            }

            SyncLegacyStorageComps();
            batch = new FoodBatchV2(
                drawnNutrition,
                drawnFullness,
                ingredients,
                createdTick == int.MaxValue ? CurrentTick : createdTick);
            ValidateConservation(
                "draw",
                nutritionBefore - StoredNutrition,
                fullnessBefore - StoredFullness,
                batch.nutrition,
                batch.fullness);
            return !batch.Empty;
        }

        public void Purge()
        {
            foreach (FoodNetworkTankBindingV2 tank in tanks)
            {
                tank.state.Purge();
            }
            SyncLegacyStorageComps();
        }

        public string IngredientSummary(int maximumNames)
        {
            List<ThingDef> ingredients = tanks
                .SelectMany(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return tank.state.batches ?? new List<FoodBatchV2>();
                })
                .Where(delegate(FoodBatchV2 batch) { return batch != null; })
                .SelectMany(delegate(FoodBatchV2 batch)
                {
                    return batch.ingredients ?? new List<ThingDef>();
                })
                .Where(delegate(ThingDef ingredient) { return ingredient != null; })
                .Distinct()
                .OrderBy(delegate(ThingDef ingredient) { return ingredient.label; })
                .ToList();

            if (ingredients.Count == 0)
            {
                return "RR_FoodNetworkNoIngredients".Translate();
            }

            List<string> names = ingredients
                .Take(maximumNames)
                .Select(delegate(ThingDef ingredient)
                {
                    return ingredient.LabelCap.ToString();
                })
                .ToList();
            if (ingredients.Count > maximumNames)
            {
                names.Add("+" + (ingredients.Count - maximumNames).ToString());
            }
            return string.Join(", ", names.ToArray());
        }

        public void SyncLegacyStorageComps()
        {
            foreach (FoodNetworkTankBindingV2 tank in tanks)
            {
                if (tank.legacyComp == null)
                {
                    continue;
                }

                tank.legacyComp.Stored = Mathf.Clamp(
                    tank.state.StoredFullness,
                    0f,
                    tank.Capacity);
                tank.legacyComp.FullnessToNutritionRatio =
                    tank.state.AverageFullnessToNutritionRatio;
            }
        }

        private List<FoodNetworkDrawSliceV2> BuildDrawPlan(
            float maximumNutrition,
            float maximumFullness)
        {
            List<FoodNetworkDrawSliceV2> result =
                new List<FoodNetworkDrawSliceV2>();
            float nutritionRemaining = maximumNutrition;
            float fullnessRemaining = maximumFullness;

            IEnumerable<FoodNetworkDrawSliceV2> orderedBatches = tanks
                .SelectMany(delegate(FoodNetworkTankBindingV2 tank)
                {
                    return (tank.state.batches ?? new List<FoodBatchV2>())
                        .Select(delegate(FoodBatchV2 batch)
                        {
                            return new FoodNetworkDrawSliceV2
                            {
                                tank = tank,
                                source = batch
                            };
                        });
                })
                .Where(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.source != null && !slice.source.Empty;
                })
                .OrderBy(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.source.createdTick;
                })
                .ThenBy(delegate(FoodNetworkDrawSliceV2 slice)
                {
                    return slice.tank.building.thingIDNumber;
                });

            foreach (FoodNetworkDrawSliceV2 entry in orderedBatches)
            {
                if (nutritionRemaining <= FoodNetworkV2Constants.Epsilon ||
                    fullnessRemaining <= FoodNetworkV2Constants.Epsilon)
                {
                    break;
                }

                float ratio = entry.source.FullnessToNutritionRatio;
                float nutritionByFullness = fullnessRemaining / ratio;
                float nutrition = Mathf.Min(
                    entry.source.nutrition,
                    nutritionRemaining,
                    nutritionByFullness);
                if (nutrition <= FoodNetworkV2Constants.Epsilon)
                {
                    continue;
                }

                float fullness = nutrition * ratio;
                entry.nutrition = nutrition;
                entry.fullness = fullness;
                result.Add(entry);
                nutritionRemaining -= nutrition;
                fullnessRemaining -= fullness;
            }

            return result;
        }

        private static int CurrentTick
        {
            get { return Find.TickManager == null ? 0 : Find.TickManager.TicksGame; }
        }

        private void ValidateConservation(
            string operation,
            float actualNutrition,
            float actualFullness,
            float expectedNutrition,
            float expectedFullness)
        {
            if (!Prefs.DevMode)
            {
                return;
            }
            if (Mathf.Abs(actualNutrition - expectedNutrition) > 0.001f ||
                Mathf.Abs(actualFullness - expectedFullness) > 0.001f)
            {
                Log.Error(
                    "[RimRound Feed Other] Food Network v2 conservation " +
                    "check failed during " + operation + " on network " + Id +
                    ": expected " + expectedNutrition.ToString("F4") +
                    " nutrition/" + expectedFullness.ToString("F4") +
                    " fullness, observed " + actualNutrition.ToString("F4") +
                    "/" + actualFullness.ToString("F4") + ".");
            }
        }
    }

    /// <summary>
    /// Ephemeral topology. Save data remains in FoodNetworkV2GameComponent;
    /// this component only rebuilds the affected map when connectors change.
    /// </summary>
    public sealed class FoodNetworkV2MapComponent : MapComponent
    {
        private readonly List<FoodNetworkV2> networks = new List<FoodNetworkV2>();
        private readonly Dictionary<int, FoodNetworkV2> networkByThingId =
            new Dictionary<int, FoodNetworkV2>();
        private readonly Dictionary<IntVec3, FoodNetworkV2> networkByCell =
            new Dictionary<IntVec3, FoodNetworkV2>();
        private bool topologyDirty = true;

        public FoodNetworkV2MapComponent(Map map) : base(map)
        {
        }

        public static FoodNetworkV2MapComponent For(Map map)
        {
            return map == null ? null : map.GetComponent<FoodNetworkV2MapComponent>();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            FoodNetworkV2FaucetListerUtility.SyncMap(
                map,
                !FeedOtherMod.Settings.foodNetworkV2Enabled);
            topologyDirty = true;
            EnsureTopology();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (!FeedOtherMod.Settings.foodNetworkV2Enabled)
            {
                return;
            }

            int ticks = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (topologyDirty || ticks % 60 == 0)
            {
                EnsureTopology();
            }

            if (ticks > 0 && ticks % 250 == 0)
            {
                FoodNetworkV2AutoFeederUtility.CleanupMapLinks(map);
                foreach (FoodNetworkV2 network in networks)
                {
                    network.SyncLegacyStorageComps();
                }
            }
        }

        public void MarkDirty()
        {
            topologyDirty = true;
        }

        public void EnsureTopology()
        {
            if (!topologyDirty)
            {
                return;
            }

            RebuildTopology();
            topologyDirty = false;
        }

        public FoodNetworkV2 NetworkFor(Thing thing)
        {
            if (thing == null)
            {
                return null;
            }

            EnsureTopology();
            FoodNetworkV2 network;
            return networkByThingId.TryGetValue(thing.thingIDNumber, out network)
                ? network
                : null;
        }

        public FoodNetworkV2 NetworkAt(IntVec3 cell)
        {
            EnsureTopology();
            FoodNetworkV2 network;
            return networkByCell.TryGetValue(cell, out network) ? network : null;
        }

        public bool HasConnectorAt(IntVec3 cell)
        {
            return NetworkAt(cell) != null;
        }

        public IReadOnlyList<FoodNetworkV2> Networks
        {
            get
            {
                EnsureTopology();
                return networks;
            }
        }

        private void RebuildTopology()
        {
            networks.Clear();
            networkByThingId.Clear();
            networkByCell.Clear();

            if (!FeedOtherMod.Settings.foodNetworkV2Enabled || map == null)
            {
                return;
            }

            List<Thing> nodes = map.listerThings.AllThings
                .Where(delegate(Thing thing)
                {
                    if (!FoodNetworkV2Constants.IsFoodNetworkThing(thing) ||
                        FoodNetworkV2Constants.IsDistiller(thing) ||
                        !thing.Spawned)
                    {
                        return false;
                    }

                    FoodTransmitter_ThingComp comp =
                        thing.TryGetComp<FoodTransmitter_ThingComp>();
                    return comp != null && comp.TransmitsFoodNow;
                })
                .ToList();

            Dictionary<IntVec3, List<Thing>> nodesByCell =
                new Dictionary<IntVec3, List<Thing>>();
            foreach (Thing node in nodes)
            {
                foreach (IntVec3 cell in ConnectionCells(node))
                {
                    List<Thing> atCell;
                    if (!nodesByCell.TryGetValue(cell, out atCell))
                    {
                        atCell = new List<Thing>();
                        nodesByCell.Add(cell, atCell);
                    }
                    atCell.Add(node);
                }
            }

            HashSet<Thing> unvisited = new HashSet<Thing>(nodes);
            while (unvisited.Count > 0)
            {
                Thing root = unvisited.First();
                Queue<Thing> queue = new Queue<Thing>();
                List<Thing> component = new List<Thing>();
                queue.Enqueue(root);
                unvisited.Remove(root);

                while (queue.Count > 0)
                {
                    Thing current = queue.Dequeue();
                    component.Add(current);
                    foreach (Thing neighbor in Neighbors(current, nodesByCell))
                    {
                        if (unvisited.Remove(neighbor))
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                AddNetwork(component);
            }
        }

        private void AddNetwork(List<Thing> component)
        {
            if (component == null || component.Count == 0)
            {
                return;
            }

            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            List<FoodNetworkTankBindingV2> tanks =
                new List<FoodNetworkTankBindingV2>();
            foreach (Building building in component.OfType<Building>())
            {
                FoodNetStorage_ThingComp legacy =
                    building.TryGetComp<FoodNetStorage_ThingComp>();
                if (legacy == null || saved == null)
                {
                    continue;
                }

                FoodTankStateV2 state = saved.GetTankState(building, true);
                MigrateLegacyStorage(legacy, state);
                tanks.Add(new FoodNetworkTankBindingV2
                {
                    building = building,
                    legacyComp = legacy,
                    state = state
                });
            }

            int id = component.Min(delegate(Thing thing)
            {
                return thing.thingIDNumber;
            });
            FoodNetworkV2 network = new FoodNetworkV2(id, component, tanks);
            networks.Add(network);
            foreach (Thing thing in component)
            {
                networkByThingId[thing.thingIDNumber] = network;
                foreach (IntVec3 cell in ConnectionCells(thing))
                {
                    networkByCell[cell] = network;
                }
            }
            network.SyncLegacyStorageComps();
        }

        private static void MigrateLegacyStorage(
            FoodNetStorage_ThingComp legacy,
            FoodTankStateV2 state)
        {
            if (legacy == null || state == null || state.legacyMigrated)
            {
                return;
            }

            if (legacy.Stored > FoodNetworkV2Constants.Epsilon)
            {
                float ratio = Mathf.Max(
                    FoodNetworkV2Constants.Epsilon,
                    legacy.FullnessToNutritionRatio);
                state.Add(new FoodBatchV2(
                    legacy.Stored / ratio,
                    legacy.Stored,
                    new ThingDef[0],
                    Find.TickManager == null ? 0 : Find.TickManager.TicksGame));
            }
            state.legacyMigrated = true;
        }

        private static IEnumerable<Thing> Neighbors(
            Thing thing,
            Dictionary<IntVec3, List<Thing>> nodesByCell)
        {
            HashSet<Thing> result = new HashSet<Thing>();
            foreach (IntVec3 cell in ConnectionCells(thing))
            {
                AddThingsAt(cell, thing, nodesByCell, result);
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    AddThingsAt(
                        cell + GenAdj.CardinalDirections[i],
                        thing,
                        nodesByCell,
                        result);
                }
            }
            return result;
        }

        private static void AddThingsAt(
            IntVec3 cell,
            Thing self,
            Dictionary<IntVec3, List<Thing>> nodesByCell,
            HashSet<Thing> result)
        {
            List<Thing> things;
            if (!nodesByCell.TryGetValue(cell, out things))
            {
                return;
            }

            foreach (Thing thing in things)
            {
                if (thing != self)
                {
                    result.Add(thing);
                }
            }
        }

        internal static IEnumerable<IntVec3> ConnectionCells(Thing thing)
        {
            if (thing == null)
            {
                yield break;
            }

            // Existing machines accepted pipes around their footprint. Keeping
            // those explicit footprint ports preserves old colonies. The
            // distiller is deliberately excluded and uses two directional
            // external ports instead.
            foreach (IntVec3 cell in thing.OccupiedRect().Cells)
            {
                yield return cell;
            }
        }

        public static void ImportLegacyStorageFromAllMaps()
        {
            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            if (saved == null || Find.Maps == null)
            {
                return;
            }

            foreach (Map currentMap in Find.Maps)
            {
                foreach (Building tank in currentMap.listerThings.AllThings
                    .OfType<Building>())
                {
                    FoodNetStorage_ThingComp legacy =
                        tank.TryGetComp<FoodNetStorage_ThingComp>();
                    if (legacy == null)
                    {
                        continue;
                    }

                    FoodTankStateV2 state = saved.GetTankState(tank, true);
                    state.Purge();
                    if (legacy.Stored > FoodNetworkV2Constants.Epsilon)
                    {
                        float ratio = Mathf.Max(
                            FoodNetworkV2Constants.Epsilon,
                            legacy.FullnessToNutritionRatio);
                        state.Add(new FoodBatchV2(
                            legacy.Stored / ratio,
                            legacy.Stored,
                            new ThingDef[0],
                            Find.TickManager == null
                                ? 0
                                : Find.TickManager.TicksGame));
                    }
                    state.legacyMigrated = true;
                }
            }
        }

        public static void ExportSavedStorageToLegacyAllMaps()
        {
            FoodNetworkV2GameComponent saved = FoodNetworkV2GameComponent.Instance;
            if (saved == null || Find.Maps == null)
            {
                return;
            }

            foreach (Map currentMap in Find.Maps)
            {
                foreach (Building tank in currentMap.listerThings.AllThings
                    .OfType<Building>())
                {
                    FoodNetStorage_ThingComp legacy =
                        tank.TryGetComp<FoodNetStorage_ThingComp>();
                    FoodTankStateV2 state = saved.GetTankState(tank, false);
                    if (legacy == null || state == null)
                    {
                        continue;
                    }

                    legacy.Stored = Mathf.Clamp(
                        state.StoredFullness,
                        0f,
                        legacy.Capacity);
                    legacy.FullnessToNutritionRatio =
                        state.AverageFullnessToNutritionRatio;
                }
            }
        }

        public static void MarkAllMapsDirty()
        {
            if (Find.Maps == null)
            {
                return;
            }

            foreach (Map currentMap in Find.Maps)
            {
                FoodNetworkV2MapComponent component = For(currentMap);
                if (component != null)
                {
                    component.MarkDirty();
                }
            }
        }
    }
}
