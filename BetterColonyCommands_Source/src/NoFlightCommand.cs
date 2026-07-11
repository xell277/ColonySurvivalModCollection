using System.Collections.Generic;
using Pipliz;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class NoFlightChatCommand : IChatCommand
	{

		public bool TryDoCommand (Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/noflight")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "noflight")) {
				return true;
			}

			foreach (Players.Player player in Players.PlayerDatabase.Values) {
				if (player.HasFlightMode) {
					if (!PermissionsManager.HasPermission(player, "setflight")) {
						player.SetFlightMode(false);
						if (player.ConnectionState == Players.EConnectionState.Connected) {
							Chat.Send(player, "Please don't fly");
						}
					}
				}
			}

			return true;
		}

	}
}

