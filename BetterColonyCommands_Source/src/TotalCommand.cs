using System.Collections.Generic;
using System.Linq;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class TotalChatCommand : IChatCommand
	{

		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].StartsWith("/tot")) {
				return false;
			}
			if (splits.Count < 2) {
				Chat.Send(causedBy, "Syntax: /total {itemname}");
				return true;
			}

			string typename = splits[1];
			ushort itemId;
			if (!ItemTypes.IndexLookup.TryGetIndex(typename, out itemId)) {
				Chat.Send(causedBy, $"There is no item called {typename}");
				return true;
			}

			int amount = 0;
			//foreach (Colony colony in ServerManager.ColonyTracker.ColoniesByID.Values) {
			Pipliz.Collections.Hashmap<ColonyGroupID, ColonyGroup>.ValueEnumerator colonyGroupEnum = ServerManager.ColonyTracker.ColonyGroupsByID.GetValueEnumerator();
			while (colonyGroupEnum.MoveNext()) {
				ColonyGroup colony = colonyGroupEnum.Current;
				if (colony.Owners.Count == 0) {
					continue;
				}
				amount += colony.Stockpile.AmountContained(itemId);
			}

			// pretty string for output
			while (typename.Contains(".")) {
				typename = typename.Substring(typename.IndexOf(".") + 1);
			}
			typename = typename.Substring(0,1).ToUpper() + typename.Substring(1);

			string display_value;
			if (amount > 9501300) {
				display_value = string.Format("{0:N0}m", amount / 1000000);
			} else {
				display_value = string.Format("{0:N0}", amount);
			}
			Chat.Send(causedBy, $"Total amount of {typename}: {display_value}");

			return true;
		}

	}

}
