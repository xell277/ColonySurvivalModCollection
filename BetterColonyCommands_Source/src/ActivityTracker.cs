using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Pipliz;
using Newtonsoft.Json;

namespace ColonyCommands
{
	public static class ActivityTracker
	{
		const string CONFIG_FILE = "playeractivity.json";
		static string ConfigFilePath {
			get {
				return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, CONFIG_FILE));
			}
		}
		static Dictionary<string, StatsDataEntry> PlayerStats = new Dictionary<string, StatsDataEntry>();


		public static void OnPlayerConnectedLate(Players.Player player)
		{
			var now = DateTime.Now.ToString();
			var stats = GetOrCreateStats(player.ID.ToStringReadable());
			stats.lastSeen = now;
		}


		public static void OnPlayerDisconnected(Players.Player player)
		{
			var now = DateTime.Now;
			var stats = GetOrCreateStats(player.ID.ToStringReadable());
			stats.secondsPlayed += (long)now.Subtract(DateTime.Parse(stats.lastSeen)).TotalSeconds;
			stats.lastSeen = now.ToString();
		}


		public static void OnAutoSaveWorld()
		{
			var now = DateTime.Now;
			for (var c = 0; c < Players.ConnectedPlayers.Count; c++) {
				Players.Player player = Players.ConnectedPlayers[c];
				var stats = GetOrCreateStats(player.ID.ToStringReadable());
				stats.secondsPlayed += (long)now.Subtract(DateTime.Parse(stats.lastSeen)).TotalSeconds;
				stats.lastSeen = now.ToString();
			}
			Save();
		}


		public static void OnQuit()
		{
			Save();
		}


		public static void Load()
		{
			if (File.Exists(ConfigFilePath)) {
				try {
					Log.Write("Loading player activity from {0}", CONFIG_FILE);
					JsonSerializer js = new JsonSerializer();
					JsonTextReader jtr = new JsonTextReader(new StreamReader(ConfigFilePath));
					PlayerStats = js.Deserialize<Dictionary<string, StatsDataEntry>>(jtr);
					jtr.Close();
					if (PlayerStats == null) {
						PlayerStats = new Dictionary<string, StatsDataEntry>();
					}
				} catch (Exception e) {
					Log.Write("Error parsing {0}: {1}", CONFIG_FILE, e.Message);
				}
			}
		}


		static void Save()
		{
			if (PlayerStats.Count == 0) return;

			Log.Write("Saving player activity to {0}", CONFIG_FILE);
			try {
				JsonSerializer json = new JsonSerializer();
				JsonTextWriter jsonWriter = new JsonTextWriter(new StreamWriter(ConfigFilePath));
				json.Serialize(jsonWriter, PlayerStats);
				jsonWriter.Close();
			} catch (Exception e) {
				Log.Write("Could not save {0}: {1}", CONFIG_FILE, e.Message);
			}
		}


		public static StatsDataEntry GetOrCreateStats(string playerId)
		{
			StatsDataEntry stats;
			if (!PlayerStats.TryGetValue(playerId, out stats)) {
				stats = new StatsDataEntry();
				PlayerStats.Add(playerId, stats);
			}
			return stats;
		}


		public static string GetLastSeen(string playerId)
		{
			StatsDataEntry stats;
			if (!PlayerStats.TryGetValue(playerId, out stats)) {
				return "never";
			}
			return stats.lastSeen;
		}


		public static Dictionary<Players.Player, int> GetInactivePlayers(int days)
		{
			Dictionary<Players.Player, int> result = new Dictionary<Players.Player, int>();
			foreach (Players.Player player in Players.PlayerDatabase.Values) {
				if (Players.IsServerID(player.ID)) {
					continue;
				}
				StatsDataEntry stats = GetOrCreateStats(player.ID.ToStringReadable());
				DateTime lastSeen = DateTime.Now;
				try {
					lastSeen = DateTime.Parse(stats.lastSeen);
				} catch (Exception exception) {
					Log.WriteError($"Unable to parse lastSeen '{stats.lastSeen}': {exception.Message}");
				}
				double inactiveDays = DateTime.Now.Subtract(lastSeen).TotalDays;
				if (inactiveDays >= days) {
					result.Add(player, (int)inactiveDays);
				}
			}
			return result;
		}


		public class StatsDataEntry
		{
			public string lastSeen = "";
			public long secondsPlayed;

			public StatsDataEntry()
				: this(DateTime.Now.ToString(), 0)
			{
			}

			public StatsDataEntry(String lastSeen, long secondsPlayed)
			{
				this.lastSeen = lastSeen;
				this.secondsPlayed = secondsPlayed;
			}

		}
	}
}
