using System;
using System.IO;
using System.Collections.Generic;
using Pipliz;
using Newtonsoft.Json;

namespace ColonyCommands {

	[ModLoader.ModManager]
	public static class TravelManager
	{ 
		const string CONFIG_FILE = "travelpaths.json";
		public static int DefaultWarpRange = 2;
		private static string ConfigFilePath {
			get {
				return Path.Combine(Path.Combine("gamedata", "savegames"), Path.Combine(ServerManager.WorldName, CONFIG_FILE));
			}
		}

		private static Dictionary<Players.Player, Vector3Int> warpedPlayers = new Dictionary<Players.Player, Vector3Int>();
		private static Dictionary<Vector3Int, Vector3Int> TravelPoints = new Dictionary<Vector3Int, Vector3Int>();

		public class TravelPoint
		{
			public int sx;
			public int sy;
			public int sz;
			public int tx;
			public int ty;
			public int tz;
		}

		// track players movement
		[ModLoader.ModCallback(ModLoader.EModCallbackType.OnPlayerMoved, AntiGrief.NAMESPACE + ".OnPlayerMoved")]
		public static void OnPlayerMoved(Players.Player causedBy, UnityEngine.Vector3 pos)
		{
			// avoid warping loop. Player needs to move outside the warp range first
			if (warpedPlayers.ContainsKey(causedBy)) {
				if (Distance(causedBy.PositionVoxelStanding, warpedPlayers[causedBy]) > DefaultWarpRange * 2 &&
					Distance(causedBy.PositionVoxelStanding, TravelPoints[warpedPlayers[causedBy]]) > DefaultWarpRange * 2) {
					warpedPlayers.Remove(causedBy);
				}
				return;
			}

			// check if at a travel point
			foreach (Vector3Int point in TravelPoints.Keys) {
				if (Distance(causedBy.PositionVoxelStanding, point) <= DefaultWarpRange) {
					warpedPlayers.Add(causedBy, point);
					Helper.TeleportPlayer(causedBy, TravelPoints[point].Vector);
					break;
				}
			}
		}

		// add a travel path
		public static bool addPath(Players.Player causedBy, Vector3Int source, Vector3Int target)
		{
			// check for duplicates
			foreach (Vector3Int point in TravelPoints.Keys) {
				if (Distance(point, source) < DefaultWarpRange * 2 ||
					Distance(point, target) < DefaultWarpRange * 2) {
					return false;
				}
			}

			// add two points, source->target and target->source
			Log.Write($"Adding travel path between {source} and {target}");
			TravelPoints.Add(source, target);
			TravelPoints.Add(target, source);
			Save();
			warpedPlayers.Add(causedBy, target);
			return true;
		}

		// remove a travel path
		public static bool removePath(Players.Player causedBy, Vector3Int pos)
		{
			Vector3Int source = new Vector3Int();
			bool found = false;
			foreach (Vector3Int point in TravelPoints.Keys) {
				if (Distance(point, pos) <= DefaultWarpRange * 2) {
					source = point;
					found = true;
					break;
				}
			}
			if (found) {
				Vector3Int target = TravelPoints[source];
				Log.Write($"Removing travel path between {source} and {target}");
				TravelPoints.Remove(target);
				TravelPoints.Remove(source);
				if (warpedPlayers.ContainsKey(causedBy)) {
					warpedPlayers.Remove(causedBy);
				}
				Save();
			}
			return found;
		}


		public static void Load()
		{
			if (File.Exists(ConfigFilePath)) {
				Log.Write("Loading travel points from {0}", CONFIG_FILE);
				try {
					JsonSerializer js = new JsonSerializer();
					JsonTextReader jtr = new JsonTextReader(new StreamReader(ConfigFilePath));
					foreach (TravelPoint point in js.Deserialize<List<TravelPoint>>(jtr)) {
						Vector3Int src = new Vector3Int(point.sx, point.sy, point.sz);
						Vector3Int tgt = new Vector3Int(point.tx, point.ty, point.tz);
						TravelPoints[src] = tgt;
					}
				} catch (Exception e) {
					Log.Write($"Could not parse {CONFIG_FILE}: {e.Message}");
				}
			}
		}


		// Save() is only run when a new travel point pair was added
		public static void Save()
		{
			Log.Write("Saving travel points to {0}", CONFIG_FILE);
			try {
				List<TravelPoint> savePoints = new List<TravelPoint>();
				foreach (KeyValuePair<Vector3Int, Vector3Int> kvp in TravelPoints) {
					TravelPoint point = new TravelPoint();
					point.sx = kvp.Key.x;
					point.sy = kvp.Key.y;
					point.sz = kvp.Key.z;
					point.tx = kvp.Value.x;
					point.ty = kvp.Value.y;
					point.tz = kvp.Value.z;
					savePoints.Add(point);
				}
				JsonSerializer json = new JsonSerializer();
				JsonTextWriter jsonWriter = new JsonTextWriter(new StreamWriter(ConfigFilePath));
				json.Serialize(jsonWriter, savePoints);
				jsonWriter.Flush();
			} catch (Exception e) {
				Log.Write($"Error saving {CONFIG_FILE}: {e.Message}");
			}
			return;
		}

		// calculate distance as int
		public static int Distance(Vector3Int a, Vector3Int b)
		{
			return (int)System.Math.Sqrt(System.Math.Pow(a.x - b.x, 2) + System.Math.Pow(a.y - b.y, 2) + System.Math.Pow(a.z - b.z, 2));
		}

	}	// class

} // namespace
