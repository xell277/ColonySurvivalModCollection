using System.Collections.Generic;

namespace ColonyCommands {

	public class ModConfig {
		public int SpawnProtectionRange = 200;
		public int BannerProtectionRange = 100;
		public bool ShowJoinPopup = true;
		public bool ProtectionIncludesHeightCheck = true;
		public int ColonistLimit = 0;
		public int ColonistLimitCheckSeconds = 30;
		public int ColonistLimitMaxKillPerIteration = 500;
		public List<int> ColonistTierLimits = new List<int>();
		public List<List<int>> ColonistPerColonyTierLimits = new List<List<int>>();
		public float ColonyColonistLimitTierCheckSeconds;
		public int ColonyColonistLimitTierWarnTimes;
		public Dictionary<Colony, int> ColonyColonistLimitTiers = new Dictionary<Colony, int>();
		public int OnlineBackupIntervalHours;
		public List<CustomProtectionArea> CustomAreas = new List<CustomProtectionArea>();
		public int NpcKillsJailThreshold;
		public int NpcKillsKickThreshold;
		public int NpcKillsBanThreshold;
		public bool EnableWarpCommand;
		public int WarDuration = 2 * 60 * 60; // 2 hours
		public int StartupGracePeriod = 0;
	}

}
