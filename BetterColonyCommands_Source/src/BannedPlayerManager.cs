using System;
using System.IO;
using System.Collections.Generic;
using Pipliz;
using Newtonsoft.Json;
using Steamworks;

namespace ColonyCommands {

	// static class to manage banned players
	public static class BannedPlayerManager
	{

		private const string CONFIG_FILE = "banned-players-log.json";
		private static string ConfigFilePath { get {
			return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, CONFIG_FILE)); }
		}
		public static List<BanLogRecord> banLogs = new List<BanLogRecord>();

		// BanPlayer
		public static void BanPlayer(Players.Player target, Players.Player causedBy, uint time, string reason)
		{
			BlackAndWhitelisting.AddBlackList(target.ID.SteamID.m_SteamID);
			BlackAndWhitelisting.Reload();
			Players.Disconnect(target);

			for (int i = 0; i < target.ColonyGroups.Count; ++i) {
				ColonyGroup colonyGroup = target.ColonyGroups.GetAt(i);
				Pipliz.Log.Write($"Purging colony {colonyGroup.Name} from banned player {target.Name}");
				if (colonyGroup.Owners.Count == 1) {
					for (int j = colonyGroup.Colonies.Count - 1; j >= 0; ++j) {
						Colony colony = colonyGroup.Colonies.GetAt(j);
						ServerManager.ClientCommands.DeleteColonyAndBanner(null, colony, colony.Banners[0].Position);
					}
				} else {
					colonyGroup.RemoveOwner(target);
				}
			}
			Log.Write($"{causedBy.Name} banned {target.Name}/{target.ID.SteamID.m_SteamID} for {time} days: {reason}");

			long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000;
			BanLogRecord record = new BanLogRecord(target, causedBy, now, time, reason);
			banLogs.Add(record);
			Save();

			return;
		}

		// UnbanPlayer
		public static void UnbanPlayer(BanLogRecord bannedPlayer)
		{
			bannedPlayer.unbanned = true;
			Players.Player target = Players.PlayerDatabaseBySteamID[new CSteamID(bannedPlayer.target)];
			Log.Write($"Unbanning {target.Name} after {bannedPlayer.duration} days");
			BlackAndWhitelisting.RemoveBlackList(bannedPlayer.target);
			Save();
		}


		// save banned records
		public static void Save()
		{
			if (banLogs.Count == 0) {
				if (File.Exists(ConfigFilePath)) {
					File.Delete(ConfigFilePath);
				}
				return;
			}

			Log.Write("Saving player ban logfile {0}", CONFIG_FILE);
			try {
				JsonSerializer json = new JsonSerializer();
				JsonTextWriter jsonWriter = new JsonTextWriter(new StreamWriter(ConfigFilePath));
				json.Serialize(jsonWriter, banLogs);
				jsonWriter.Close();
			} catch (Exception e) {
				Log.Write("Could not save {0}: {1}", CONFIG_FILE, e.Message);
			}
		}


		// load records
		public static void Load()
		{
			if (File.Exists(ConfigFilePath)) {
				try {
					Log.Write("Loading jail config from {0}", CONFIG_FILE);
					JsonSerializer js = new JsonSerializer();
					JsonTextReader jtr = new JsonTextReader(new StreamReader(ConfigFilePath));
					banLogs = js.Deserialize<List<BanLogRecord>>(jtr);
					jtr.Close();
				} catch (Exception e) {
					Log.Write("Error parsing {0}: {1}", CONFIG_FILE, e.Message);
				}
				CheckAndUnbanPlayers();
			}
		}


		// check time/duration and unban players
		public static void CheckAndUnbanPlayers()
		{
			long now = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 1000;
			foreach (BanLogRecord record in banLogs) {
				if (!record.unbanned && record.timestamp + (record.duration * 24 * 60 * 60) <= now) {
					UnbanPlayer(record);
				}
			}
		}


		// return nicely formatted times
		public static string prettyPrintDuration(long timevalue)
		{
			int days = (int)(timevalue / (60 * 60 * 24));
			int hours = (int)(timevalue % (60 * 60 * 24) / (60 * 60));
			int mins = (int)(timevalue / 60 % 60);
			int secs = (int)(timevalue % 60);
			if (days > 0) {
				return String.Format("{0}d {1:00}h:{2:00}m:{3:00}s", days, hours, mins, secs);
			} else if (hours > 0) {
				return String.Format("{0}h {1:00}m:{2:00}s", hours, mins, secs);
			}
			return String.Format("{0:00}m:{1:00}s", mins, secs);
		}

	} // BannedPlayerManager class


	// BanLogRecord
	public class BanLogRecord {
		public long timestamp;
		public uint duration;
		public ulong target;
		public ulong causedBy;
		public string reason;
		public bool unbanned;

		public BanLogRecord(Players.Player p, Players.Player c, long t, uint d, string r, bool u = false)
		{
			this.target = p.ID.SteamID.m_SteamID;
			this.causedBy = p.ID.SteamID.m_SteamID;
			this.timestamp = t;
			this.duration = d;
			this.reason = r;
			this.unbanned = u;
		}


	} // BanLogRecord

}

