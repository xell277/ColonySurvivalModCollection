using System.Collections.Generic;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class ServerPopCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/serverpop")) {
				return false;
			}
			var allPlayers = Players.PlayerDatabase.Count;
			var onlinePlayers = Players.ConnectedPlayers.Count;
			var allFollower = 0;
			Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (colonyGroupEnum.MoveNext()) {
				allFollower += colonyGroupEnum.Current.FollowerCountSum;
			}
			var allMonsters = Monsters.MonsterTracker.MonstersTotal;
			var allUnits = allPlayers + allFollower + allMonsters;
			Chat.Send(causedBy, $"Server Population: {allUnits}, Players: {allPlayers}, Online: {onlinePlayers}, Colonists: {allFollower}, Monsters: {allMonsters}");
			return true;
		}
	}
}

