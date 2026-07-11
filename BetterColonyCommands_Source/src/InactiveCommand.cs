using System;
using System.Collections.Generic;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class InactiveChatCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/inactive")) {
				return false;
				}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "inactive")) {
				return true;
			}
			int days;
			if (splits.Count == 2) {
				if (!int.TryParse(splits[1], out days)) {
					Chat.Send(causedBy, $"Could not parse days value");
					return true;
				}
			} else {
				Chat.Send(causedBy, "Syntax: /inactive {days}");
				return true;
			}

			Dictionary<Players.Player, int> inactivePlayers = ActivityTracker.GetInactivePlayers(days);
			List<ColonyGroup> inactiveColonyGroups = new List<ColonyGroup>();
			int colonistCount = 0;
			Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (colonyGroupEnum.MoveNext()) {
				ColonyGroup colGroup = colonyGroupEnum.Current;
				bool activeOwnerFound = false;
				if (colGroup.Owners.Count == 0) {
					continue;
				}
				for (int i = 0; i < colGroup.Owners.Count; ++i) {
					if (!inactivePlayers.ContainsKey(colGroup.Owners[i])) {
						activeOwnerFound = true;
					}
				}
				if (!activeOwnerFound) {
					inactiveColonyGroups.Add(colGroup);
					colonistCount += colGroup.FollowerCountSum;
				}
			}

			string msg = $"No players inactive longer than {days} days";
			if (inactivePlayers.Count > 0)  {
				msg = String.Format("{0} players inactive since {1} days. Would purge {2} colonies with {3} colonists", inactivePlayers.Count, days, inactiveColonyGroups.Count, colonistCount);
			};
			Chat.Send(causedBy, msg);
			return true;
		}
	}
}

