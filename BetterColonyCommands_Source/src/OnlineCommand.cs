using System.Collections.Generic;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class OnlineChatCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/online")) {
				return false;
			}
			string msg = "";
			bool idMode = false;
			if (chattext.Equals("/online id")) {
				idMode = true;
			}

			foreach (Players.Player player in Players.ConnectedPlayers) {
				if (!msg.Equals("")) {
					msg += ", ";
				}
				msg += player.Name;
				if (idMode) {
					msg += ": #" + player.ID.SteamID.GetHashCode();
				}
			}
			msg += $"\nTotal {Players.ConnectedPlayers.Count} players online";

			Chat.Send (causedBy, msg);
			return true;
		}
	}
}
