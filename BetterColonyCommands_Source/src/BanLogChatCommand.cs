using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Chatting;
using Chatting.Commands;
using Steamworks;

namespace ColonyCommands
{

	public class BanLogChatCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/banlog")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "ban")) {
				return true;
			}

			if (BannedPlayerManager.banLogs.Count == 0) {
				Chat.Send(causedBy, "Banlog is empty");
			}

			foreach (BanLogRecord record in BannedPlayerManager.banLogs) {
				DateTime timestamp = new DateTime(record.timestamp * TimeSpan.TicksPerMillisecond * 1000);
				Players.Player target = Players.PlayerDatabaseBySteamID[new CSteamID(record.target)];
				Players.Player bannedBy = Players.PlayerDatabaseBySteamID[new CSteamID(record.causedBy)];
				Chat.Send(causedBy, String.Format("{0} {1} banned by: {2} for {3} days: {4}", timestamp.ToString(), target.Name, bannedBy.Name, record.duration, record.reason));
			}

			return true;
		}
	}
}
