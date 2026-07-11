using System.Collections.Generic;
using System.Linq;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class MTopChatCommand : IChatCommand
	{

		public enum EScoreType {
			Score,
			Food,
			Colonists,
			Item,
			TimePlayed
		}

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/mtop")) {
				return false;
			}
			if (splits.Count < 2) {
				Chat.Send(causedBy, "Syntax: /mtop {score|food|colonists|time|itemtypename}");
				return true;
			}
			string typename = splits[1];

			List<Players.Player> players = new List<Players.Player>();
			foreach (KeyValuePair<Players.PlayerIDShort, Players.Player> item in Players.PlayerDatabase) {
				// and also empty ones
				if (string.IsNullOrEmpty(item.Value.Name)) {
					continue;
				}
				// remove players that should be hidden from scoring (but never issuing player)
				if (PermissionsManager.HasPermissionExact(item.Value, AntiGrief.MOD_PREFIX + "hidefromtopcmd" )) {
					if (item.Value != causedBy) {
						continue;
					}
				}
				players.Add(item.Value);
			}

			Dictionary<Players.Player, long> results;
			EScoreType scoreType;
			if (typename.Equals("score")) {
				scoreType = EScoreType.Score;
				results = ScoreColonies(players, scoreType);

			} else if (typename.Equals("food")) {
				scoreType = EScoreType.Food;
				results = ScoreColonies(players, scoreType);

			} else if (typename.Equals("colonists")) {
				scoreType = EScoreType.Colonists;
				results = ScoreColonies(players, scoreType);

			} else if (typename.Equals("time")) {
				scoreType = EScoreType.TimePlayed;
				results = ScoreByTime(players);

			} else {
				scoreType = EScoreType.Item;
				ushort itemId;
				if (!ItemTypes.IndexLookup.TryGetIndex(typename, out itemId)) {
					Chat.Send(causedBy, $"There is no item called {typename}");
					return true;
				}
				results = ScoreColonies(players, EScoreType.Item, itemId);
			}

			// pretty string for output
			while (typename.Contains(".")) {
				typename = typename.Substring(typename.IndexOf(".") + 1);
			}
			typename = typename.Substring(0,1).ToUpper() + typename.Substring(1);

			List<KeyValuePair<Players.Player, long>> sortedResult = results.ToList();
			sortedResult.Sort(delegate(KeyValuePair<Players.Player, long> kvp1, KeyValuePair<Players.Player, long> kvp2) {
				return kvp2.Value.CompareTo(kvp1.Value);
			});

			Chat.Send(causedBy, $"##### Top {typename} #####");

			bool causedByIncluded = false;
			for (int i = 0; i < 10 && i < sortedResult.Count; i++) {
				if (sortedResult[i].Key == causedBy) {
					causedByIncluded = true;
				}
				string display_val;
				long val = sortedResult[i].Value;
				if (val > 9501300) {
					display_val = string.Format("{0:N0}m", val / 1000000);
				} else {
					display_val = string.Format("{0:N0}", val);
				}
				if (scoreType == EScoreType.TimePlayed) {
					display_val = $"{System.Math.Truncate(val / 3600f)}:{System.Math.Truncate(val % 3600f / 60f):00}:{val % 60f:00}";
				}
				Chat.Send(causedBy, $"{i + 1,2}: {display_val,10} {sortedResult[i].Key}");
			}

			// output player entry
			if (!causedByIncluded) {
				for (int i = 0; i < sortedResult.Count; i++) {
					if (sortedResult[i].Key != causedBy) {
						continue;
					}

					string display_val;
					long val = sortedResult[i].Value;
					if (val > 9501300) {
						display_val = string.Format("{0:N0}m", val / 1000000);
					} else {
						display_val = string.Format("{0:N0}", val);
					}
					if (scoreType == EScoreType.TimePlayed) {
						display_val = $"{System.Math.Truncate(val / 3600f)}:{System.Math.Truncate(val % 3600f / 60f):00}:{val % 60f:00}";
					}
					Chat.Send(causedBy, $".....\n{i + 1,2}: {display_val,10} {sortedResult[i].Key}");
					}
			}

			return true;
		}

		public Dictionary<Players.Player, long> ScoreColonies(List<Players.Player> players, EScoreType scoreType, ushort item = 0)
		{
			Dictionary<ColonyGroup, long> colonyresults = new Dictionary<ColonyGroup, long>();
			Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (colonyGroupEnum.MoveNext()) {
				ColonyGroup colony = colonyGroupEnum.Current;
				if (colony.Owners.Count == 0) {
					continue;
				}
				long score = 0;
				if (scoreType == EScoreType.Score) {
					score = colony.ColonyPoints;
				} else if (scoreType == EScoreType.Food) {
					score = (long)colony.Stockpile.TotalMeals;
				} else if (scoreType == EScoreType.Colonists) {
					score = colony.FollowerCountSum;
				} else if (scoreType == EScoreType.Item) {
					score = colony.Stockpile.AmountContained(item);
				}
				colonyresults[colony] = score;
			}

			Dictionary<Players.Player, long> results = new Dictionary<Players.Player, long>();
			foreach (ColonyGroup col in colonyresults.Keys) {
				Players.Player owner = col.Owners[0];
				if (!results.ContainsKey(owner)) {
					results[owner] = colonyresults[col];
				} else {
					results[owner] += colonyresults[col];
				}
			}

			return results;
		}

		public Dictionary<Players.Player, long> ScoreByTime(List<Players.Player> players)
		{
			Dictionary<Players.Player, long> results = new Dictionary<Players.Player, long>();
			foreach (Players.Player player in players) {
				results[player] = ActivityTracker.GetOrCreateStats(player.ID.ToStringReadable()).secondsPlayed;
			}
			return results;
		}

	}

}
