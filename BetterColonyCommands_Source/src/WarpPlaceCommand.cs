using System.Text.RegularExpressions;
using System.Collections.Generic;
using Pipliz;
using TerrainGeneration2;
using Chatting;
using Chatting.Commands;

namespace ColonyCommands
{

	public class WarpPlaceChatCommand : IChatCommand
	{

		public bool TryDoCommand (Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/warpplace")) {
				return false;
			}
			if (!PermissionsManager.CheckAndWarnPermission(causedBy, AntiGrief.MOD_PREFIX + "warp.place")) {
				return true;
			}
			var m = Regex.Match(chattext, @"/warpplace (?<px>-?\d+) (?<py>-?\d+)( (?<pz>-?\d+))?");
			if (!m.Success) {
				Chat.Send(causedBy, "Syntax: /warpplace [x] [y] [z] or /warpplace [x] [z]");
				return true;
			}
			float vx, vy, vz;
			var xCoord = m.Groups["px"].Value;
			var yCoord = m.Groups["py"].Value;
			var zCoord = m.Groups ["pz"].Value;
			if (!float.TryParse(xCoord, out vx)) {
				Chat.Send(causedBy, $"Failure parsing first coordinate '{xCoord}'");
				return true;
			}
			if (!float.TryParse(yCoord, out vy)) {
				Chat.Send (causedBy, $"Failure parsing second coordinate '{yCoord}'");
				return true;
			}
			if (zCoord.Length > 0) {
				if (!float.TryParse(zCoord, out vz)) {
					Chat.Send (causedBy, $"Failure parsing third coordinate '{zCoord}'");
					return true;
				}
			} else {
				vz = vy;
				Pipliz.Vector3Int position = new Pipliz.Vector3Int((int)vx, 0, (int)vz);
				TerrainGenerator2 gen = (TerrainGenerator2)ServerManager.TerrainGenerator;
				float unusedTemp, unusedMoisture;
				ushort unusedLeavesType, unusedLogType;
				gen.GetLocationData(position, out vy, out unusedTemp, out unusedMoisture, out unusedLeavesType, out unusedLogType);
				vy += 1;
			}

			Helper.TeleportPlayer(causedBy, new UnityEngine.Vector3(vx, vy, vz));
			return true;
		}
	}
}
