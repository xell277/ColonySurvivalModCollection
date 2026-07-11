using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using Newtonsoft.Json;
using Pipliz;
using Chatting;
using Chatting.Commands;
using BlockEntities.Implementations;
using NPC;
using Jobs;
using UnityEngine;
using Shared.Networking;
using MeshedObjects;
using NetworkUI;
using NetworkUI.Items;

namespace ColonyCommands {

	public static class AntiGrief
	{
		public const string MOD_PREFIX = "mods.scarabol.commands.";
		public const string NAMESPACE = "AntiGrief";
		public static string MOD_DIRECTORY;
		public const string PERMISSION_SUPER = "mods.scarabol.antigrief";
		public const string PERMISSION_SPAWN_CHANGE = PERMISSION_SUPER + ".spawnchange";
		public const string PERMISSION_BANNER_PREFIX = PERMISSION_SUPER + ".banner.";
		private const string COLONY_ID_FORMAT = "colony.{0:0000000000}";
		private const float JOIN_MESSAGE_DELAY_SECONDS = 10.0f;
		private const int JOIN_POPUP_WIDTH = 420;
		private const int JOIN_POPUP_HEIGHT = 180;

		public static ModConfig config;
		public static MethodInfo AngryGuardsWarMode = null;
		public static List<MethodInfo> ChatColorForeignModMethods = new List<MethodInfo>();
		public static GameCallbackManager BaseGameCallbacks = new GameCallbackManager();

		public static Dictionary<Players.Player, int> KillCounter = new Dictionary<Players.Player, int>();

		const string CONFIG_FILE = "antigrief-config.json";
		const string CONFIG_EXAMPLE = "antigrief-config.example.json";
		static string ConfigFilePath {
			get {
				return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, CONFIG_FILE));
			}
		}


		public static void OnAssemblyLoaded(string path)
		{
			MOD_DIRECTORY = Path.GetDirectoryName(path);
			Log.Write("Loaded ColonyCommands (Anti-Grief)");
		}


		public static void AfterItemTypesDefined()
		{
			Log.Write("Registering commands (Anti-Grief)");
			CommandManager.RegisterCommand(new AnnouncementsChatCommand());
			CommandManager.RegisterCommand(new AntiGriefChatCommand());
			CommandManager.RegisterCommand(new CustomAreaCommand());
			CommandManager.RegisterCommand(new BanChatCommand());
			CommandManager.RegisterCommand(new BannerNameChatCommand());
			CommandManager.RegisterCommand(new BetterChatCommand());
			CommandManager.RegisterCommand(new ColonyCap());
			CommandManager.RegisterCommand(new DrainChatCommand());
			CommandManager.RegisterCommand(new GodChatCommand());
			CommandManager.RegisterCommand(new InactiveChatCommand());
			CommandManager.RegisterCommand(new ItemIdChatCommand());
			CommandManager.RegisterCommand(new KickChatCommand());
			CommandManager.RegisterCommand(new KillNPCsChatCommand());
			CommandManager.RegisterCommand(new KillPlayerChatCommand());
			CommandManager.RegisterCommand(new LastSeenChatCommand());
			CommandManager.RegisterCommand(new NoFlightChatCommand());
			CommandManager.RegisterCommand(new OnlineChatCommand());
			CommandManager.RegisterCommand(new ServerPopCommand());
			CommandManager.RegisterCommand(new StuckChatCommand());
			CommandManager.RegisterCommand(new TopChatCommand());
			CommandManager.RegisterCommand(new TradeChatCommand());
			CommandManager.RegisterCommand(new TrashChatCommand());
			CommandManager.RegisterCommand(new TravelChatCommand());
			CommandManager.RegisterCommand(new WarpChatCommand());
			CommandManager.RegisterCommand(new WarpBannerChatCommand());
			CommandManager.RegisterCommand(new WarpPlaceChatCommand());
			CommandManager.RegisterCommand(new WarpSpawnChatCommand());
			CommandManager.RegisterCommand(new WhisperChatCommand());
			CommandManager.RegisterCommand(new SetJailCommand());
			CommandManager.RegisterCommand(new JailCommand());
			CommandManager.RegisterCommand(new JailReleaseCommand());
			CommandManager.RegisterCommand(new JailVisitCommand());
			CommandManager.RegisterCommand(new JailLeaveCommand());
			CommandManager.RegisterCommand(new JailRecCommand());
			CommandManager.RegisterCommand(new JailTimeCommand());
			//CommandManager.RegisterCommand(new AreaShowCommand());
			CommandManager.RegisterCommand(new HelpCommand());
			CommandManager.RegisterCommand(new ColorTestCommand());
			CommandManager.RegisterCommand(new SpawnNpcCommand());
			CommandManager.RegisterCommand(new BedsCommand());
			CommandManager.RegisterCommand(new PurgeBannerCommand());
			CommandManager.RegisterCommand(new MuteChatCommand());
			CommandManager.RegisterCommand(new UnmuteChatCommand());
			CommandManager.RegisterCommand(new ListPlayerChatCommand());
			CommandManager.RegisterCommand(new WarChatCommand());
			CommandManager.RegisterCommand(new PromoteChatCommand());
			CommandManager.RegisterCommand(new GracePeriodChatCommand());
			CommandManager.RegisterCommand(new RoleplayChatCommand());
			CommandManager.RegisterCommand(new RoleplayBanChatCommand());
			CommandManager.RegisterCommand(new RoleplayUnbanChatCommand());
			CommandManager.RegisterCommand(new MTopChatCommand());
			CommandManager.RegisterCommand(new TotalChatCommand());
			CommandManager.RegisterCommand(new BanLogChatCommand());
			return;
		}


		public static void OnTryChangeBlock(ModLoader.OnTryChangeBlockData userData)
		{
			if (config == null || userData == null || userData.TypeNew == null) {
				return;
			}

			// TryChangeBlock can be caused by both players and colonies (Builder/Digger)
			Players.Player causedBy = null;
			if (userData.RequestOrigin.Type == BlockChangeRequestOrigin.EType.Player) {
				causedBy = userData.RequestOrigin.AsPlayer;
			} else if (userData.RequestOrigin.Type == BlockChangeRequestOrigin.EType.Colony) {
				Colony colony = userData.RequestOrigin.AsColony;
				if (colony == null || colony.ColonyGroup == null || colony.ColonyGroup.Owners.Count == 0) {
					return;
				}
				causedBy = colony.ColonyGroup.Owners[0];	// colony leader
			}
			// allow staff members and server itself
			if (causedBy == null || PermissionsManager.HasPermission(causedBy, PERMISSION_SUPER)) {
				return;
			}

			// prevent dangerous blocks
			if (userData.TypeNew == BlockTypes.BuiltinBlocks.Types.water && !PermissionsManager.HasPermission(causedBy, MOD_PREFIX + ".placewater")) {
				Chat.Send(causedBy, "<color=red>You don't have permission to place this block!</color>");
				BlockCallback(userData);
				return;
			}

			Pipliz.Vector3Int playerPos = userData.Position;

			// check spawn area
			int ox = (int)System.Math.Abs(playerPos.x - ServerManager.GetSpawnPoint().Position.x);
			int oz = (int)System.Math.Abs(playerPos.z - ServerManager.GetSpawnPoint().Position.z);
			if (ox <= config.SpawnProtectionRange && oz <= config.SpawnProtectionRange) {
				if (!PermissionsManager.HasPermission(causedBy, PERMISSION_SPAWN_CHANGE)) {
					if (causedBy.ConnectionState == Players.EConnectionState.Connected) {
						Chat.Send(causedBy, "<color=red>You don't have permission to change the spawn area!</color>");
					}
					BlockCallback(userData);
					return;
				}
			}

			// check custom protection areas
			foreach (CustomProtectionArea area in config.CustomAreas) {
				if (area.Contains(playerPos) && !PermissionsManager.HasPermission(causedBy, PERMISSION_SPAWN_CHANGE)) {
					if (causedBy.ConnectionState == Players.EConnectionState.Connected) {
						Chat.Send(causedBy, "<color=red>You don't have permission to change this protected area!</color>");
					}
					BlockCallback(userData);
					return;
				}
			}

			// Check all banners and then decide by Colony.ColonyGroup.Owners if allowed or not
			bool isBannerBlock = false;
			int checkRange = config.BannerProtectionRange;
			if (userData.TypeNew.ItemIndex == BlockTypes.BuiltinBlocks.Indices.banner ||
				userData.TypeNew.ItemIndex == BlockTypes.BuiltinBlocks.Indices.outpostbanner) {
				checkRange *= 2;
				isBannerBlock = true;
			}
			Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (colonyGroupEnum.MoveNext()) {
				ColonyGroup checkColonyGroup = colonyGroupEnum.Current;
				for (int i = 0; i < checkColonyGroup.Colonies.Count; ++i) {
					Colony checkColony = checkColonyGroup.Colonies.GetAt(i);
					for (int j = 0; j < checkColony.Banners.Count; ++j) {
						BannerTracker.Banner checkBanner = checkColony.Banners.GetAt(j);
						int distanceX = (int)System.Math.Abs(playerPos.x - checkBanner.Position.x);
						int distanceZ = (int)System.Math.Abs(playerPos.z - checkBanner.Position.z);
						int distanceY = (int)System.Math.Abs(playerPos.y - checkBanner.Position.y);

						if (distanceX <= checkRange && distanceZ <= checkRange && (!config.ProtectionIncludesHeightCheck || distanceY <= checkRange)) {
							if (checkColony.ColonyGroup.Owners.IndexOf(causedBy) >= 0) {
								if (!isBannerBlock) {
									return;
								} else {
									continue; // banner block still has to check if overlapping with any other colony
								}
							}

							// check if /antigrief permission - only done for banner placement
							if (isBannerBlock) {
								// permission for this colony id
								if (PermissionsManager.HasPermission(causedBy, PERMISSION_BANNER_PREFIX + string.Format(COLONY_ID_FORMAT, checkColony.ColonyID))) {
									return;
								}

								// permission for all colonies of the owner
								for (int k = 0; k < checkColony.ColonyGroup.Owners.Count; ++k) {
									Players.Player owner = checkColony.ColonyGroup.Owners.GetAt(k);
									if (PermissionsManager.HasPermission(causedBy, PERMISSION_BANNER_PREFIX + owner.ID.SteamID.m_SteamID)) {
										return;
									}
								}
							}

							if (isBannerBlock) {
								int tooCloseX = (int)System.Math.Abs(checkRange - distanceX) + 1;
								int tooCloseZ = (int)System.Math.Abs(checkRange - distanceZ) + 1;
								int distance = tooCloseX < tooCloseZ ? tooCloseX : tooCloseZ;
								string direction = tooCloseX < tooCloseZ ? "east/west" : "north/south";
								if (causedBy.ConnectionState == Players.EConnectionState.Connected) {
									Chat.Send(causedBy, $"<color=red>Too close near {checkColony.Name}! Please move {distance} more blocks {direction}.</color>");
								}
							} else {
								if (causedBy.ConnectionState == Players.EConnectionState.Connected) {
									Chat.Send(causedBy, "<color=red>No permission to change blocks near this banner!</color>");
								}
							}
							BlockCallback(userData);
							return;
						}
					}
				}
			}

			return;
		}


		// Deny a TryChangeBlock event
		static void BlockCallback(ModLoader.OnTryChangeBlockData userData)
		{
			userData.CallbackState = ModLoader.OnTryChangeBlockData.ECallbackState.Cancelled;
			userData.InventoryItemResults.Clear();
		}


		// load everything after the world starts
		public static void AfterWorldLoad()
		{
			Load();
			CheckColonistLimit();

			if (config.OnlineBackupIntervalHours > 0) {
				Log.Write($"Found online backup interval setting {config.OnlineBackupIntervalHours}h");
				ThreadManager.InvokeOnMainThread(delegate {
					PerformOnlineBackup();
				}, config.OnlineBackupIntervalHours * 60f * 60f);
			}

			// TODO ?? if required at all
			/*if (config.ColonistPerColonyTierLimits.Count > 0) {
				Log.Write("Found per colony tier/difficulty limit settings");
				ThreadManager.InvokeOnMainThread(delegate {
					PerformColonistPerColonyLimitCheck();
				}, config.ColonyColonistLimitTierCheckSeconds);
			}*/

			// check and remove empty banners
			ThreadManager.InvokeOnMainThread(delegate { StartupEmptyColonyCheck(); }, 160.0);
		}


		// load config
		public static void Load()
		{
			string configDirectory = Path.GetDirectoryName(ConfigFilePath);
			if (!string.IsNullOrEmpty(configDirectory)) {
				Directory.CreateDirectory(configDirectory);
			}

			if (!File.Exists(ConfigFilePath)) {
				config = new ModConfig();
				Log.Write($"Creating default configuration {ConfigFilePath}");
				Save();
			} else {
				Log.Write($"Loading config from {CONFIG_FILE}");
			}

			try {
				JsonSerializer js = new JsonSerializer();
				JsonTextReader jtr = new JsonTextReader(new StreamReader(ConfigFilePath));
				config = js.Deserialize<ModConfig>(jtr);
				jtr.Close();
			} catch (Exception e) {
				Log.Write($"ERROR: Could not parse {CONFIG_FILE}: {e.Message}");
			}

			// Try to catch config file errors
			if (config == null || config.SpawnProtectionRange == 0 || config.BannerProtectionRange == 0) {
				int spawnRange = config == null ? 0 : config.SpawnProtectionRange;
				int bannerRange = config == null ? 0 : config.BannerProtectionRange;
				Log.Write("ERROR: ProtectionRange is zero, meaning NO PROTECTION AT ALL");
				Log.Write($"ERROR: SpawnProtectionRange={spawnRange}, BannerProtectionRange={bannerRange}");
				Log.Write("ERROR: please double check your antigrief-config.json File!");
			}
		}

		public static void AddCustomArea (CustomProtectionArea area)
		{
			config.CustomAreas.Add(area);
			Save();
		}

		public static void RemoveCustomArea(CustomProtectionArea area)
		{
			config.CustomAreas.Remove(area);
			Save();
		}

		// save config
		public static void Save()
		{
			Log.Write($"Saving {CONFIG_FILE}");
			try {
				JsonSerializer json = new JsonSerializer();
				JsonTextWriter jsonWriter = new JsonTextWriter(new StreamWriter(ConfigFilePath));
				json.Formatting = Formatting.Indented;
				json.Serialize(jsonWriter, config);
				jsonWriter.Close();
			} catch (Exception e) {
				Log.Write($"Error saving {CONFIG_FILE}: {e.Message}");
			}
		}


		// track NPC killing
		public static void OnNPCHit(NPC.NPCBase npc, ModLoader.OnHitData data)
		{
			if (!IsKilled(npc, data) || !IsHitByPlayer(data.HitSourceType) || !(data.HitSourceObject is Players.Player)) {
				return;
			}
			Players.Player killer = (Players.Player)data.HitSourceObject;
			if (npc.Colony.ColonyGroup.Owners.IndexOf(killer) >= 0) {
				return;
			}

			// WAR mode: killing NPC is allowed if killer and target colony are war enabled
			if (WarManager.IsWarEnabled(killer) && WarManager.IsWarEnabled(npc.Colony)) {
				return;
			}

			int kills;
			if (!KillCounter.TryGetValue(killer, out kills)) {
				kills = 0;
			}
			KillCounter[killer] = ++kills;

			// ban
			if (config.NpcKillsBanThreshold > 0 && kills >= config.NpcKillsBanThreshold) {
				Chat.SendToConnected($"{killer.Name} banned for killing too many colonists");
				BlackAndWhitelisting.AddBlackList(killer.ID.SteamID.m_SteamID);
				Players.Disconnect(killer);
				for (int i = 0; i < killer.ColonyGroups.Count; ++i) {
					ColonyGroup colonyGroup = killer.ColonyGroups.GetAt(i);
					for (int j = 0; j < colonyGroup.Colonies.Count; ++j) {
						Colony colony = colonyGroup.Colonies.GetAt(j);
						Log.Write($"Purging colony {colony.Name} from banned player {killer.Name}");
						if (colony.ColonyGroup.Owners.Count == 1) {
							ServerManager.ClientCommands.DeleteColonyAndBanner(null, colony, colony.Banners[0].Position);
						} else {
							colonyGroup.RemoveOwner(killer);
						}
					}
				}

			// kick
			} else if (config.NpcKillsKickThreshold > 0 && kills >= config.NpcKillsKickThreshold) {
				Chat.SendToConnected($"{killer.Name} kicked for killing too many colonists");
				Players.Disconnect(killer);

			// jail
			} else if (config.NpcKillsJailThreshold > 0 && kills >= config.NpcKillsJailThreshold && JailManager.validJail) {
				Chat.SendToConnected($"{killer.Name} put in Jail for killing too many colonists");
				JailManager.jailPlayer(killer, null, "Killing Colonists", JailManager.jailConfig.defaultJailTime);
			}

			Log.Write($"{killer.Name} killed a colonist of {npc.Colony.Name} at {npc.Position}");
			int remainingJail = config.NpcKillsJailThreshold - kills;
			int remainingKick = config.NpcKillsKickThreshold - kills;
			string msg = "You killed a colonist";
			if (config.NpcKillsJailThreshold > 0) {
				msg += $", remaining until jail: {remainingJail}";
			}
			if (config.NpcKillsKickThreshold > 0) {
				msg += $", remaining until kick: {remainingKick}";
			}
			Chat.Send(killer, msg);
		}

		static bool IsKilled(NPC.NPCBase npc, ModLoader.OnHitData data)
		{
			return npc.health - data.ResultDamage <= 0;
		}

		static bool IsHitByPlayer(ModLoader.OnHitData.EHitSourceType hitSourceType)
		{
			return hitSourceType == ModLoader.OnHitData.EHitSourceType.PlayerClick ||
				hitSourceType == ModLoader.OnHitData.EHitSourceType.PlayerProjectile ||
				hitSourceType == ModLoader.OnHitData.EHitSourceType.Misc;
		}

		// check colonist limit (total colonists per player)
		public static void CheckColonistLimit()
		{
			if (config.ColonistLimit < 1) {
				return;
			}

			int total_killed = 0;
			foreach (Players.Player target in Players.PlayerDatabase.Values) {
				if (target.ColonyGroups.Count == 0 || total_killed > config.ColonistLimitMaxKillPerIteration) {
					continue;
				}
				int player_colonists = 0;
				int killed_per_player = 0;
				for (int i = 0; i < target.ColonyGroups.Count; ++i) {
					ColonyGroup checkColonyGroup = target.ColonyGroups.GetAt(i);
					if (checkColonyGroup.Owners.GetAt(0) == target) {
						player_colonists += checkColonyGroup.FollowerCountSum;
					}
				}

				// calculate effective limit to allow tier levels per player
				int effectiveLimit = config.ColonistLimit;
				for (int i = 0; i < config.ColonistTierLimits.Count; i++) {
					if (config.ColonistTierLimits[i] > 0 && PermissionsManager.HasPermission(target, $"colonistcapacity.tier{i+1}")) {
						effectiveLimit = config.ColonistTierLimits[i];
					}
				}
				if (player_colonists <= effectiveLimit) {
					continue;
				}

				for (int i = target.ColonyGroups.Count - 1; i >= 0; --i) {
					ColonyGroup colonyGroup = target.ColonyGroups.GetAt(i);
					for (int j = colonyGroup.Colonies.Count - 1; j >= 0; --j) {
						Colony colony = colonyGroup.Colonies.GetAt(j);

						if (colony.JobFinder.AutoRecruit) {
							JobFinder colonyJobFinder = colony.JobFinder;
							colonyJobFinder.AutoRecruit = false;
						}

						int killed_per_colony = 0;
						List<NPCBase> cachedFollowers = new List<NPCBase>(colony.Followers);
						int k = cachedFollowers.Count - 1;
						while (player_colonists > effectiveLimit && total_killed < config.ColonistLimitMaxKillPerIteration && k >= 0) {
							cachedFollowers[k--].OnDeath();
							player_colonists--;
							killed_per_colony++;
							killed_per_player++;
							total_killed++;
						}
						if (killed_per_colony > 0) {
							Log.Write($"ColonyCap: killed {killed_per_colony} colonists of {target.Name} in colony {colony.Name}. Player total: {player_colonists} (limit: {effectiveLimit})");
						}
					}
				}
				if (target.ConnectionState == Players.EConnectionState.Connected) {
					Chat.Send(target, $"<color=red>Colonists are dying, limit is {effectiveLimit}</color>");
				}
			}

			if (total_killed > 0) {
				Log.Write($"ColonyCap: killed {total_killed} colonists in total");
			}

			ThreadManager.InvokeOnMainThread(delegate() {
				CheckColonistLimit();
			}, config.ColonistLimitCheckSeconds + 0.150);
		}

/*		public static void PerformColonistPerColonyLimitCheck()
		{
			int total_killed = 0;
			foreach (Colony colony in ServerManager.ColonyTracker.ColoniesByID.Values) {
				if (colony.Followers.Count == 0) {
					continue;
				}
				byte zombieDayIndex = ServerManager.WorldSettingsReadOnly.DifficultyDayMonsters;
				byte zombieNightIndex = ServerManager.WorldSettingsReadOnly.DifficultyNightMonsters;
				// bool happiness = ServerManager.WorldSettingsReadOnly.EnableHappiness;
				JSONNode node = new JSONNode(NodeType.Object).SetAs("difficulty", "1");
				Difficulty.ColonyDifficultySetting setting = (Difficulty.ColonyDifficultySetting)colony.DifficultySetting;
				if (setting != null) {
					setting.SerializeToColonyJSON(node, colony);
					node = node.GetAsOrDefault<JSONNode>("difficulty", null);
					if (node != null) {
						zombieDayIndex = node.GetAs<byte>("day_cd");
						zombieNightIndex = node.GetAs<byte>("night_cd");
						// happiness = node.GetAs<bool>("enablehappiness");
					}
				}
				int difficultyIndex = zombieDayIndex + zombieNightIndex;

				// find the correct tier limits by leader player
				Players.Player owner = colony.ColonyGroup.Owners[0];
				List<int> difficultyLimits = null;
				for (int i = 0; i < ColonistPerColonyTierLimits.Count; i++) {
					if (PermissionsManager.HasPermission(owner, $"colonistcapacity.tier{i+1}")) {
						difficultyLimits = ColonistPerColonyTierLimits[i];
					}
				}
				if (difficultyLimits == null || difficultyLimits.Count == 0) {
					continue;
				}

				// find the colonist limit based on difficulty
				int effectiveLimit = 0;
				if (difficultyIndex >= difficultyLimits.Count) {
					effectiveLimit = difficultyLimits[difficultyLimits.Count - 1];
				} else {
					effectiveLimit = difficultyLimits[difficultyIndex];
				}
				if (effectiveLimit == 0) {
					continue;
				}

				if (colony.Followers.Count <= effectiveLimit) {
					// remove from 'warning' list if below limit
					if (ColonyColonistLimitTiers.ContainsKey(colony)) {
						ColonyColonistLimitTiers.Remove(colony);
					}
					continue;
				}

				// above limit. send out warnings at first
				Log.Write($"Colony {colony.Name} is at {colony.Followers.Count} colonists. Limit is {effectiveLimit} for difficulty {difficultyIndex}");
				if (!ColonyColonistLimitTiers.ContainsKey(colony)) {
					ColonyColonistLimitTiers[colony] = 0;
				}
				int warnCount = ColonyColonistLimitTiers[colony];
				if (warnCount < ColonyColonistLimitTierWarnTimes && owner.ConnectionState == Players.EConnectionState.Connected) {
					Chat.Send(owner, $"<color=yellow>{colony.Name} is above the colonist limit ({effectiveLimit}) for your difficulty setting. Please increase zombie spawn level!</color>");
					ColonyColonistLimitTiers[colony] += 1; // inc warning count
				} else {
					Chat.Send(owner, $"<color=red>{colony.Name} is still above the colonist limit ({effectiveLimit}) for your difficulty setting. Killing colonists.</color>");
					int killed = 0;
					List<NPCBase> cachedFollowers = new List<NPCBase>(colony.Followers);
					int j = cachedFollowers.Count - 1;
					while (colony.Followers.Count > effectiveLimit && total_killed < config.ColonistLimitMaxKillPerIteration) {
						cachedFollowers[j--].OnDeath();
						killed++;
						total_killed++;
					}
					Log.Write($"Killed {killed} colonist from {colony.Name}, owner is {owner.Name}");

					if (colony.Followers.Count <= effectiveLimit) {
						ColonyColonistLimitTiers.Remove(colony);
					}
				}
			}
			Log.Write($"Colony difficulty/tier limit check: killed {total_killed} in total");

			// queue the next iteration
			ThreadManager.InvokeOnMainThread(delegate {
				PerformColonistPerColonyLimitCheck();
			}, ColonyColonistLimitTierCheckSeconds);
		}
*/


		// TODO
		public static void PerformOnlineBackup()
		{
/*			double timeStart = Pipliz.Time.SecondsSinceStartDouble;
			Chat.SendToConnected("Starting online backup", EChatSendOptions.Default);

			string backupPath = "gamedata/savegames/" + ServerManager.WorldName + "-" + Pipliz.Time.FullTimeStamp();
			int num = 0;
			string text = backupPath + ".zip";
			while (File.Exists(text)) {
				num++;
				text = backupPath + $"-{num:02}.zip";
			}
			backupPath = text;

			ModLoader.Callbacks.OnAutoSaveWorld.Invoke();
			ServerManager.SaveManager.FlushAllDirtyChunks();
			ServerManager.SaveManager.EnqueueJob(new SaveManager.SaveJob(delegate (SaveManager.ChunkStorage storage) {
				Pipliz.Application.WaitForQuitsNoLogging();
				storage.FlushChunksToFreeForced();
				storage.Close();
				ZipFile.CreateFromDirectory("gamedata/savegames/" + ServerManager.WorldName, backupPath, CompressionLevel.Optimal, true);
				Chat.SendToConnected ("Backup complete", EChatSendOptions.Default);
			}));
			double secondsSinceStartDouble = Pipliz.Time.SecondsSinceStartDouble;
			Log.Write($"Online Backup completed; took {secondsSinceStartDouble - timeStart:F3} seconds");

			// queue the next iteration
			ThreadManager.InvokeOnMainThread(delegate {
				PerformOnlineBackup();
			}, OnlineBackupIntervalHours * 60f * 60f);
*/		}

		// enable hooks into other mods (AngryGuards and Imperium, for now)
		public static void AfterModsLoaded(List<ModLoader.ModDescription> mods)
		{
			Assembly angryguards = null, imperium = null;
			for (int i = 0; i < mods.Count; i++) {
				if (mods[i].name == "Angry Guards") {
					angryguards = mods[i].LoadedAssembly;
					Log.Write("ColonyCommands: found AngryGuards mod, enabling hook");
				} else if (mods[i].name == "Imperium") {
					imperium = mods[i].LoadedAssembly;
					Log.Write("ColonyCommands: found Imperium mod, enabling hook");
				}
			}
			if (angryguards != null) {
				foreach (Type t in angryguards.GetTypes()) {
					if (t.FullName == "AngryGuards.AngryGuards") {
						MethodInfo m = t.GetMethod("ColonySetWarMode");
						if (m != null) {
							Log.Write("Method AngryGuards.ColonySetWarMode found, hook enabled");
							AngryGuardsWarMode = m;
						}
					}
				}
			}

			// hook to enable chat colors / modifications
			if (imperium != null) {
				foreach (Type t in imperium.GetTypes()) {
					if (t.FullName == "Imperium.Imperium") {
						MethodInfo m = t.GetMethod("ChatMarker");
						if (m != null) {
							Log.Write("Method Imperium.ChatMarker found, hook enabled");
							ChatColorForeignModMethods.Add(m);
						}
					}
				}
			}
		}


		public static void OnPlayerConnectedLate(Players.Player player)
	  	{
			if (player == null) {
				return;
			}

			ThreadManager.InvokeOnMainThread(delegate {
				SendJoinProtectionNotice(player);
			}, JOIN_MESSAGE_DELAY_SECONDS);

			if (config == null) {
				return;
			}

			long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000;
			if (Pipliz.Time.SecondsSinceStartInt + config.StartupGracePeriod < now) {
				return;
			}
			if (PermissionsManager.HasPermission(player, "antigrief.graceperiod")) {
				return;
			}
			Chat.Send(player, $"Server not yet ready. Please try again in {config.StartupGracePeriod/60} minutes");
			Players.Disconnect(player);
		}


		private static void SendJoinProtectionNotice(Players.Player player)
		{
			if (player == null || player.ConnectionState != Players.EConnectionState.Connected || !player.IsConnectionReady) {
				return;
			}

			if (!ShouldAnnounceProtection()) {
				return;
			}

			Chat.Send(player, "<color=yellow>Anti-Grief protection enabled</color>");

			if (config != null && config.ShowJoinPopup) {
				SendJoinProtectionPopup(player);
			}
		}


		private static bool ShouldAnnounceProtection()
		{
			if (config == null) {
				return false;
			}

			return config.SpawnProtectionRange >= 0 ||
				config.BannerProtectionRange >= 0 ||
				(config.CustomAreas != null && config.CustomAreas.Count > 0) ||
				config.NpcKillsJailThreshold > 0 ||
				config.NpcKillsKickThreshold > 0 ||
				config.NpcKillsBanThreshold > 0;
		}


		private static void SendJoinProtectionPopup(Players.Player player)
		{
			NetworkMenu popup = new NetworkMenu
			{
				TextColor = Color.black,
				Width = JOIN_POPUP_WIDTH,
				Height = JOIN_POPUP_HEIGHT,
				ForceClosePopups = false
			};

			popup.LocalStorage["header"] = "Anti-Grief";
			popup.Items.Add(new EmptySpace(10));
			popup.Items.Add(new Label(new LabelData("Anti-Grief protection is enabled on this world.", ELabelAlignment.MiddleCenter, 16, LabelData.ELocalizationType.None), 36));
			popup.Items.Add(new Label(new LabelData("Spawn and colony protection are active. Open the config or README for details.", ELabelAlignment.MiddleCenter, 14, LabelData.ELocalizationType.None), 54));
			popup.Items.Add(new EmptySpace(8));
			popup.Items.Add(new HorizontalRow(new List<(IItem, int)>
			{
				(new EmptySpace(1), 132),
				(new ButtonCallback("antigrief.notice.ok", new LabelData("OK", ELabelAlignment.MiddleCenter, 16, LabelData.ELocalizationType.None), 120, 30, ButtonCallback.EOnClickActions.ClosePopup, null), 120),
				(new EmptySpace(1), 132)
			}, 30));

			NetworkMenuManager.SendServerPopup(player, popup);
		}


		// TODO ?if required at all
		// check for empty colony leftovers and remove them
		public static void StartupEmptyColonyCheck()
		{
/*			int ic = 0;
			foreach (Colony colony in ServerManager.ColonyTracker.ColoniesByID.Values) {
				ic++;
				if (colony.ColonyGroup.Owners.Length == 0 && colony.Banners.Length > 0) {
					Log.Write($"Empty colony check: #{colony.ColonyID} {colony.Name} has no owners - purging it");
					while (colony.Banners.Length > 1) {
						ServerManager.ClientCommands.DeleteBannerTo(null, colony, colony.Banners[0].Position);
					}
					ServerManager.ClientCommands.DeleteColonyAndBanner(null, colony, colony.Banners[0].Position);
				}
			}
			Log.Write($"Empty colony check: finished checking {ic} colonies.");
*/		}

	} // class

} // namespace
