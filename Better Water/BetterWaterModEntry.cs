using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BlockTypes;
using ModLoaderInterfaces;
using Newtonsoft.Json.Linq;
using Pipliz;
using Shared;

namespace BetterWater
{
    [ModLoader.ModManager]
    public static class BetterWaterModEntry
    {
        public const string Namespace = "BetterWater";
        public const string EmptyBucketTypeName = "BetterWater.BucketEmpty";
        public const string WaterBucketTypeName = "BetterWater.BucketWater";

        private enum WaterNodeKind
        {
            Source,
            Falling,
            Flow
        }

        private sealed class WaterNode
        {
            public Vector3Int Position;
            public Vector3Int Source;
            public Vector3Int Parent;
            public WaterNodeKind Kind;
            public int FallDistance;
            public int HorizontalDistance;
            public bool AllowHorizontalSpread = true;
            public bool AllowLandingSpread = true;
            public bool IsChannel;
            public Vector3Int FlowDirection;
            public readonly HashSet<Vector3Int> Children = new HashSet<Vector3Int>();
        }

        private struct WaterfallSoundKey : IEquatable<WaterfallSoundKey>
        {
            public Vector3Int Source;
            public Vector3Int Cluster;

            public bool Equals(WaterfallSoundKey other)
            {
                return Source == other.Source && Cluster == other.Cluster;
            }

            public override bool Equals(object obj)
            {
                return obj is WaterfallSoundKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + Source.GetHashCode();
                    hash = hash * 31 + Cluster.GetHashCode();
                    return hash;
                }
            }
        }

        private sealed class WaterfallLoopState
        {
            public AudioManager.AudioClipPlayingID PlayingId;
            public AudioManager.AudioClipIndex ClipIndex;
            public Vector3Int Position;
            public long NextRefreshMilliseconds;
        }

        private static readonly Vector3Int[] HorizontalDirections =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        private static readonly Dictionary<Vector3Int, WaterNode> Nodes = new Dictionary<Vector3Int, WaterNode>();
        private static readonly Queue<Vector3Int> Queue = new Queue<Vector3Int>();
        private static readonly HashSet<Vector3Int> Queued = new HashSet<Vector3Int>();
        private static readonly HashSet<Vector3Int> InternalChanges = new HashSet<Vector3Int>();
        private static readonly HashSet<Vector3Int> DesiredChildrenScratch = new HashSet<Vector3Int>();
        private static readonly Dictionary<WaterfallSoundKey, long> WaterfallSoundCooldowns = new Dictionary<WaterfallSoundKey, long>();
        private static readonly Dictionary<WaterfallSoundKey, WaterfallLoopState> WaterfallSoundLoops = new Dictionary<WaterfallSoundKey, WaterfallLoopState>();
        private static readonly List<WaterfallSoundKey> WaterfallSoundCleanupScratch = new List<WaterfallSoundKey>();
        private static readonly List<WaterfallSoundKey> WaterfallLoopScratch = new List<WaterfallSoundKey>();

        private static readonly BlockChangeRequestOrigin FlowOrigin =
            new BlockChangeRequestOrigin(BlockChangeRequestOrigin.EType.Misc);

        private static FieldInfo _vanillaUpdatesPerTickField;
        private static FieldInfo _vanillaLocationsToCheckField;
        private static long _nextTickMilliseconds;
        private static long _nextSoundCooldownCleanupMilliseconds;
        private static long _nextSourceMarkerMilliseconds;
        private static bool _itemTypesReady;
        private static bool _waterfallAudioMissingLogged;
        private static ushort _emptyBucketType;
        private static ushort _waterBucketType;
        private static int _vanillaUpdatesPerTickFallback = -1;

        public static BetterWaterConfig Config { get; private set; }
        public static string ModFolder { get; private set; } = string.Empty;
        public static string ConfigPath { get; private set; } = string.Empty;

        private static bool CustomFlowActive
        {
            get
            {
                return _itemTypesReady &&
                       Config != null &&
                       Config.Enabled &&
                       !Config.VanillaSpreadEnabled;
            }
        }

        [ModLoader.ModCallback(ModLoader.EModCallbackType.OnAssemblyLoaded, Namespace + ".OnAssemblyLoaded")]
        public static void OnAssemblyLoaded(string path)
        {
            ModFolder = Path.GetDirectoryName(path) ?? string.Empty;
            ConfigPath = Path.Combine(ModFolder, "betterwaterconfig.json");
            BetterWaterLogger.Initialize(Path.Combine(ModFolder, "betterwater.log"));
            TryLoadAudioPatches();
            ReloadConfig();
        }

        public static bool ReloadConfig()
        {
            try
            {
                Config = BetterWaterConfig.LoadOrCreate(ConfigPath);
                _waterfallAudioMissingLogged = false;
                StopAllWaterfallLoops();
                ApplyVanillaSpreadMode();
                QueueAllSources();
                BetterWaterLogger.Write("Config reloaded. " + GetStatusText());
                return true;
            }
            catch (Exception exception)
            {
                BetterWaterLogger.Write("Config reload failed: " + exception);
                return false;
            }
        }

        public static void OnAfterItemTypesDefined()
        {
            _itemTypesReady = true;
            ResolveBucketItemTypes();
            ResolveVanillaWaterFields();
            ApplyVanillaSpreadMode();
        }

        public static void OnUpdate()
        {
            if (!CustomFlowActive || Players.GetPauseState())
            {
                return;
            }

            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            if (now < _nextTickMilliseconds)
            {
                return;
            }

            _nextTickMilliseconds = now + Config.TickIntervalMilliseconds;
            ProcessQueue(Config.MaxUpdatesPerTick);
            RefreshWaterfallSoundLoops(now);
            PruneWaterfallSoundCooldowns(now);
            TryShowSourceMarkers(now);
        }

        public static void OnSaveWorldMisc(JObject data)
        {
            if (data == null)
            {
                return;
            }

            JArray nodes = new JArray();
            foreach (WaterNode node in Nodes.Values)
            {
                nodes.Add(new JObject
                {
                    { "x", node.Position.x },
                    { "y", node.Position.y },
                    { "z", node.Position.z },
                    { "kind", node.Kind.ToString() },
                    { "sourceX", node.Source.x },
                    { "sourceY", node.Source.y },
                    { "sourceZ", node.Source.z },
                    { "parentX", node.Parent.x },
                    { "parentY", node.Parent.y },
                    { "parentZ", node.Parent.z },
                    { "fallDistance", node.FallDistance },
                    { "horizontalDistance", node.HorizontalDistance },
                    { "allowHorizontalSpread", node.AllowHorizontalSpread },
                    { "allowLandingSpread", node.AllowLandingSpread },
                    { "isChannel", node.IsChannel },
                    { "directionX", node.FlowDirection.x },
                    { "directionY", node.FlowDirection.y },
                    { "directionZ", node.FlowDirection.z }
                });
            }

            data[Namespace] = new JObject
            {
                { "version", 4 },
                { "nodes", nodes }
            };
        }

        public static void OnLoadWorldMisc(JObject data)
        {
            Nodes.Clear();
            Queue.Clear();
            Queued.Clear();
            WaterfallSoundCooldowns.Clear();
            StopAllWaterfallLoops();

            if (data == null ||
                !(data[Namespace] is JObject root) ||
                !(root["nodes"] is JArray nodes))
            {
                return;
            }

            Dictionary<Vector3Int, WaterNode> loaded = new Dictionary<Vector3Int, WaterNode>();
            foreach (JToken token in nodes)
            {
                if (!(token is JObject obj))
                {
                    continue;
                }

                WaterNode node = ReadNode(obj);
                if (node != null)
                {
                    loaded[node.Position] = node;
                }
            }

            foreach (WaterNode node in loaded.Values)
            {
                if (node.Kind == WaterNodeKind.Source || loaded.ContainsKey(node.Parent))
                {
                    Nodes[node.Position] = node;
                }
            }

            foreach (WaterNode node in Nodes.Values)
            {
                node.Children.Clear();
            }

            foreach (WaterNode node in Nodes.Values)
            {
                if (node.Kind != WaterNodeKind.Source && Nodes.TryGetValue(node.Parent, out WaterNode parent))
                {
                    parent.Children.Add(node.Position);
                }
            }

            QueueAllSources();
            BetterWaterLogger.Write("Loaded " + Nodes.Count + " managed water nodes.");
        }

        public static void SetBetterWaterEnabled(bool enabled)
        {
            EnsureConfig();
            Config.Enabled = enabled;
            if (enabled)
            {
                Config.VanillaSpreadEnabled = false;
            }
            else
            {
                Config.VanillaSpreadEnabled = true;
            }

            Config.Normalize();
            BetterWaterConfig.Save(ConfigPath, Config);
            ApplyVanillaSpreadMode();
            if (!enabled)
            {
                StopAllWaterfallLoops();
            }

            QueueAllSources();
        }

        public static void SetVanillaSpreadEnabled(bool enabled)
        {
            EnsureConfig();
            Config.VanillaSpreadEnabled = enabled;
            Config.Enabled = !enabled;
            Config.Normalize();
            BetterWaterConfig.Save(ConfigPath, Config);
            ApplyVanillaSpreadMode();
            if (enabled)
            {
                StopAllWaterfallLoops();
                SeedVanillaWaterQueue();
            }
            else
            {
                QueueAllSources();
            }
        }

        public static void SetWaterfallSoundsEnabled(bool enabled)
        {
            EnsureConfig();
            Config.EnableWaterfallSounds = enabled;
            Config.Normalize();
            BetterWaterConfig.Save(ConfigPath, Config);
            _waterfallAudioMissingLogged = false;
            if (!enabled)
            {
                WaterfallSoundCooldowns.Clear();
                StopAllWaterfallLoops();
            }
        }

        public static void SetSourceMarkersEnabled(bool enabled)
        {
            EnsureConfig();
            Config.ShowSourceMarkers = enabled;
            Config.Normalize();
            BetterWaterConfig.Save(ConfigPath, Config);
            if (enabled)
            {
                _nextSourceMarkerMilliseconds = 0;
            }
        }

        public static void CleanupManagedFlows()
        {
            StopAllWaterfallLoops();

            List<Vector3Int> managedFlows = new List<Vector3Int>();
            foreach (WaterNode node in Nodes.Values)
            {
                if (node.Kind != WaterNodeKind.Source)
                {
                    managedFlows.Add(node.Position);
                }
            }

            for (int i = 0; i < managedFlows.Count; i++)
            {
                RemoveManagedBranch(managedFlows[i], true);
            }

            foreach (WaterNode node in Nodes.Values)
            {
                node.Children.Clear();
            }

            QueueAllSources();
        }

        public static string GetStatusText()
        {
            EnsureConfig();
            return "BetterWater " +
                   (Config.Enabled ? "enabled" : "disabled") +
                   " | vanillaSpread=" + (Config.VanillaSpreadEnabled ? "on" : "off") +
                   " | maxFall=" + Config.MaxFallDistance +
                   " | sourceSpread=" + Config.SourceHorizontalSpread +
                   " | flowSpread=" + Config.FlowHorizontalSpread +
                   " | sound=" + (Config.EnableWaterfallSounds ? "on" : "off") +
                   " | markers=" + (Config.ShowSourceMarkers ? "on" : "off") +
                   " | loops=" + WaterfallSoundLoops.Count +
                   " | nodes=" + Nodes.Count +
                   " | queue=" + Queue.Count;
        }

        public static bool CanManage(Players.Player player)
        {
            if (player == null)
            {
                return true;
            }

            if (Players.IsSingleplayerID(player.ID) || Players.IsServerID(player.ID))
            {
                return true;
            }

            try
            {
                return PermissionsManager.CheckAndWarnPermission(player, "server.save");
            }
            catch
            {
                return false;
            }
        }

        public static void OnWaterAdjacent(Vector3Int waterPosition)
        {
            if (!CustomFlowActive)
            {
                return;
            }

            if (Config.TreatUntrackedWaterAsSource && !Nodes.ContainsKey(waterPosition))
            {
                EnsureSource(waterPosition);
            }

            QueueWater(waterPosition);
        }

        public static void OnWaterChanged(Vector3Int position, ItemTypes.ItemType oldType, ItemTypes.ItemType newType)
        {
            if (!_itemTypesReady || oldType == null || newType == null)
            {
                return;
            }

            bool oldWater = oldType == BuiltinBlocks.Types.water;
            bool newWater = newType == BuiltinBlocks.Types.water;
            if (!oldWater && !newWater)
            {
                return;
            }

            if (!CustomFlowActive)
            {
                return;
            }

            bool internalChange = InternalChanges.Contains(position);
            if (newWater && !oldWater)
            {
                if (!internalChange && Config.TreatUntrackedWaterAsSource)
                {
                    EnsureSource(position);
                }

                QueueWater(position);
                QueueWaterNeighbours(position);
                return;
            }

            if (oldWater && !newWater)
            {
                if (Nodes.TryGetValue(position, out WaterNode node))
                {
                    RemoveManagedBranch(position, false);
                    QueueWater(node.Parent);
                }

                QueueWaterNeighbours(position);
            }
        }

        public static void OnPlayerClicked(Players.Player player, PlayerClickedData click)
        {
            if (player == null ||
                click == null ||
                !_itemTypesReady ||
                click.HitType != PlayerClickedData.EHitType.Block ||
                click.ClickType != PlayerClickedData.EClickType.Right ||
                (click.ConsumedType != PlayerClickedData.EConsumedType.Not &&
                 click.ConsumedType != PlayerClickedData.EConsumedType.InvalidBuild) ||
                click.InventoryIndexSelected < 0 ||
                click.InventoryIndexSelected >= 8)
            {
                return;
            }

            if (click.TypeSelected == _emptyBucketType)
            {
                click.ConsumedType = PlayerClickedData.EConsumedType.UsedByMod;
                TryFillBucket(player, click);
                return;
            }

            if (click.TypeSelected == _waterBucketType)
            {
                click.ConsumedType = PlayerClickedData.EConsumedType.UsedByMod;
                TryEmptyBucket(player, click);
            }
        }

        private static void ProcessQueue(int budget)
        {
            int processed = 0;
            while (processed < budget && Queue.Count > 0)
            {
                Vector3Int position = Queue.Dequeue();
                Queued.Remove(position);
                processed++;

                try
                {
                    ProcessPosition(position);
                }
                catch (Exception exception)
                {
                    BetterWaterLogger.Write("Process failed at " + FormatPosition(position) + ": " + exception);
                }
            }
        }

        private static void ProcessPosition(Vector3Int position)
        {
            if (!World.TryGetTypeAt(position, out ushort type))
            {
                return;
            }

            if (type != BuiltinBlocks.Indices.water)
            {
                if (Nodes.TryGetValue(position, out WaterNode oldNode))
                {
                    RemoveManagedBranch(position, false);
                    QueueWater(oldNode.Parent);
                }

                return;
            }

            if (!Nodes.TryGetValue(position, out WaterNode node))
            {
                if (!Config.TreatUntrackedWaterAsSource)
                {
                    return;
                }

                node = EnsureSource(position);
            }

            UpdateNode(node);
        }

        private static void UpdateNode(WaterNode node)
        {
            DesiredChildrenScratch.Clear();

            switch (node.Kind)
            {
                case WaterNodeKind.Source:
                    if (!TryPlanDown(node, 1))
                    {
                        PlanHorizontal(node, 1, Config.SourceHorizontalSpread);
                    }
                    break;
                case WaterNodeKind.Falling:
                    if (!TryPlanDown(node, node.FallDistance + 1) &&
                        node.AllowLandingSpread &&
                        HasSupportBelow(node.Position))
                    {
                        if (node.IsChannel)
                        {
                            PlanChannelForward(node);
                        }
                        else
                        {
                            PlanHorizontal(node, 1, Config.FlowHorizontalSpread);
                        }
                    }
                    break;
                case WaterNodeKind.Flow:
                    int spreadLimit = GetHorizontalSpreadLimit(node);
                    if (!TryPlanDown(node, 1) &&
                        node.AllowHorizontalSpread &&
                        node.HorizontalDistance < spreadLimit)
                    {
                        PlanHorizontal(node, node.HorizontalDistance + 1, spreadLimit);
                    }
                    break;
            }

            ReconcileChildren(node, DesiredChildrenScratch);
        }

        private static bool TryPlanDown(WaterNode node, int nextFallDistance)
        {
            Vector3Int target = node.Position.Add(0, -1, 0);
            if (nextFallDistance > Config.MaxFallDistance)
            {
                TryPlayWaterfallImpactSound(node, target);
                return false;
            }

            bool planned = TryEnsureChild(
                node,
                target,
                WaterNodeKind.Falling,
                nextFallDistance,
                0,
                GetFallingChildHorizontalSpreadMode(node),
                GetFallingChildLandingSpreadMode(node),
                GetFallingChildChannelMode(node),
                GetFallingChildDirection(node));
            if (!planned)
            {
                TryPlayWaterfallImpactSound(node, target);
            }

            return planned;
        }

        private static void TryFillBucket(Players.Player player, PlayerClickedData click)
        {
            if (!IsSelectedInventoryItem(player, click.InventoryIndexSelected, _emptyBucketType))
            {
                return;
            }

            if (!TryGetBucketWaterTarget(click, out Vector3Int waterPosition))
            {
                return;
            }

            EServerChangeBlockResult result = ServerManager.TryChangeBlock(
                waterPosition,
                BuiltinBlocks.Types.water,
                BuiltinBlocks.Types.air,
                player,
                ESetBlockFlags.DefaultAudio);

            if (result != EServerChangeBlockResult.Success)
            {
                return;
            }

            ReplaceSelectedBucket(player, click.InventoryIndexSelected, _emptyBucketType, _waterBucketType);
            TryPlayBucketAudio(waterPosition);
            BetterWaterLogger.Debug("Filled bucket from " + FormatPosition(waterPosition));
        }

        private static bool TryGetBucketWaterTarget(PlayerClickedData click, out Vector3Int waterPosition)
        {
            PlayerClickedData.VoxelHit hit = click.GetVoxelHit();
            waterPosition = hit.BlockHit;
            if (World.TryGetTypeAt(waterPosition, out ushort targetType) && targetType == BuiltinBlocks.Indices.water)
            {
                return true;
            }

            float distance = 0f;
            try
            {
                distance = click.GetDistanceToHit();
            }
            catch
            {
                return false;
            }

            UnityEngine.Vector3 direction = click.PlayerAimDirection.normalized;
            float maxDistance = System.Math.Min(distance + 0.25f, 12f);
            for (float sampleDistance = 0.5f; sampleDistance <= maxDistance; sampleDistance += 0.25f)
            {
                Vector3Int sample = new Vector3Int(click.PlayerEyePosition + direction * sampleDistance);
                if (sample == waterPosition)
                {
                    continue;
                }

                if (World.TryGetTypeAt(sample, out targetType) && targetType == BuiltinBlocks.Indices.water)
                {
                    waterPosition = sample;
                    return true;
                }
            }

            return false;
        }

        private static void TryEmptyBucket(Players.Player player, PlayerClickedData click)
        {
            if (!IsSelectedInventoryItem(player, click.InventoryIndexSelected, _waterBucketType))
            {
                return;
            }

            PlayerClickedData.VoxelHit hit = click.GetVoxelHit();
            if (TryEmptyBucketIntoSource(player, click.InventoryIndexSelected, hit.BlockHit))
            {
                return;
            }

            Vector3Int buildPosition = hit.PositionBuild;
            if (!World.TryGetTypeAt(buildPosition, out ushort targetType) || targetType != BuiltinBlocks.Indices.air)
            {
                return;
            }

            EServerChangeBlockResult result = ServerManager.TryChangeBlockOverlapChecked(
                buildPosition,
                BuiltinBlocks.Types.air,
                BuiltinBlocks.Types.water,
                player,
                ESetBlockFlags.DefaultAudio);

            if (result != EServerChangeBlockResult.Success)
            {
                return;
            }

            EnsureSource(buildPosition);
            QueueWater(buildPosition);
            QueueWaterNeighbours(buildPosition);
            ReplaceSelectedBucket(player, click.InventoryIndexSelected, _waterBucketType, _emptyBucketType);
            TryPlayBucketAudio(buildPosition);
            BetterWaterLogger.Debug("Emptied bucket at " + FormatPosition(buildPosition));
        }

        private static bool TryEmptyBucketIntoSource(Players.Player player, int inventoryIndex, Vector3Int waterPosition)
        {
            if (!World.TryGetTypeAt(waterPosition, out ushort targetType) || targetType != BuiltinBlocks.Indices.water)
            {
                return false;
            }

            if (!Nodes.TryGetValue(waterPosition, out WaterNode node))
            {
                if (!Config.TreatUntrackedWaterAsSource)
                {
                    return false;
                }

                node = EnsureSource(waterPosition);
            }

            if (node.Kind != WaterNodeKind.Source)
            {
                return false;
            }

            ReplaceSelectedBucket(player, inventoryIndex, _waterBucketType, _emptyBucketType);
            QueueWater(waterPosition);
            TryPlayBucketAudio(waterPosition);
            BetterWaterLogger.Debug("Emptied bucket into source at " + FormatPosition(waterPosition));
            return true;
        }

        private static bool IsSelectedInventoryItem(Players.Player player, int inventoryIndex, ushort expectedType)
        {
            if (expectedType == 0 || player == null || inventoryIndex < 0 || inventoryIndex >= 8)
            {
                return false;
            }

            InventoryItem selected = player.Inventory.GetAt(inventoryIndex);
            return selected.Type == expectedType && selected.Amount > 0;
        }

        private static void ReplaceSelectedBucket(Players.Player player, int inventoryIndex, ushort fromType, ushort toType)
        {
            InventoryItem selected = player.Inventory.GetAt(inventoryIndex);
            if (selected.Type != fromType || selected.Amount <= 0)
            {
                return;
            }

            if (selected.Amount == 1)
            {
                player.Inventory.SetAt(inventoryIndex, new InventoryItem(toType));
            }
            else
            {
                player.Inventory.SetAt(inventoryIndex, new InventoryItem(fromType, selected.Amount - 1));
                if (!player.Inventory.TryAdd(toType, 1, -1, false))
                {
                    player.ActiveColonyGroup?.Stockpile.Add(toType);
                }
            }

            player.SendStockpileInventory(player.ActiveColonyGroup?.Stockpile);
        }

        private static void TryPlayBucketAudio(Vector3Int position)
        {
            try
            {
                AudioManager.SendAudio(position.Vector, "waterSplashFeet");
            }
            catch
            {
            }
        }

        private static void PlanHorizontal(WaterNode node, int horizontalDistance, int spreadLimit)
        {
            if (horizontalDistance > spreadLimit)
            {
                return;
            }

            for (int i = 0; i < HorizontalDirections.Length; i++)
            {
                Vector3Int direction = HorizontalDirections[i];
                Vector3Int target = node.Position + direction;
                TryEnsureChild(node, target, WaterNodeKind.Flow, 0, horizontalDistance, true, true, false, direction);
            }
        }

        private static void PlanChannelForward(WaterNode node)
        {
            Vector3Int direction = NormalizeHorizontalDirection(node.FlowDirection);
            if (direction == new Vector3Int(0, 0, 0))
            {
                return;
            }

            Vector3Int target = node.Position + direction;
            TryEnsureChild(node, target, WaterNodeKind.Flow, 0, 1, false, true, true, direction);
        }

        private static bool GetFallingChildHorizontalSpreadMode(WaterNode parent)
        {
            if (parent.Kind == WaterNodeKind.Source)
            {
                return true;
            }

            if (parent.Kind == WaterNodeKind.Falling)
            {
                return parent.AllowHorizontalSpread;
            }

            return false;
        }

        private static bool GetFallingChildLandingSpreadMode(WaterNode parent)
        {
            if (parent.Kind == WaterNodeKind.Source)
            {
                return true;
            }

            if (parent.Kind == WaterNodeKind.Falling)
            {
                return parent.AllowLandingSpread;
            }

            return true;
        }

        private static bool GetFallingChildChannelMode(WaterNode parent)
        {
            if (parent.Kind == WaterNodeKind.Flow)
            {
                return true;
            }

            if (parent.Kind == WaterNodeKind.Falling)
            {
                return parent.IsChannel;
            }

            return false;
        }

        private static Vector3Int GetFallingChildDirection(WaterNode parent)
        {
            if (parent.Kind == WaterNodeKind.Flow)
            {
                Vector3Int direction = NormalizeHorizontalDirection(parent.FlowDirection);
                if (direction != new Vector3Int(0, 0, 0))
                {
                    return direction;
                }

                return NormalizeHorizontalDirection(parent.Position - parent.Parent);
            }

            if (parent.Kind == WaterNodeKind.Falling)
            {
                return NormalizeHorizontalDirection(parent.FlowDirection);
            }

            return new Vector3Int(0, 0, 0);
        }

        private static int GetHorizontalSpreadLimit(WaterNode node)
        {
            if (Config == null)
            {
                return 3;
            }

            if (node.Kind == WaterNodeKind.Source)
            {
                return Config.SourceHorizontalSpread;
            }

            WaterNode cursor = node;
            for (int i = 0; i < 128 && cursor.Kind == WaterNodeKind.Flow; i++)
            {
                if (!Nodes.TryGetValue(cursor.Parent, out WaterNode parent))
                {
                    break;
                }

                if (parent.Kind == WaterNodeKind.Source)
                {
                    return Config.SourceHorizontalSpread;
                }

                if (parent.Kind == WaterNodeKind.Falling)
                {
                    return Config.FlowHorizontalSpread;
                }

                cursor = parent;
            }

            return Config.FlowHorizontalSpread;
        }

        private static bool TryEnsureChild(
            WaterNode parent,
            Vector3Int target,
            WaterNodeKind kind,
            int fallDistance,
            int horizontalDistance,
            bool allowHorizontalSpread,
            bool allowLandingSpread,
            bool isChannel,
            Vector3Int flowDirection)
        {
            if (Nodes.TryGetValue(target, out WaterNode existing))
            {
                if (existing.Parent != parent.Position)
                {
                    return false;
                }

                existing.Kind = kind;
                existing.Source = parent.Source;
                existing.FallDistance = fallDistance;
                existing.HorizontalDistance = horizontalDistance;
                existing.AllowHorizontalSpread = allowHorizontalSpread;
                existing.AllowLandingSpread = allowLandingSpread;
                existing.IsChannel = isChannel;
                existing.FlowDirection = NormalizeHorizontalDirection(flowDirection);
                DesiredChildrenScratch.Add(target);
                QueueWater(target);
                return true;
            }

            if (!World.TryGetTypeAt(target, out ushort targetType) || targetType != BuiltinBlocks.Indices.air)
            {
                return false;
            }

            WaterNode child = new WaterNode
            {
                Position = target,
                Source = parent.Source,
                Parent = parent.Position,
                Kind = kind,
                FallDistance = fallDistance,
                HorizontalDistance = horizontalDistance,
                AllowHorizontalSpread = allowHorizontalSpread,
                AllowLandingSpread = allowLandingSpread,
                IsChannel = isChannel,
                FlowDirection = NormalizeHorizontalDirection(flowDirection)
            };

            Nodes[target] = child;
            parent.Children.Add(target);
            DesiredChildrenScratch.Add(target);

            EServerChangeBlockResult result = ChangeBlockInternal(target, BuiltinBlocks.Types.air, BuiltinBlocks.Types.water);
            if (result != EServerChangeBlockResult.Success)
            {
                parent.Children.Remove(target);
                Nodes.Remove(target);
                DesiredChildrenScratch.Remove(target);
                return false;
            }

            BetterWaterLogger.Debug("Created " + kind + " water at " + FormatPosition(target));
            QueueWater(target);
            return true;
        }

        private static void ReconcileChildren(WaterNode node, HashSet<Vector3Int> desiredChildren)
        {
            List<Vector3Int> children = new List<Vector3Int>();
            foreach (Vector3Int child in node.Children)
            {
                children.Add(child);
            }

            for (int i = 0; i < children.Count; i++)
            {
                Vector3Int child = children[i];
                if (!desiredChildren.Contains(child))
                {
                    RemoveManagedBranch(child, true);
                }
            }
        }

        private static void RemoveManagedBranch(Vector3Int position, bool removeWater)
        {
            if (!Nodes.TryGetValue(position, out WaterNode node))
            {
                return;
            }

            StopWaterfallLoopsForSource(node.Source);

            List<Vector3Int> children = new List<Vector3Int>();
            foreach (Vector3Int child in node.Children)
            {
                children.Add(child);
            }

            for (int i = 0; i < children.Count; i++)
            {
                RemoveManagedBranch(children[i], true);
            }

            if (node.Kind != WaterNodeKind.Source && Nodes.TryGetValue(node.Parent, out WaterNode parent))
            {
                parent.Children.Remove(position);
                QueueWater(parent.Position);
            }

            Nodes.Remove(position);
            Queued.Remove(position);

            if (removeWater && node.Kind != WaterNodeKind.Source)
            {
                if (World.TryGetTypeAt(position, out ushort type) && type == BuiltinBlocks.Indices.water)
                {
                    ChangeBlockInternal(position, BuiltinBlocks.Types.water, BuiltinBlocks.Types.air);
                }
            }
        }

        private static bool HasSupportBelow(Vector3Int position)
        {
            Vector3Int below = position.Add(0, -1, 0);
            if (!World.TryGetTypeAt(below, out ushort type))
            {
                return false;
            }

            if (type != BuiltinBlocks.Indices.air)
            {
                return true;
            }

            return Nodes.TryGetValue(below, out WaterNode child) && child.Parent == position;
        }

        private static void TryPlayWaterfallImpactSound(WaterNode fallingNode, Vector3Int impactPosition)
        {
            if (fallingNode == null ||
                fallingNode.Kind != WaterNodeKind.Falling ||
                Config == null ||
                !Config.EnableWaterfallSounds ||
                fallingNode.FallDistance < Config.WaterfallAudioMinimumFallDistance ||
                string.IsNullOrEmpty(Config.WaterfallAudioClipName))
            {
                return;
            }

            if (!World.TryGetTypeAt(impactPosition, out ushort impactType) || impactType != BuiltinBlocks.Indices.water)
            {
                return;
            }

            if (Nodes.TryGetValue(impactPosition, out WaterNode impactNode) && impactNode.Source == fallingNode.Source)
            {
                return;
            }

            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            WaterfallSoundKey soundKey = GetWaterfallSoundKey(fallingNode, impactPosition);
            if (WaterfallSoundCooldowns.TryGetValue(soundKey, out long nextAllowed) && now < nextAllowed)
            {
                return;
            }

            WaterfallSoundCooldowns[soundKey] = now + Config.WaterfallAudioCooldownMilliseconds;
            if (!AudioManager.TryGetIndex(Config.WaterfallAudioClipName, out AudioManager.AudioClipIndex clipIndex))
            {
                if (!_waterfallAudioMissingLogged)
                {
                    BetterWaterLogger.Write("Waterfall audio clip is not registered: " + Config.WaterfallAudioClipName);
                    _waterfallAudioMissingLogged = true;
                }

                return;
            }

            try
            {
                PlayOrRefreshWaterfallLoop(soundKey, impactPosition, clipIndex, now);
                BetterWaterLogger.Debug("Waterfall loop at " + FormatPosition(impactPosition));
            }
            catch (Exception exception)
            {
                BetterWaterLogger.Write("Failed to play waterfall loop at " + FormatPosition(impactPosition) + ": " + exception.Message);
            }
        }

        private static void PlayOrRefreshWaterfallLoop(
            WaterfallSoundKey soundKey,
            Vector3Int position,
            AudioManager.AudioClipIndex clipIndex,
            long now)
        {
            int loopMilliseconds = GetWaterfallAudioLoopMilliseconds();
            int refreshMilliseconds = GetWaterfallAudioRefreshMilliseconds(loopMilliseconds);
            if (WaterfallSoundLoops.TryGetValue(soundKey, out WaterfallLoopState existing))
            {
                if (existing.Position != position || existing.ClipIndex != clipIndex)
                {
                    StopWaterfallLoop(soundKey);
                }
                else
                {
                    AudioManager.PlayLoop(position.Vector, clipIndex, existing.PlayingId, 0, loopMilliseconds);
                    existing.NextRefreshMilliseconds = now + refreshMilliseconds;
                    return;
                }
            }

            if (WaterfallSoundLoops.TryGetValue(soundKey, out existing))
            {
                existing.Position = position;
                existing.ClipIndex = clipIndex;
                AudioManager.PlayLoop(position.Vector, clipIndex, existing.PlayingId, 0, loopMilliseconds);
                existing.NextRefreshMilliseconds = now + refreshMilliseconds;
                return;
            }

            AudioManager.AudioClipPlayingID playingId = AudioManager.PlayLoop(position.Vector, clipIndex, 0, loopMilliseconds);
            AudioManager.PlayLoop(position.Vector, clipIndex, playingId, 0, loopMilliseconds);
            WaterfallSoundLoops[soundKey] = new WaterfallLoopState
            {
                PlayingId = playingId,
                ClipIndex = clipIndex,
                Position = position,
                NextRefreshMilliseconds = now + refreshMilliseconds
            };
        }

        private static WaterfallSoundKey GetWaterfallSoundKey(WaterNode fallingNode, Vector3Int impactPosition)
        {
            int radius = Config == null ? 6 : Config.WaterfallAudioClusterRadius;
            return new WaterfallSoundKey
            {
                Source = fallingNode.Source,
                Cluster = new Vector3Int(
                    FloorDivide(impactPosition.x, radius),
                    FloorDivide(impactPosition.y, radius),
                    FloorDivide(impactPosition.z, radius))
            };
        }

        private static void RefreshWaterfallSoundLoops(long now)
        {
            if (WaterfallSoundLoops.Count == 0)
            {
                return;
            }

            if (Config == null || !Config.EnableWaterfallSounds || string.IsNullOrEmpty(Config.WaterfallAudioClipName))
            {
                StopAllWaterfallLoops();
                return;
            }

            int loopMilliseconds = GetWaterfallAudioLoopMilliseconds();
            int refreshMilliseconds = GetWaterfallAudioRefreshMilliseconds(loopMilliseconds);
            WaterfallLoopScratch.Clear();
            foreach (KeyValuePair<WaterfallSoundKey, WaterfallLoopState> entry in WaterfallSoundLoops)
            {
                WaterfallLoopState state = entry.Value;
                if (!Nodes.ContainsKey(entry.Key.Source) ||
                    !World.TryGetTypeAt(state.Position, out ushort targetType) ||
                    targetType != BuiltinBlocks.Indices.water)
                {
                    WaterfallLoopScratch.Add(entry.Key);
                    continue;
                }

                if (now >= state.NextRefreshMilliseconds)
                {
                    AudioManager.PlayLoop(state.Position.Vector, state.ClipIndex, state.PlayingId, 0, loopMilliseconds);
                    state.NextRefreshMilliseconds = now + refreshMilliseconds;
                }
            }

            for (int i = 0; i < WaterfallLoopScratch.Count; i++)
            {
                StopWaterfallLoop(WaterfallLoopScratch[i]);
            }

            WaterfallLoopScratch.Clear();
        }

        private static int GetWaterfallAudioLoopMilliseconds()
        {
            if (Config == null)
            {
                return 12000;
            }

            return System.Math.Max(1000, Config.WaterfallAudioLoopMilliseconds);
        }

        private static int GetWaterfallAudioRefreshMilliseconds(int loopMilliseconds)
        {
            int configured = Config == null ? 6000 : Config.WaterfallAudioCooldownMilliseconds;
            int halfLoop = System.Math.Max(500, loopMilliseconds / 2);
            return System.Math.Max(500, System.Math.Min(configured, halfLoop));
        }

        private static void StopAllWaterfallLoops()
        {
            if (WaterfallSoundLoops.Count == 0)
            {
                return;
            }

            WaterfallLoopScratch.Clear();
            foreach (WaterfallSoundKey key in WaterfallSoundLoops.Keys)
            {
                WaterfallLoopScratch.Add(key);
            }

            for (int i = 0; i < WaterfallLoopScratch.Count; i++)
            {
                StopWaterfallLoop(WaterfallLoopScratch[i]);
            }

            WaterfallLoopScratch.Clear();
        }

        private static void StopWaterfallLoopsForSource(Vector3Int source)
        {
            if (WaterfallSoundLoops.Count == 0)
            {
                return;
            }

            WaterfallLoopScratch.Clear();
            foreach (WaterfallSoundKey key in WaterfallSoundLoops.Keys)
            {
                if (key.Source == source)
                {
                    WaterfallLoopScratch.Add(key);
                }
            }

            for (int i = 0; i < WaterfallLoopScratch.Count; i++)
            {
                StopWaterfallLoop(WaterfallLoopScratch[i]);
            }

            WaterfallLoopScratch.Clear();
        }

        private static void StopWaterfallLoop(WaterfallSoundKey key)
        {
            if (!WaterfallSoundLoops.TryGetValue(key, out WaterfallLoopState state))
            {
                return;
            }

            try
            {
                AudioManager.Stop(state.Position.Vector, state.ClipIndex, state.PlayingId);
            }
            catch
            {
            }

            WaterfallSoundLoops.Remove(key);
            WaterfallSoundCooldowns.Remove(key);
        }

        private static int FloorDivide(int value, int divisor)
        {
            if (divisor <= 1)
            {
                return value;
            }

            int result = value / divisor;
            if (value < 0 && value % divisor != 0)
            {
                result--;
            }

            return result;
        }

        private static Vector3Int NormalizeHorizontalDirection(Vector3Int direction)
        {
            if (System.Math.Abs(direction.x) >= System.Math.Abs(direction.z))
            {
                if (direction.x > 0)
                {
                    return new Vector3Int(1, 0, 0);
                }

                if (direction.x < 0)
                {
                    return new Vector3Int(-1, 0, 0);
                }
            }

            if (direction.z > 0)
            {
                return new Vector3Int(0, 0, 1);
            }

            if (direction.z < 0)
            {
                return new Vector3Int(0, 0, -1);
            }

            return new Vector3Int(0, 0, 0);
        }

        private static void PruneWaterfallSoundCooldowns(long now)
        {
            if (WaterfallSoundCooldowns.Count == 0 || now < _nextSoundCooldownCleanupMilliseconds)
            {
                return;
            }

            _nextSoundCooldownCleanupMilliseconds = now + 60000;
            WaterfallSoundCleanupScratch.Clear();
            foreach (KeyValuePair<WaterfallSoundKey, long> entry in WaterfallSoundCooldowns)
            {
                if (entry.Value < now - 60000)
                {
                    WaterfallSoundCleanupScratch.Add(entry.Key);
                }
            }

            for (int i = 0; i < WaterfallSoundCleanupScratch.Count; i++)
            {
                WaterfallSoundCooldowns.Remove(WaterfallSoundCleanupScratch[i]);
            }

            WaterfallSoundCleanupScratch.Clear();
        }

        private static void TryShowSourceMarkers(long now)
        {
            if (Config == null ||
                !Config.ShowSourceMarkers ||
                Nodes.Count == 0 ||
                now < _nextSourceMarkerMilliseconds)
            {
                return;
            }

            _nextSourceMarkerMilliseconds = now + Config.SourceMarkerIntervalMilliseconds;
            foreach (WaterNode node in Nodes.Values)
            {
                if (node.Kind != WaterNodeKind.Source ||
                    !World.TryGetTypeAt(node.Position, out ushort type) ||
                    type != BuiltinBlocks.Indices.water)
                {
                    continue;
                }

                UnityEngine.Vector3 markerPosition = node.Position.Vector + new UnityEngine.Vector3(0.5f, 1.1f, 0.5f);
                Indicator.SendIconIndicatorNear(
                    markerPosition,
                    IndicatorState.NewItemIndicator(Config.SourceMarkerDurationSeconds, BuiltinBlocks.Indices.water));
            }
        }

        private static WaterNode EnsureSource(Vector3Int position)
        {
            if (Nodes.TryGetValue(position, out WaterNode existing))
            {
                if (existing.Kind != WaterNodeKind.Source)
                {
                    existing.Kind = WaterNodeKind.Source;
                    existing.Parent = position;
                    existing.Source = position;
                    existing.FallDistance = 0;
                    existing.HorizontalDistance = 0;
                    existing.AllowHorizontalSpread = true;
                    existing.AllowLandingSpread = true;
                    existing.IsChannel = false;
                    existing.FlowDirection = new Vector3Int(0, 0, 0);
                }

                return existing;
            }

            WaterNode node = new WaterNode
            {
                Position = position,
                Source = position,
                Parent = position,
                Kind = WaterNodeKind.Source,
                FallDistance = 0,
                HorizontalDistance = 0,
                AllowHorizontalSpread = true,
                AllowLandingSpread = true,
                IsChannel = false,
                FlowDirection = new Vector3Int(0, 0, 0)
            };

            Nodes[position] = node;
            QueueWater(position);
            BetterWaterLogger.Debug("Registered source at " + FormatPosition(position));
            return node;
        }

        private static EServerChangeBlockResult ChangeBlockInternal(Vector3Int position, ItemTypes.ItemType oldType, ItemTypes.ItemType newType)
        {
            InternalChanges.Add(position);
            try
            {
                return ServerManager.TryChangeBlock(position, oldType, newType, FlowOrigin);
            }
            finally
            {
                InternalChanges.Remove(position);
            }
        }

        private static void QueueWater(Vector3Int position)
        {
            if (Queued.Add(position))
            {
                Queue.Enqueue(position);
            }
        }

        private static void QueueWaterNeighbours(Vector3Int position)
        {
            QueueWater(position.Add(1, 0, 0));
            QueueWater(position.Add(-1, 0, 0));
            QueueWater(position.Add(0, 1, 0));
            QueueWater(position.Add(0, -1, 0));
            QueueWater(position.Add(0, 0, 1));
            QueueWater(position.Add(0, 0, -1));
        }

        private static void QueueAllSources()
        {
            foreach (WaterNode node in Nodes.Values)
            {
                if (node.Kind == WaterNodeKind.Source)
                {
                    QueueWater(node.Position);
                }
            }
        }

        private static void ApplyVanillaSpreadMode()
        {
            if (!_itemTypesReady || Config == null)
            {
                return;
            }

            if (Config.VanillaSpreadEnabled || !Config.Enabled)
            {
                RestoreVanillaSpread();
            }
            else
            {
                SuppressVanillaSpread();
            }
        }

        private static void SuppressVanillaSpread()
        {
            ResolveVanillaWaterFields();
            if (_vanillaUpdatesPerTickField == null)
            {
                return;
            }

            try
            {
                object current = _vanillaUpdatesPerTickField.GetValue(null);
                if (current is int value && value > 0)
                {
                    _vanillaUpdatesPerTickFallback = value;
                }

                _vanillaUpdatesPerTickField.SetValue(null, 0);
                ClearVanillaLocations();
            }
            catch (Exception exception)
            {
                BetterWaterLogger.Write("Failed to suppress vanilla spread: " + exception.Message);
            }
        }

        private static void RestoreVanillaSpread()
        {
            ResolveVanillaWaterFields();
            if (_vanillaUpdatesPerTickField == null)
            {
                return;
            }

            try
            {
                int updates = _vanillaUpdatesPerTickFallback > 0
                    ? _vanillaUpdatesPerTickFallback
                    : ServerManager.ServerSettings.Water.MaxUpdatesPerTick;
                _vanillaUpdatesPerTickField.SetValue(null, updates);
            }
            catch (Exception exception)
            {
                BetterWaterLogger.Write("Failed to restore vanilla spread: " + exception.Message);
            }
        }

        private static void SeedVanillaWaterQueue()
        {
            foreach (WaterNode node in Nodes.Values)
            {
                if (node.Kind == WaterNodeKind.Source)
                {
                    try
                    {
                        BlockEntities.Implementations.Water.AddLocation(node.Position);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void ClearVanillaLocations()
        {
            ResolveVanillaWaterFields();
            if (_vanillaLocationsToCheckField == null)
            {
                return;
            }

            try
            {
                object locations = _vanillaLocationsToCheckField.GetValue(null);
                MethodInfo clear = locations?.GetType().GetMethod(
                    "Clear",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
                clear?.Invoke(locations, null);
            }
            catch (Exception exception)
            {
                BetterWaterLogger.Write("Failed to clear vanilla water queue: " + exception.Message);
            }
        }

        private static void TryLoadAudioPatches()
        {
            if (string.IsNullOrEmpty(ModFolder))
            {
                return;
            }

            TryQueueAudioPatch(Path.Combine(ModFolder, "Audio", "audioFiles.json"));
        }

        private static void TryQueueAudioPatch(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                AudioManager.QueueAudioPatchFile(path, 100);
                BetterWaterLogger.Write("Queued audio patch " + path);
            }
            catch (Exception exception)
            {
                BetterWaterLogger.Write("Failed to queue audio patch " + path + ": " + exception.Message);
            }
        }

        private static void ResolveVanillaWaterFields()
        {
            if (_vanillaUpdatesPerTickField != null)
            {
                return;
            }

            Type vanillaWaterType = typeof(BlockEntities.Implementations.Water);
            _vanillaUpdatesPerTickField = vanillaWaterType.GetField("UpdatesPerTick", BindingFlags.Static | BindingFlags.NonPublic);
            _vanillaLocationsToCheckField = vanillaWaterType.GetField("locationsToCheck", BindingFlags.Static | BindingFlags.NonPublic);
            if (_vanillaUpdatesPerTickField == null)
            {
                BetterWaterLogger.Write("Could not find vanilla water UpdatesPerTick field.");
            }
        }

        private static void ResolveBucketItemTypes()
        {
            _emptyBucketType = 0;
            _waterBucketType = 0;

            if (!ItemTypes.IndexLookup.TryGetIndex(EmptyBucketTypeName, out _emptyBucketType))
            {
                BetterWaterLogger.Write("Could not find item type " + EmptyBucketTypeName);
            }

            if (!ItemTypes.IndexLookup.TryGetIndex(WaterBucketTypeName, out _waterBucketType))
            {
                BetterWaterLogger.Write("Could not find item type " + WaterBucketTypeName);
            }
        }

        private static WaterNode ReadNode(JObject obj)
        {
            try
            {
                WaterNodeKind kind;
                if (!Enum.TryParse(obj.Value<string>("kind") ?? "Source", out kind))
                {
                    kind = WaterNodeKind.Source;
                }

                Vector3Int position = new Vector3Int(obj.Value<int>("x"), obj.Value<int>("y"), obj.Value<int>("z"));
                Vector3Int flowDirection = new Vector3Int(
                    obj.Value<int?>("directionX") ?? 0,
                    obj.Value<int?>("directionY") ?? 0,
                    obj.Value<int?>("directionZ") ?? 0);

                return new WaterNode
                {
                    Position = position,
                    Kind = kind,
                    Source = new Vector3Int(obj.Value<int>("sourceX"), obj.Value<int>("sourceY"), obj.Value<int>("sourceZ")),
                    Parent = new Vector3Int(obj.Value<int>("parentX"), obj.Value<int>("parentY"), obj.Value<int>("parentZ")),
                    FallDistance = System.Math.Max(0, obj.Value<int?>("fallDistance") ?? 0),
                    HorizontalDistance = System.Math.Max(0, obj.Value<int?>("horizontalDistance") ?? 0),
                    AllowHorizontalSpread = obj.Value<bool?>("allowHorizontalSpread") ?? true,
                    AllowLandingSpread = obj.Value<bool?>("allowLandingSpread") ?? true,
                    IsChannel = obj.Value<bool?>("isChannel") ?? false,
                    FlowDirection = NormalizeHorizontalDirection(flowDirection)
                };
            }
            catch (Exception exception)
            {
                BetterWaterLogger.Write("Failed to read saved node: " + exception.Message);
                return null;
            }
        }

        private static void EnsureConfig()
        {
            if (Config == null)
            {
                Config = new BetterWaterConfig();
                Config.Normalize();
            }
        }

        private static string FormatPosition(Vector3Int position)
        {
            return position.x + "," + position.y + "," + position.z;
        }
    }

    public sealed class BetterWaterCallbacks :
        IModCallbackConsumer,
        IAfterItemTypesDefined,
        IOnUpdate,
        IOnPlayerClicked,
        IOnSaveWorldMisc,
        IOnLoadWorldMisc
    {
        [ModLoader.ModCallback(BetterWaterModEntry.Namespace + ".AfterItemTypesDefined", 100f)]
        public void AfterItemTypesDefined()
        {
            BetterWaterModEntry.OnAfterItemTypesDefined();
        }

        [ModLoader.ModCallback(BetterWaterModEntry.Namespace + ".OnUpdate")]
        public void OnUpdate()
        {
            BetterWaterModEntry.OnUpdate();
        }

        [ModLoader.ModCallback(BetterWaterModEntry.Namespace + ".OnPlayerClicked", 200f)]
        public void OnPlayerClicked(Players.Player player, PlayerClickedData click)
        {
            BetterWaterModEntry.OnPlayerClicked(player, click);
        }

        [ModLoader.ModCallback(BetterWaterModEntry.Namespace + ".SaveWorldMisc")]
        public void OnSaveWorldMisc(JObject data)
        {
            BetterWaterModEntry.OnSaveWorldMisc(data);
        }

        [ModLoader.ModCallback(BetterWaterModEntry.Namespace + ".LoadWorldMisc")]
        public void OnLoadWorldMisc(JObject data)
        {
            BetterWaterModEntry.OnLoadWorldMisc(data);
        }
    }
}
