using Chatting;
using Chatting.Commands;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Steamworks;

namespace ColonyCommands
{

	public class JailRecCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/jailrec")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "jail")) {
				return true;
			}

			// get log by player
			var m = Regex.Match(chattext, @"/jailrec (?<target>.+)");
			if (m.Success) {
				string targetName = m.Groups["target"].Value;
				Players.Player target;
				string error;
				if (!PlayerHelper.TryGetPlayer(targetName, out target, out error)) {
					Chat.Send(causedBy, $"Could not find {targetName}: {error}");
					return true;
				}
				List<JailManager.JailLogRecord> PlayerJailLog;
				if (!JailManager.jailLog.TryGetValue(target.ID.SteamID.m_SteamID, out PlayerJailLog)) {
					Chat.Send(causedBy, $"No records found - {targetName} is clean");
					return true;
				}
				foreach (JailManager.JailLogRecord record in PlayerJailLog) {
					DateTime timestamp = new DateTime(record.timestamp * TimeSpan.TicksPerMillisecond * 1000);
					string jailedBy = "Server";
					if (record.jailedBy > 0) {
						CSteamID jailedBySteamID = new CSteamID(record.jailedBy);
						jailedBy = Players.PlayerDatabaseBySteamID[jailedBySteamID].Name;
					}
					Chat.Send(causedBy, String.Format("{0} by: {1} for: {2}", timestamp.ToString(), jailedBy, record.reason));
				}

			// or get the full log
			} else {
				// in full log mode only timestamp and playername are displayed (last 10 jailed).
				// showing more would require a more capable chat window
				List<combinedJailLog> combinedLog = new List<combinedJailLog>();
				foreach (KeyValuePair<ulong, List<JailManager.JailLogRecord>> kvp in JailManager.jailLog) {
					Players.Player target = Players.PlayerDatabaseBySteamID[new CSteamID(kvp.Key)];
					List<JailManager.JailLogRecord> records = kvp.Value;
					foreach (JailManager.JailLogRecord record in records) {
						Players.Player jailedBy = null;
						if (record.jailedBy > 0) {
							jailedBy = Players.PlayerDatabaseBySteamID[new CSteamID(record.jailedBy)];
						}
						combinedLog.Add(new combinedJailLog(target, jailedBy, record.timestamp));
					}
				}
				combinedLog.Sort(delegate(combinedJailLog a, combinedJailLog b) {
					return b.timestamp.CompareTo(a.timestamp);
				});

				int limit = 10;
				if (combinedLog.Count < limit) {
					limit = combinedLog.Count;
				}

				Chat.Send(causedBy, "Jail Records (last 10):");
				for (int i = 0; i < limit; ++i) {
					combinedJailLog record = combinedLog[i];
					DateTime timestamp = new DateTime(record.timestamp * TimeSpan.TicksPerMillisecond * 1000);
					string targetName = record.target.Name;
					string jailerName = "Server";
					if (record.causedBy != null) {
						jailerName = record.causedBy.Name;
					}
					Chat.Send(causedBy, String.Format("{0} {1} by: {2}", timestamp.ToString(), targetName, jailerName));
				}
			}

			return true;
		}


		private class combinedJailLog
		{
			public Players.Player target { get; set; }
			public Players.Player causedBy { get; set; }
			public long timestamp { get; set; }

			public combinedJailLog(Players.Player tgt, Players.Player src, long time)
			{
				target = tgt;
				causedBy = src;
				timestamp = time;
			}
		}

	}
}

