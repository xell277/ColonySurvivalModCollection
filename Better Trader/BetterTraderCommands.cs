using System.Collections.Generic;
using Chatting;

namespace BetterTrader
{
    [ChatCommandAutoLoader]
    public sealed class BetterTraderCommands : IChatCommand
    {
        public bool TryDoCommand(Players.Player player, string chatItem, List<string> splits)
        {
            if (splits == null || splits.Count < 1)
            {
                return false;
            }

            string command = splits[0].ToLowerInvariant();
            if (command != "/bettertrader" && command != "/bt")
            {
                return false;
            }

            if (!BetterTraderEntry.TryOpenTraderPopup(player))
            {
                Chat.Send(player, "Better Trader could not open.");
            }

            return true;
        }
    }
}
