using Pipliz;
using Chatting;
using Chatting.Commands;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ColonyCommands
{
	public class HelpCommand: IChatCommand
	{

		private static Dictionary<string, string> commandPermissionList = new Dictionary<string, string>() {
			{"/announcements",	"mods.scarabol.commands.announcements.list"},
			{"/antigrief",		""},
			// {"areashow", "DISABLED"},
			{"/ban",			"mods.scarabol.commands.ban"},
			{"/banlog",			"mods.scarabol.commands.ban"},
			{"/bannername",		""},
			{"/beds",			"mods.scarabol.commands.beds"},
			{"/colonycap",		""},
			{"/colortest",		"mods.scarabol.commands.mute"},   // hide it for normal users, permission randomly choosen
			{"/customarea",		""},
			{"/drain",			"mods.scarabol.commands.drain"},
			{"/god",			"mods.scarabol.commands.god"},
			{"/graceperiod",	"mods.scarabol.commands.antigrief.setgraceperiod"},
			{"/inactive",		"mods.scarabol.commands.inactive"},
			{"/itemid",			""},
			{"/jail",			"mods.scarabol.commands.jail"},
			{"/jailleave",		""},
			{"/leavejail",		""},
			{"/jailrec",		"mods.scarabol.commands.jail"},
			{"/jailrelease",	"mods.scarabol.commands.jail"},
			{"/jailtime",		""},
			{"/jailvisit",		""},
			{"/visitjail",		""},
			{"/kick",			"mods.scarabol.commands.kick"},
			{"/killnpcs",		"mods.scarabol.commands.killnpcs.self"},
			{"/killplayer",		"mods.scarabol.commands.killplayer.self"},
			{"/lastseen",		""},
			{"/list",			"mods.scarabol.commands.listplayer"},
			{"/mtop",			""},
			{"/noflight",		"mods.scarabol.commands.noflight"},
			{"/online",			""},
			{"/promote",		"permissions.setgroup"},
			{"/purgebanner",	"mods.scarabol.commands.purgebanner"},
			{"/rpban",			"mods.scarabol.commands.roleplayban"},
			{"/rpunban",		"mods.scarabol.commands.roleplayban"},
			{"/rp",				""},
			{"/serverpop",		""},
			{"/setjail",		"mods.scarabol.commands.setjailposition"},
			{"/mute",			"mods.scarabol.commands.mute"},
			{"/unmute",			"mods.scarabol.commands.mute"},
			{"/spawnnpc",		"mods.scarabol.commands.npcandbeds"},
			{"/stuck",			""},
			{"/top",			""},
			{"/total",			""},
			{"/trade",			"mods.scarabol.commands.trade"},
			{"/trash",			""}, {"travel",			"mods.scarabol.commands.travelpaths"},
			{"/war",			""},
			{"/warpbanner",		"mods.scarabol.commands.warp.banner"},
			{"/warp",			"mods.scarabol.commands.warp.self"},
			{"/warpplace",		"mods.scarabol.commands.warp.place"},
			{"/warpspawn",		"mods.scarabol.commands.warp.spawn.self"},
			{"/spawn",			""},
			{"/w",				""},
			{"/whisper",		""}
		};


		public bool TryDoCommand(Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/help")) {
				return false;
			}

			if (splits.Count == 2 && (splits[1].Equals("help") || splits[1].Equals("?"))) {
				Chat.Send(causedBy, "Type /help warp|jail|rp... to filter");
				return true;
			}

			List<string> selectedCommands = new List<string>();
			foreach (KeyValuePair<string, string> kvp in commandPermissionList) {
				if (kvp.Value.Equals("")) {
					selectedCommands.Add(kvp.Key);
				} else {
					if (PermissionsManager.HasPermission(causedBy, kvp.Value)) {
						selectedCommands.Add(kvp.Key);
					}
				}
			}

			if (splits.Count == 2) {
				List<string> filtered = selectedCommands.FindAll(w => w.Contains(splits[1]));
				selectedCommands = filtered;
			}

			selectedCommands.Sort(StringComparer.InvariantCulture);
			string result = string.Join(", ", selectedCommands);

			Chat.Send(causedBy, $"Commands: {result}");
			return true;
		}
	}
}
