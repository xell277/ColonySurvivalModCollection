using System;
using System.Collections.Generic;
using Chatting;

namespace BetterWater
{
    [ChatCommandAutoLoader]
    public sealed class BetterWaterCommands : IChatCommand
    {
        public bool TryDoCommand(Players.Player player, string chatItem, List<string> splits)
        {
            if (splits == null || splits.Count < 1)
            {
                return false;
            }

            string command = splits[0].ToLowerInvariant();
            if (command != "/betterwater" && command != "/bwater" && command != "/bwtr")
            {
                return false;
            }

            if (splits.Count == 1)
            {
                Send(player, "Use /betterwater status|on|off|vanilla|sound|markers|reload|cleanup");
                return true;
            }

            string action = splits[1].ToLowerInvariant();
            switch (action)
            {
                case "status":
                    Send(player, BetterWaterModEntry.GetStatusText());
                    return true;
                case "on":
                    if (!CanManage(player))
                    {
                        return true;
                    }

                    BetterWaterModEntry.SetBetterWaterEnabled(true);
                    Send(player, "BetterWater enabled. Vanilla water spread is off.");
                    return true;
                case "off":
                    if (!CanManage(player))
                    {
                        return true;
                    }

                    BetterWaterModEntry.SetBetterWaterEnabled(false);
                    Send(player, "BetterWater disabled. Vanilla water spread is on.");
                    return true;
                case "vanilla":
                    return HandleVanilla(player, splits);
                case "sound":
                case "sounds":
                case "audio":
                    return HandleSound(player, splits);
                case "marker":
                case "markers":
                case "sources":
                    return HandleMarkers(player, splits);
                case "reload":
                    if (!CanManage(player))
                    {
                        return true;
                    }

                    Send(player, BetterWaterModEntry.ReloadConfig()
                        ? "BetterWater config reloaded."
                        : "BetterWater config reload failed. Check betterwater.log.");
                    return true;
                case "cleanup":
                    if (!CanManage(player))
                    {
                        return true;
                    }

                    BetterWaterModEntry.CleanupManagedFlows();
                    Send(player, "BetterWater managed flow blocks cleaned up; sources were kept.");
                    return true;
                default:
                    Send(player, "Unknown option. Use /betterwater status|on|off|vanilla|sound|markers|reload|cleanup");
                    return true;
            }
        }

        private static bool HandleVanilla(Players.Player player, List<string> splits)
        {
            if (splits.Count < 3 || splits[2].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Send(player, BetterWaterModEntry.GetStatusText());
                return true;
            }

            if (!CanManage(player))
            {
                return true;
            }

            string value = splits[2].ToLowerInvariant();
            if (value == "on" || value == "true" || value == "1")
            {
                BetterWaterModEntry.SetVanillaSpreadEnabled(true);
                Send(player, "Vanilla water spread enabled. BetterWater custom flow is paused.");
                return true;
            }

            if (value == "off" || value == "false" || value == "0")
            {
                BetterWaterModEntry.SetVanillaSpreadEnabled(false);
                Send(player, "Vanilla water spread disabled. BetterWater custom flow is active.");
                return true;
            }

            Send(player, "Use /betterwater vanilla on|off|status");
            return true;
        }

        private static bool HandleSound(Players.Player player, List<string> splits)
        {
            if (splits.Count < 3 || splits[2].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Send(player, BetterWaterModEntry.GetStatusText());
                return true;
            }

            if (!CanManage(player))
            {
                return true;
            }

            string value = splits[2].ToLowerInvariant();
            if (value == "on" || value == "true" || value == "1")
            {
                BetterWaterModEntry.SetWaterfallSoundsEnabled(true);
                Send(player, "BetterWater waterfall sounds enabled.");
                return true;
            }

            if (value == "off" || value == "false" || value == "0")
            {
                BetterWaterModEntry.SetWaterfallSoundsEnabled(false);
                Send(player, "BetterWater waterfall sounds disabled.");
                return true;
            }

            Send(player, "Use /betterwater sound on|off|status");
            return true;
        }

        private static bool HandleMarkers(Players.Player player, List<string> splits)
        {
            if (splits.Count < 3 || splits[2].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Send(player, BetterWaterModEntry.GetStatusText());
                return true;
            }

            if (!CanManage(player))
            {
                return true;
            }

            string value = splits[2].ToLowerInvariant();
            if (value == "on" || value == "true" || value == "1")
            {
                BetterWaterModEntry.SetSourceMarkersEnabled(true);
                Send(player, "BetterWater source markers enabled.");
                return true;
            }

            if (value == "off" || value == "false" || value == "0")
            {
                BetterWaterModEntry.SetSourceMarkersEnabled(false);
                Send(player, "BetterWater source markers disabled.");
                return true;
            }

            Send(player, "Use /betterwater markers on|off|status");
            return true;
        }

        private static bool CanManage(Players.Player player)
        {
            if (BetterWaterModEntry.CanManage(player))
            {
                return true;
            }

            Send(player, "You do not have permission to manage BetterWater.");
            return false;
        }

        private static void Send(Players.Player player, string message)
        {
            if (player != null)
            {
                Chat.Send(player, message);
            }
        }
    }
}
