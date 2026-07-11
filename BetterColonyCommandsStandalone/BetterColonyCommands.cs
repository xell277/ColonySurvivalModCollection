#nullable disable
using BlockEntities.Implementations;
using ModLoaderInterfaces;
using Monsters;
using NPC;
using Pipliz;
using Pipliz.Collections;

[ModLoader.ModManager]
public class BetterColony
{
    public class ThreatCallbacks : IOnRecalculateThreatLevel, IModCallbackConsumer
    {
        [ModLoader.ModCallback("bettercolony.rebalance_threat")]
        [ModLoader.ModCallbackDependsOn("defaultThreat")]
        [ModLoader.ModCallbackDependsOn("check_npc_count")]
        [ModLoader.ModCallbackDependsOn("add_failsafe")]
        public void OnRecalculateThreatLevel(ColonyGroup group)
        {
            Colony mainColony = group.MainColony;
            float totalWeight = 0f;
            int i;

            if (mainColony == null)
            {
                return;
            }

            for (i = 0; i < group.Colonies.Count; i++)
            {
                totalWeight += group.Colonies.GetAt(i).ColonyThreat.InternalGetThreatWeight();
            }

            for (i = 0; i < group.Colonies.Count; i++)
            {
                ref Colony colony = ref group.Colonies.GetAt(i);
                if (colony.ColonyID != group.MainColonyID)
                {
                    colony.ColonyThreat.InternalSetThreatWeight(0f);
                }
            }

            if (totalWeight <= 0f)
            {
                totalWeight = 1f;
            }

            mainColony.ColonyThreat.InternalSetThreatWeight(totalWeight);
        }
    }

    public class WorldCallbacks : IAfterWorldLoad, IOnActiveColonyChanges, IModCallbackConsumer
    {
        [ModLoader.ModCallback("bettercolony.register_spawner")]
        [ModLoader.ModCallbackDependsOn("pipliz.server.monsterspawner.register")]
        public void AfterWorldLoad()
        {
            MonsterTracker.MonsterSpawner = new RedirectingMonsterSpawner();
            RecalculateAndSyncAll();
        }

        [ModLoader.ModCallback("bettercolony.sync_on_active_colony_change")]
        public void OnActiveColonyChanges(Players.Player player, Colony oldColony, Colony newColony)
        {
            if (oldColony != null)
            {
                RecalculateAndSyncGroup(oldColony.ColonyGroup);
            }

            if (newColony != null && (oldColony == null || newColony.ColonyGroup != oldColony.ColonyGroup))
            {
                RecalculateAndSyncGroup(newColony.ColonyGroup);
            }
            else if (newColony != null)
            {
                newColony.ColonyGroup.SendThreatLevelsToActiveOwners();
            }
        }

        private static void RecalculateAndSyncAll()
        {
            Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator enumerator = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
            while (enumerator.MoveNext())
            {
                ColonyGroup group = enumerator.Current;
                if (group != null)
                {
                    RecalculateAndSyncGroup(group);
                }
            }
        }

        private static void RecalculateAndSyncGroup(ColonyGroup group)
        {
            int i;

            if (group == null)
            {
                return;
            }

            group.RecalculateThreat();

            for (i = 0; i < group.Colonies.Count; i++)
            {
                ref Colony colony = ref group.Colonies.GetAt(i);
                if (colony.ColonyID != group.MainColonyID)
                {
                    colony.MonsterSpawnData = default(ColonyMonsterData);
                    colony.ClearSiegeIfNeeded();
                }
            }

            group.SendThreatLevelsToActiveOwners();
        }
    }

    public class RedirectingMonsterSpawner : MonsterSpawner
    {
        public override void Update()
        {
            int i;

            if (World.FramesSinceInitialization < 2 || ServerManager.ChunkQueue == null || !ServerManager.ChunkQueue.CompletedInitialLoad || ServerTime.IsPaused)
            {
                return;
            }

            for (i = 0; i < ActiveJobs.Count; i++)
            {
                ref SpawnJob activeJob = ref ActiveJobs.GetAt(i);
                if (!activeJob.UpdateEarly())
                {
                    Flowfields.Remove(activeJob.UsedBanner.Position);
                    activeJob.Dispose();
                    ActiveJobs.RemoveSwapback(i);
                    i--;
                }
            }

            TimeCycle.GameTimeSpan totalTime = TimeCycle.TotalTime;
            Hashmap<ColonyID, Colony>.ValueEnumerator valueEnumerator = ServerManager.ColonyTracker.ColoniesByID.GetValueEnumerator();
            while (valueEnumerator.MoveNext())
            {
                Colony current = valueEnumerator.Current;
                ref ColonyMonsterData monsterSpawnData = ref current.MonsterSpawnData;

                if (current.FollowerCount == 0)
                {
                    monsterSpawnData.NextHPReduction = 0f;
                    current.ClearSiegeIfNeeded();
                    continue;
                }

                BannerTracker.Banner targetBanner = GetTargetBanner(current);
                if (targetBanner == null)
                {
                    monsterSpawnData = default(ColonyMonsterData);
                    current.ClearSiegeIfNeeded();
                    continue;
                }

                float threatLevel = current.ColonyThreat.GetThreatLevel(current.ColonyGroup);
                if (threatLevel <= 0f)
                {
                    monsterSpawnData = default(ColonyMonsterData);
                    current.ClearSiegeIfNeeded();
                    continue;
                }

                if (current.NextSiegeModeCheck.IsPassed)
                {
                    if (current.ColonyGroup.DifficultySetting.GetSetting().MonsterScaling <= 0f)
                    {
                        current.SetSiegeMode(false);
                    }
                    else
                    {
                        QueueCheckSiegeMode(targetBanner);
                    }
                }

                if (monsterSpawnData.CheckIsExpired(totalTime))
                {
                    monsterSpawnData.Refresh(current, totalTime);
                }

                if (monsterSpawnData.Spawns.Count == 0)
                {
                    continue;
                }

                for (i = monsterSpawnData.Spawns.Count - 1; i >= 0; i--)
                {
                    ref ColonyMonsterData.SpawnEntry spawn = ref monsterSpawnData.Spawns.GetAt(i);
                    if (totalTime < spawn.StartTime)
                    {
                        continue;
                    }

                    if (totalTime >= spawn.EndTime)
                    {
                        monsterSpawnData.Spawns.RemoveAt(i);
                        continue;
                    }

                    int missingSpawns = Pipliz.Math.CeilToInt((float)TimeCycle.GameTimeSpan.InverseLerpClamped(spawn.StartTime, spawn.EndTime, totalTime) * (float)spawn.Spawns) - spawn.Spawned - spawn.SpawnsInFlight;
                    while (missingSpawns > 0)
                    {
                        QueueSpawnZombie(targetBanner, spawn.MonsterType);
                        spawn.SpawnsInFlight++;
                        missingSpawns--;
                    }
                }
            }
        }

        private static BannerTracker.Banner GetTargetBanner(Colony colony)
        {
            Colony targetColony = ResolveTargetColony(colony);
            BannerTracker.Banner targetBanner = null;

            if (targetColony != null)
            {
                targetBanner = targetColony.GetRandomBanner();
                if (targetBanner != null)
                {
                    return targetBanner;
                }
            }

            return colony.GetRandomBanner();
        }

        private static Colony ResolveTargetColony(Colony colony)
        {
            ColonyGroup group = colony.ColonyGroup;

            if (group == null || group.MainColony == null)
            {
                return colony;
            }

            return group.MainColony;
        }
    }
}
