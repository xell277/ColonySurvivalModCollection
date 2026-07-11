using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Pipliz;
using Newtonsoft.Json;
using Chatting;
using UnityEngine;
using Shared.Networking;
using Steamworks;

namespace ColonyCommands {

	public static class JailManager
	{
		public static Dictionary<ulong, List<JailLogRecord>> jailLog = new Dictionary<ulong, List<JailLogRecord>>();
		public static Dictionary<Players.Player, Vector3> visitorPreviousPos = new Dictionary<Players.Player, Vector3>();
		const string CONFIG_FILE = "jail-config.json";
		const string LOG_FILE = "jail-log.json";
		public const int DEFAULT_RANGE = 5;
		public static bool validJail = false;
		public static bool validVisitorPos = false;
		public static JailConfig jailConfig = new JailConfig();
		public static Vector3 jailPosition;
		public static Vector3 jailVisitorPosition;
		public static Pipliz.BoundsInt prisonBox;
		public static Dictionary<Players.Player, JailRecord> jailedPersons = new Dictionary<Players.Player, JailRecord>();

		// Jail Config
		public class JailConfig {
			public int jailx, jaily, jailz;
			public int jailvx, jailvy, jailvz;
			public int rangex, rangey, rangez;
			public int boxRange;
			public int defaultJailTime;
			public int graceEscapeAttempts;
			public string prisonerGroup;
			public bool restoreGroupsOnRelease;
			public string defaultGroup;
			public Dictionary<ulong, JailRecord> jailedRecords;
		}

		// Jail record per player
		public class JailRecord {
			public int gracePeriod;
			public int escapeAttempts;
			public long jailTimestamp;
			public long jailDuration;
			public ulong jailedBy;
			public string jailReason;
			public List<string> groups;

			public JailRecord()
			{
			}

			public JailRecord(long time, long duration, Players.Player causedBy, string reason, List<string> groups)
			{
				this.gracePeriod = 2;
				this.escapeAttempts = 0;
				this.jailTimestamp = time;
				this.jailDuration = duration;
				this.jailedBy = (causedBy != null) ? causedBy.ID.SteamID.m_SteamID : 0;
				this.jailReason = reason;
				this.groups = groups;
			}
		}

		// log file record
		public class JailLogRecord {
			public long timestamp;
			public long duration;
			public ulong jailedBy;
			public string reason;

			public JailLogRecord()
			{
			}

			public JailLogRecord(long time, long duration, Players.Player causedBy, string reason)
			{
				this.timestamp = time;
				this.duration = duration;
				this.jailedBy = (causedBy != null) ? causedBy.ID.SteamID.m_SteamID : 0;
				this.reason = reason;
			}
		}

		static string ConfigFilePath {
			get {
				return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, CONFIG_FILE));
			}
		}

		static string LogFilePath {
			get {
				return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, LOG_FILE));
			}
		}


		// send a player to jail
		public static void jailPlayer(Players.Player target, Players.Player causedBy, string reason, long jailtime)
		{
			if (!validJail) {
				if (causedBy == null) {
					Log.Write($"Cannot Jail {target.Name}: no valid jail found");
				} else {
					Chat.Send(causedBy, "<color=yellow>No valid jail found. Unable to complete jailing</color>");
				}
				return;
			}
			if (jailtime < 1) jailtime = 1; // fail safe

			Helper.TeleportPlayer(target, jailPosition, true);

			// move to prisoner permissions group
			List<string> groups = new List<string>();
			foreach (KeyValuePair<string, PermissionsManager.PermissionsGroup> kvp in PermissionsManager.Groups) {
				if (kvp.Value == PermissionsManager.Users[target.ID.SteamID]) {
					Log.Write($"Player {target.Name} was group: {kvp.Key}");
					groups.Add(kvp.Key);
				}
			}
			if (groups.Count == 0) groups.Add(jailConfig.defaultGroup);
			PermissionsManager.SetGroupOfUser((Players.Player)null, target, jailConfig.prisonerGroup);

			// remove flight state
			if (target.HasFlightMode) {
				Log.Write($"Removing flight mode from {target.Name}");
				target.SetFlightMode(false);
			}

			// create/add history record
			long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000;
			JailLogRecord logRecord = new JailLogRecord(now, jailtime * 60, causedBy, reason);
			List<JailLogRecord> playerRecords;
			if (jailLog.TryGetValue(target.ID.SteamID.m_SteamID, out playerRecords)) {
				playerRecords.Add(logRecord);
			} else {
				playerRecords = new List<JailLogRecord>();
				playerRecords.Add(logRecord);
				jailLog.Add(target.ID.SteamID.m_SteamID, playerRecords);
			}
			SaveLogFile();

			// create jail record
			JailRecord record = new JailRecord(now, jailtime * 60, causedBy, reason, groups);
			jailedPersons.Add(target, record);
			Save();

			string sender;
			if (causedBy == null) {
				sender = "Server";
			} else {
				sender = causedBy.Name;
			}
			Chat.Send(target, $"<color=red>{sender} threw you into jail! Reason: {reason}</color>");
			Chat.Send(target, $"Remaining Jail Time: {jailtime} minutes, type /jailtime to check");
			Chat.SendToConnectedBut(target, $"<color=red>{sender} threw {target.Name} into jail! Reason: {reason}</color>");
			Log.Write($"{sender} threw {target.Name} into jail! Reason: {reason}");
			return;
		}

		// visit the jail - no harm is done
		public static void VisitJail(Players.Player causedBy)
		{
			if (validJail && validVisitorPos) {
				visitorPreviousPos[causedBy] = causedBy.PositionStanding;
				Helper.TeleportPlayer(causedBy, jailVisitorPosition);
				Chat.Send(causedBy, "Welcome Visitor! You're free to leave anytime, /jailleave will bring you back to your previous location");
			}
			return;
		}

		// update/set the jail position in the world
		public static void setJailPosition(Players.Player causedBy, int x, int y, int z)
		{
			// if an old jail position existed remove its protection area
			if (validJail) {
				Pipliz.Vector3Int oldPos = new Pipliz.Vector3Int(jailPosition);
				CustomProtectionArea oldJail = null;
				foreach (CustomProtectionArea area in AntiGrief.config.CustomAreas) {
					if (area.Contains(oldPos)) {
						oldJail = area;
					}
				}
				if (oldJail != null) {
					AntiGrief.RemoveCustomArea(oldJail);
					Chat.Send(causedBy, String.Format("Removed old jail protection area at {0} {1}", (int)jailPosition.x, (int)jailPosition.z));
				}
			}

			// center position
			jailPosition.x = causedBy.PositionStanding.x;
			jailPosition.y = causedBy.PositionStanding.y + 1;  // one block higher to prevent clipping
			jailPosition.z = causedBy.PositionStanding.z;

			Pipliz.Vector3Int intPos = new Pipliz.Vector3Int(causedBy.PositionStanding);
			Pipliz.Vector3Int min = new Pipliz.Vector3Int(intPos.x - x, intPos.y - y, intPos.z - z);
			Pipliz.Vector3Int max = new Pipliz.Vector3Int(intPos.x + x, intPos.y + y, intPos.z + z);
			prisonBox = new Pipliz.BoundsInt(min, max);
			AntiGrief.AddCustomArea(new CustomProtectionArea(intPos, x, z));

			validJail = true;
			Save();
			return;
		}


		// update/set the jail visitor position in the world
		public static void setJailVisitorPosition(Vector3 newPosition)
		{
			jailVisitorPosition.x = newPosition.x;
			jailVisitorPosition.y = newPosition.y + 1;
			jailVisitorPosition.z = newPosition.z;
			validVisitorPos = true;
			Save();
			return;
		}

		// load from config file
		public static void Load()
		{
			if (File.Exists(ConfigFilePath)) {
				try {
					Log.Write("Loading jail config from {0}", CONFIG_FILE);
					JsonSerializer js = new JsonSerializer();
					JsonTextReader jtr = new JsonTextReader(new StreamReader(ConfigFilePath));
					jailConfig = js.Deserialize<JailConfig>(jtr);
					jtr.Close();

					if (jailConfig.jailx != 0 && jailConfig.jaily != 0 & jailConfig.jailz != 0) {
						jailPosition = new Vector3(jailConfig.jailx, jailConfig.jaily, jailConfig.jailz);
						Log.Write("Found valid jail position in config: {0} {1} {2}", jailPosition.x, jailPosition.y, jailPosition.z);
						validJail = true;
					}
					if (jailConfig.jailvx != 0 && jailConfig.jailvy != 0 && jailConfig.jailvz != 0) {
						jailVisitorPosition = new Vector3(jailConfig.jailvx, jailConfig.jailvy, jailConfig.jailvz);
						Log.Write("Found valid jail visitor spot in config: {0} {1} {2}", jailVisitorPosition.x, jailVisitorPosition.y, jailVisitorPosition.z);
						validVisitorPos = true;
					}

					Pipliz.Vector3Int intPos = new Pipliz.Vector3Int(jailConfig.jailx, jailConfig.jaily, jailConfig.jailz);
					Pipliz.Vector3Int min, max;
					if (jailConfig.boxRange != 0) {
						min = intPos - jailConfig.boxRange;
						max = intPos + jailConfig.boxRange;
						prisonBox = new Pipliz.BoundsInt(min, max);
						Log.Write("Jail dimension is a box with range: {0}", jailConfig.boxRange);
						jailConfig.boxRange = 0;
					} else if (jailConfig.rangex != 0 && jailConfig.rangey != 0 && jailConfig.rangez != 0) {
						min = new Pipliz.Vector3Int(intPos.x - jailConfig.rangex, intPos.y - jailConfig.rangey, intPos.z - jailConfig.rangez);
						max = new Pipliz.Vector3Int(intPos.x + jailConfig.rangex, intPos.y + jailConfig.rangey, intPos.z + jailConfig.rangez);
						prisonBox = new Pipliz.BoundsInt(min, max);
						Log.Write("Jail dimension are: {0} {1} {2}", jailConfig.rangex, jailConfig.rangey, jailConfig.rangez);
					}

					if (jailConfig.jailedRecords != null) {
						foreach (KeyValuePair<ulong, JailRecord> kvp in jailConfig.jailedRecords) {
							CSteamID steamId = new CSteamID(kvp.Key);
							jailedPersons[Players.PlayerDatabaseBySteamID[steamId]] = kvp.Value;
						}
					}

				} catch (Exception e) {
					Log.Write("Error parsing {0}: {1}", CONFIG_FILE, e.Message);
				}

			} else {
				Log.Write("No {0} found inside world directory, using default config", CONFIG_FILE);
				jailConfig.defaultJailTime = 5;
				jailConfig.graceEscapeAttempts = 10;
				jailConfig.prisonerGroup = "prisoner";
				jailConfig.restoreGroupsOnRelease = true;
				jailConfig.defaultGroup = "peasant";
			}

			LoadLogFile();
			CheckAndReleasePlayers();

			// check if prisoner permission group exists or try to create it
			if (!PermissionsManager.Groups.ContainsKey(jailConfig.prisonerGroup)) {
				PermissionsManager.PermissionsGroup pGroup = new PermissionsManager.PermissionsGroup();
				PermissionsManager.RegisterGroup(jailConfig.prisonerGroup, pGroup);
				Log.Write($"Registered new permissions group for prisoners: {jailConfig.prisonerGroup}");
			}

			return;
		}

		// save to config file
		public static void Save()
		{
			if (!validJail) return;

			Log.Write("Saving jail config to {0}", CONFIG_FILE);
			jailConfig.jailx = (int)jailPosition.x;
			jailConfig.jaily = (int)jailPosition.y;
			jailConfig.jailz = (int)jailPosition.z;
			jailConfig.jailvx = (int)jailVisitorPosition.x;
			jailConfig.jailvy = (int)jailVisitorPosition.y;
			jailConfig.jailvz = (int)jailVisitorPosition.z;
			jailConfig.rangex = System.Math.Abs((int)(jailPosition.x - prisonBox.min.x));
			jailConfig.rangey = System.Math.Abs((int)(jailPosition.y - prisonBox.min.y));
			jailConfig.rangez = System.Math.Abs((int)(jailPosition.z - prisonBox.min.z));
			jailConfig.jailedRecords = new Dictionary<ulong, JailRecord>();
			foreach (KeyValuePair<Players.Player, JailRecord> kvp in jailedPersons) {
				jailConfig.jailedRecords[kvp.Key.ID.SteamID.m_SteamID] = kvp.Value;
			}
			try {
				JsonSerializer json = new JsonSerializer();
				JsonTextWriter jsonWriter = new JsonTextWriter(new StreamWriter(ConfigFilePath));
				json.Serialize(jsonWriter, jailConfig);
				jsonWriter.Close();
			} catch (Exception e) {
				Log.Write("Could not save {0}: {1}", CONFIG_FILE, e.Message);
			}

			return;
		}


		// check if jailed
		public static bool IsPlayerJailed(Players.Player player)
		{
			return jailedPersons.ContainsKey(player);
		}


		// release a player from jail
		public static void releasePlayer(Players.Player target, Players.Player causedBy)
		{
			JailRecord record;
			jailedPersons.TryGetValue(target, out record);
			jailedPersons.Remove(target);
			Save();

			if (jailConfig.restoreGroupsOnRelease == true && record.groups != null) {
				for (int i = 0; i < record.groups.Count; i++) {
					if (i == 0) {
						PermissionsManager.SetGroupOfUser((Players.Player)null, target, record.groups[i]);
					} else {
						PermissionsManager.AddGroupToUser((Players.Player)null, target, record.groups[i]);
					}
				}
			} else {
				PermissionsManager.SetGroupOfUser((Players.Player)null, target, jailConfig.defaultGroup);
			}

			if (target.ConnectionState == Players.EConnectionState.Connected) {
				Helper.TeleportPlayer(target, ServerManager.GetSpawnPoint().Position.Vector, true);
				Chat.Send(target, "<color=yellow>You did your time and are released from Jail</color>");
			}

			if (causedBy != null) {
				Log.Write($"{causedBy.Name} released {target.Name} from jail");
			} else {
				Log.Write($"Released {target.Name} from jail");
			}
			return;
		}


		// track jailed players movement
		public static void OnPlayerMoved(Players.Player causedBy, Vector3 pos)
		{
			if (!jailedPersons.ContainsKey(causedBy)) {
				return;
			}

			// each newly jailed player gets a grace period. This is mostly to avoid guard warnings
			// because OnPlayerMoved triggers too fast and can get the old position before the teleport to jail
			JailRecord record;
			jailedPersons.TryGetValue(causedBy, out record);
			if (record.gracePeriod > 0) {
				--record.gracePeriod;
				return;
			}

			if (!prisonBox.Contains(causedBy.PositionVoxelStanding)) {
				Helper.TeleportPlayer(causedBy, jailPosition, true);

				++record.escapeAttempts;
				if (jailConfig.graceEscapeAttempts == 0 || record.escapeAttempts < jailConfig.graceEscapeAttempts) {
					Chat.Send(causedBy, "<color=red>A Guard spots your escape attempt and pushes you back</color>");
				} else {
					record.jailDuration += 1 * 60;
					Chat.Send(causedBy, "<color=red>The Guards get angry at you and increase your jailtime by 1 minute</color>");
				}
			}
		}


		// check time and release Players from jail
		public static void CheckAndReleasePlayers()
		{
			long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000;
			List<Players.Player> toRelease = new List<Players.Player>();

			foreach (KeyValuePair<Players.Player, JailRecord> kvp in jailedPersons) {
				Players.Player target = kvp.Key;
				// ignore offline players
				if (target.ConnectionState != Players.EConnectionState.Connected) {
					continue;
				}
				JailRecord record = kvp.Value;
				if (record.jailTimestamp + record.jailDuration <= now) {
					toRelease.Add(target);
				}
			}
			foreach (Players.Player target in toRelease) {
				releasePlayer(target, null);
			}

			ThreadManager.InvokeOnMainThread(delegate() {
				CheckAndReleasePlayers();
			}, 5.350);

			return;
		}


		// when a jailed player reconnects recalculate jail time
		public static void OnPlayerConnectedLate(Players.Player player)
		{
			if (!IsPlayerJailed(player)) {
				return;
			}
			// set jail timestamp to now
			jailedPersons[player].jailTimestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000;
		}


		public static void OnPlayerDisconnected(Players.Player player)
		{
			if (!IsPlayerJailed(player)) {
				return;
			}
			JailRecord record = jailedPersons[player];
			long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000;
			long remaining = record.jailTimestamp + record.jailDuration - now;
			// set the remaining jail time as new duration (if the player reconnects)
			jailedPersons[player].jailDuration = remaining;
		}


		// calculate remaining jailtime
		public static long getRemainingTime(Players.Player causedBy)
		{
			JailRecord record = jailedPersons[causedBy];
			long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000;
			long remaining = record.jailTimestamp + record.jailDuration - now;

			return remaining;
		}


		// load log file (=past jail records)
		public static void LoadLogFile()
		{
			if (File.Exists(LogFilePath)) {
				try {
					Log.Write("Loading jail config from {0}", LOG_FILE);
					JsonSerializer js = new JsonSerializer();
					JsonTextReader jtr = new JsonTextReader(new StreamReader(LogFilePath));
					jailLog = js.Deserialize<Dictionary<ulong, List<JailLogRecord>>>(jtr);
					jtr.Close();
					Log.Write("Found {0} entries in the jail log", jailLog.Count);
				} catch (Exception e) {
					Log.Write("Error parsing {0}: {1}", LOG_FILE, e.Message);
				}
			}
			return;
		}


		// save log file (=past jail records)
		public static void SaveLogFile()
		{
			Log.Write("Saving jail history log to {0}", LOG_FILE);

			try {
				JsonSerializer json = new JsonSerializer();
				JsonTextWriter jsonWriter = new JsonTextWriter(new StreamWriter(LogFilePath));
				json.Serialize(jsonWriter, jailLog);
				jsonWriter.Close();
			} catch (Exception e) {
				Log.Write("Could not save {0}: {1}", LOG_FILE, e.Message);
			}

			return;
		}

	}
}
