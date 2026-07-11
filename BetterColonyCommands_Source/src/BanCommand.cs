using System.Text.RegularExpressions;
using System.Collections.Generic;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class BanChatCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/ban")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "ban")) {
				return true;
			}
			var m = Regex.Match(chattext, @"/ban (?<target>['].+[']|[^ ]+) (?<bantime>[0-9]+) (?<reason>.+)");
			if (!m.Success) {
				Chat.Send(causedBy, "Syntax: /ban {playername} {days} {reason}");
				return true;
			}
			string targetPlayerName = m.Groups["target"].Value;
			string timeValue = m.Groups["bantime"].Value;
			string reason = m.Groups["reason"].Value;

			Players.Player targetPlayer;
			string error;
			if (!PlayerHelper.TryGetPlayer(targetPlayerName, out targetPlayer, out error)) {
				Chat.Send(causedBy, $"Could not find target player '{targetPlayerName}'; {error}");
				return true;
			}

			uint bantime = 0;
			if (!uint.TryParse(timeValue, out bantime)) {
				Chat.Send(causedBy, $"Could not identify number of days '{timeValue}': {error}");
				return true;
			}

			Chat.Send(targetPlayer, "<color=red>You were banned from the server</color>");
			Chat.SendToConnected($"{targetPlayer.Name} is banned by {causedBy.Name}");
			BannedPlayerManager.BanPlayer(targetPlayer, causedBy, bantime, reason);

			return true;
		}
	}
}
