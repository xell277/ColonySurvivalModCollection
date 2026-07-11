using System.Collections.Generic;
using Pipliz;
using Chatting;
using Chatting.Commands;
using BlockEntities.Implementations;

namespace ColonyCommands
{
	public class PurgeBannerCommand : IChatCommand
	{
		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/purgebanner")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "purgebanner")) {
				return true;
			}

			if (splits.Count == 3) {
				if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "purgeallbanner")) {
					return true;
				}
				// command: /purgebanner all <range> (Purge ALL colonies within range)
				if (splits[1].Equals("all")) {
					int range = 0;
					if (!int.TryParse(splits[2], out range)) {
						Chat.Send(causedBy, "Syntax: /purgebanner all <range>");
						return true;
					}
					int counter = PurgeAllColonies(causedBy, range);
					Chat.Send(causedBy, $"Purged {counter} colonies within range");
					return true;

				// command: /purgebanner days <minage> (Purge ALL colonies from inactive players)
				} else if (splits[1].Equals("days")) {
					int minage = int.MaxValue;
					if (!int.TryParse(splits[2], out minage)) {
						Chat.Send(causedBy, "Syntax: /purgebanner days <minage>");
						return true;
					}

					Dictionary<Players.Player, int> inactivePlayers = ActivityTracker.GetInactivePlayers(minage);
					int colonistCount = 0, counter = 0;
					Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
					while (colonyGroupEnum.MoveNext()) {
						ColonyGroup col = colonyGroupEnum.Current;
						if (col.Owners.Count == 0) {
							continue;
						}
						bool activeOwnerFound = false;
						for (int i = 0; i < col.Owners.Count; ++i) {
							if (!inactivePlayers.ContainsKey(col.Owners.GetAt(i))) {
								activeOwnerFound = true;
							}
						}
						if (!activeOwnerFound) {
							colonistCount += col.FollowerCountSum;
							counter++;
							PurgeColonyGroup(causedBy, col);
						}
					}
					Chat.Send(causedBy, $"Purged {counter} colonies with {colonistCount} colonists");
					return true;
				} else {
					Chat.Send(causedBy, "Syntax: /purgebanner {all|days} <range|age>");
					return true;
				}
			}

			// command: /purgebanner colony
			if (splits.Count == 2 && splits[1].Equals("colony")) {
				Colony colony = null;
				BannerTracker.Banner banner = null;
				int shortestDistance = int.MaxValue;
				Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
				while (colonyGroupEnum.MoveNext()) {
					ColonyGroup colGroup = colonyGroupEnum.Current;
					for (int i = 0; i < colGroup.Colonies.Count; ++i) {
						Colony checkColony = colGroup.Colonies.GetAt(i);
						for (int j = 0; j < checkColony.Banners.Count; ++j) {
							BannerTracker.Banner checkBanner = checkColony.Banners.GetAt(j);
							int distX = (int)(causedBy.PositionStanding.x - checkBanner.Position.x);
							int distZ = (int)(causedBy.PositionStanding.z - checkBanner.Position.z);
							int distance = (int)System.Math.Sqrt(System.Math.Pow(distX, 2) + System.Math.Pow(distZ, 2));
							if (distance < shortestDistance) {
								shortestDistance = distance;
								banner = checkBanner;
								colony = checkColony;
							}
						}
					}
				}

				if (banner == null) {
					Chat.Send(causedBy, "No banners found at all");
					return true;
				}
				if (shortestDistance > 100) {
					Chat.Send(causedBy, "Closest banner is more than 100 blocks away. Not doing anything");
					return true;
				}
				if (colony != null) {
					Chat.Send(causedBy, $"Purged colony/outpost {colony.Name}");
					PurgeColony(causedBy, colony);
					return true;
				}
			}

			// command: /purgebanner {playername}
			if (splits.Count == 2) {
				string targetName = splits[1];
				Players.Player target;
				string error;
				if (!PlayerHelper.TryGetPlayer(targetName, out target, out error, true)) {
					Chat.Send(causedBy, $"Could not find {targetName}: {error}");
					return true;
				}
				PurgePlayerColonies(causedBy, target);
				Chat.Send(causedBy, $"Freed {targetName} from all colonies");
				return true;
			}

			// only reached if nothing else matches
			Chat.Send(causedBy, "Syntax: /purgebanner { colony | [playername] | all [range] | days [age] }");
			return true;
		}


		// purge one colony / outpost
		public void PurgeColony(Players.Player causedBy, Colony colony)
		{
			Log.Write($"Purging colony/outpost {colony.Name}");
			for (int j = colony.Banners.Count - 1; j >= 0; --j) {
				if (j > 0) {
					ServerManager.ClientCommands.DeleteBannerTo(causedBy, colony, colony.Banners[j].Position);
				} else {
					ServerManager.ClientCommands.DeleteColonyAndBanner(causedBy, colony, colony.Banners[j].Position);
				}
			}
		}


		// purge a full colony group at once
		public void PurgeColonyGroup(Players.Player causedBy, ColonyGroup colonyGroup)
		{
			Log.Write($"Purging colony group {colonyGroup.Name}");
			for (int i = colonyGroup.Colonies.Count - 1; i >= 0; --i) {
				PurgeColony(causedBy, colonyGroup.Colonies.GetAt(i));
			}

			return;
		}


		// purge all colonies of a given player (or remove him/her in case of multiple owners)
		public void PurgePlayerColonies(Players.Player causedBy, Players.Player target)
		{
			for (int i = 0; i < target.ColonyGroups.Count; ++i) {
				ColonyGroup colGroup = target.ColonyGroups.GetAt(i);
				if (colGroup.Owners.Count == 1) {
					PurgeColonyGroup(causedBy, colGroup);
				} else {
					Log.Write($"Removing colony {colGroup.Name} from player {target.Name}");
					colGroup.RemoveOwner(target);
				}
			}

			return;
		}


		// purge all colonies within a given range
		public int PurgeAllColonies(Players.Player causedBy, int range)
		{
			List<Colony> colonies = new List<Colony>();
			Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (colonyGroupEnum.MoveNext()) {
				ColonyGroup colGroup = colonyGroupEnum.Current;
				for (int i = 0; i < colGroup.Colonies.Count; ++i) {
					Colony checkColony = colGroup.Colonies.GetAt(i);
					BannerTracker.Banner closestBanner = checkColony.GetClosestBanner(causedBy.PositionVoxelStanding);
					if (Pipliz.Math.ManhattanDistance(closestBanner.Position, causedBy.PositionVoxelStanding) <= range) {
						colonies.Add(checkColony);
					}
				}
			}

			// second loop for actual deletion
			int counter = 0;
			foreach (Colony colony in colonies) {
				while (colony.Banners.Count > 1) {
					ServerManager.ClientCommands.DeleteBannerTo(causedBy, colony, colony.Banners[colony.Banners.Count - 1].Position);
				}
				Chat.Send(causedBy, $"Purging {colony.Name}");
				ServerManager.ClientCommands.DeleteColonyAndBanner(causedBy, colony, colony.Banners[0].Position);
				counter++;
			}

			return counter;
		}

	}
}
