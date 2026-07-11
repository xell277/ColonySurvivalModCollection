using System.Text.RegularExpressions;
using System.Collections.Generic;
using Pipliz;
using Chatting;
using Chatting.Commands;
using BlockEntities.Implementations;

namespace ColonyCommands
{

	public class WarpBannerChatCommand : IChatCommand
	{

		public bool TryDoCommand (Players.Player causedBy, string chattext, List<string> splits)
		{
			if (splits.Count == 0 || !splits[0].Equals("/warpbanner")) {
				return false;
			}

			Colony targetColony = null;
			Match m = Regex.Match(chattext, @"/warpbanner (?<target>.+)");
			if (m.Success) {
				string error;
				if (!PlayerHelper.TryGetColony(m.Groups["target"].Value, out targetColony, out error)) {
					Chat.Send(causedBy, $"Could not find target: {error}");
					return true;
				}
			} else {
				int minDistance = int.MaxValue;
				for (int i = 0; i < causedBy.ColonyGroups.Count; i++) {
					BannerTracker.Banner found;
					int closestDistance = causedBy.ColonyGroups[i].MainColony.Banners.GetClosestDistance(causedBy.PositionVoxelStanding, out found);
					if (closestDistance < minDistance) {
						targetColony = causedBy.ColonyGroups[i].MainColony;
						minDistance = closestDistance;
					}
				}
				if (targetColony == null) {
					Chat.Send(causedBy, $"Could not find any banner to warp to");
					return true;
				}
			}

			string permission = AntiGrief.MOD_PREFIX + "warp.banner";
			if (targetColony != null) {
				if (targetColony.ColonyGroup.Owners.ContainsByReference(causedBy)) {
					permission += ".self";
				}
				if (!PermissionsManager.CheckAndWarnPermission(causedBy, permission)) {
					return true;
				}
				Helper.TeleportPlayer(causedBy, targetColony.Banners[0].Position.Vector);
			}

			return true;
		}
	}
}
