using System.Collections.Generic;
using Chatting;

namespace BetterAC130.Server;

[ChatCommandAutoLoader]
public sealed class Ac130ResetCommand : IChatCommand
{
	public bool TryDoCommand(Players.Player player, string chat, List<string> splits)
	{
		if (splits == null || splits.Count == 0)
		{
			return false;
		}
		string text = splits[0];
		bool flag = text == "/ac130reset" || text == "/betterac130reset";
		int num;
		switch (text)
		{
		default:
			num = ((text == "/betterac130terraindamage") ? 1 : 0);
			break;
		case "/ac130terrain":
		case "/betterac130terrain":
		case "/ac130terraindamage":
			num = 1;
			break;
		}
		bool flag2 = (byte)num != 0;
		bool flag3 = text == "/ac130terrainoff" || text == "/betterac130terrainoff";
		bool flag4 = text == "/ac130terrainon" || text == "/betterac130terrainon";
		int num3;
		switch (text)
		{
		default:
			num3 = ((text == "/betterac130flir") ? 1 : 0);
			break;
		case "/ac130flir":
		case "/betterac130overlay":
		case "/ac130overlay":
			num3 = 1;
			break;
		}
		bool flag5 = (byte)num3 != 0;
		bool flag6 = text == "/ac130fliron" || text == "/betterac130fliron";
		bool flag7 = text == "/ac130fliroff" || text == "/betterac130fliroff";
		int num2;
		switch (text)
		{
		default:
			num2 = ((text == "/betterac130giveuplink") ? 1 : 0);
			break;
		case "/ac130uplink":
		case "/betterac130uplink":
		case "/ac130giveuplink":
			num2 = 1;
			break;
		}
		bool flag8 = (byte)num2 != 0;
		if (!flag && !flag2 && !flag3 && !flag4 && !flag5 && !flag6 && !flag7 && !flag8)
		{
			return false;
		}
		Ac130ServerEntry instance = Ac130ServerEntry.Instance;
		if (instance == null)
		{
			Chat.Send(player, "BetterAC130 runtime is not ready yet.");
			return true;
		}
		bool flag9;
		string message;
		if (flag)
		{
			flag9 = instance.TryResetCooldownForPlayer(player, out message);
		}
		else if (flag3)
		{
			flag9 = instance.TrySetTerrainDamageEnabled(enabled: false, out message);
		}
		else if (flag4)
		{
			flag9 = instance.TrySetTerrainDamageEnabled(enabled: true, out message);
		}
		else if (flag6)
		{
			flag9 = instance.TrySetFlirEnabled(player, enabled: true, out message);
		}
		else if (flag7)
		{
			flag9 = instance.TrySetFlirEnabled(player, enabled: false, out message);
		}
		else if (flag2)
		{
			if (splits.Count < 2)
			{
				Chat.Send(player, "Usage: /ac130terrain <on|off|toggle>");
				return true;
			}
			switch (splits[1].ToLowerInvariant())
			{
			case "on":
				flag9 = instance.TrySetTerrainDamageEnabled(enabled: true, out message);
				break;
			case "off":
				flag9 = instance.TrySetTerrainDamageEnabled(enabled: false, out message);
				break;
			case "toggle":
				flag9 = instance.TryToggleTerrainDamage(out message);
				break;
			default:
				Chat.Send(player, "Usage: /ac130terrain <on|off|toggle>");
				return true;
			}
		}
		else if (flag5)
		{
			if (splits.Count < 2)
			{
				Chat.Send(player, "Usage: /ac130flir <on|off|toggle>");
				return true;
			}
			switch (splits[1].ToLowerInvariant())
			{
			case "on":
				flag9 = instance.TrySetFlirEnabled(player, enabled: true, out message);
				break;
			case "off":
				flag9 = instance.TrySetFlirEnabled(player, enabled: false, out message);
				break;
			case "toggle":
				flag9 = instance.TryToggleFlir(player, out message);
				break;
			default:
				Chat.Send(player, "Usage: /ac130flir <on|off|toggle>");
				return true;
			}
		}
		else
		{
			flag9 = instance.TryGiveUplinkBlock(player, out message);
		}
		if (flag9)
		{
			Chat.Send(player, message);
		}
		else
		{
			Chat.Send(player, message ?? "AC130 command failed.");
		}
		return true;
	}
}
