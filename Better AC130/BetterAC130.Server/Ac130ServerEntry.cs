using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BetterAC130.Shared;
using BlockTypes;
using Chatting;
using Chatting.Commands;
using MeshedObjects;
using ModLoaderInterfaces;
using Monsters;
using NetworkUI;
using NetworkUI.Items;
using Newtonsoft.Json.Linq;
using Pipliz;
using Shared;
using Shared.Networking;
using TMPro;
using UnityEngine;
using Unity.Mathematics;
using colonyserver.Assets.UIGeneration;
using colonyshared.NetworkUI;
using colonyshared.NetworkUI.UIGeneration;

namespace BetterAC130.Server;

public sealed class Ac130ServerEntry : IAfterItemTypesDefined, IModCallbackConsumer, IOnConstructPointUpgradesMenu, IOnInventorySelectionChanged, IOnLoadingColonyGroup, IOnLoadingImages, IOnPlayerClicked, IOnPlayerDisconnected, IOnPlayerHit, IOnPlayerPushedNetworkUIButton, IOnSavingColonyGroup, IOnUpdate
{
	private static class HudLayout
	{
		public static readonly Pipliz.Vector3Int TopLeft = new Pipliz.Vector3Int(28, -24, 0);

		public static readonly Pipliz.Vector3Int AltValue = new Pipliz.Vector3Int(-98, -78, 0);

		public static readonly Pipliz.Vector3Int OrbitValue = new Pipliz.Vector3Int(144, -78, 0);

		public static readonly Pipliz.Vector3Int TimeValue = new Pipliz.Vector3Int(610, -78, 0);

		public static readonly Pipliz.Vector3Int Ammo25 = new Pipliz.Vector3Int(-394, 112, 0);

		public static readonly Pipliz.Vector3Int Ammo40 = new Pipliz.Vector3Int(-130, 112, 0);

		public static readonly Pipliz.Vector3Int Ammo105 = new Pipliz.Vector3Int(120, 112, 0);

		public static readonly Pipliz.Vector3Int RotationArrowBase = new Pipliz.Vector3Int(-12, -176, 0);

		public static readonly Pipliz.Vector3Int KillFeed = new Pipliz.Vector3Int(28, 180, 0);

		public static readonly Pipliz.Vector3Int OverlayTop = new Pipliz.Vector3Int(0, -105, 0);

		public static readonly Pipliz.Vector3Int OverlayBottom = new Pipliz.Vector3Int(0, 187, 0);

		public static readonly Pipliz.Vector3Int OverlayLeft = new Pipliz.Vector3Int(115, 0, 0);

		public static readonly Pipliz.Vector3Int OverlayRight = new Pipliz.Vector3Int(-115, 0, 0);
	}

	private sealed class ActiveSession
	{
		public const int ZoomLevelCount = 3;

		public Players.Player Player;

		public Players.PlayerIDShort PlayerID;

		public int ColonyGroupID;

		public double StartedAtSeconds;

		public double EndsAtSeconds;

		public double LastOrbitSampleAt;

		public double NextVehicleSendAt;

		public double Weapon25NextShotAt;

		public double Weapon40NextShotAt;

		public double Weapon105NextShotAt;

		public int Weapon25Ammo;

		public int Weapon40Ammo;

		public int Weapon105Ammo;

		public float OrbitAngleRadians;

		public float OrbitRadius;

		public float OrbitAltitude;

		public float BaseOrbitRadius;

		public float BaseOrbitAltitude;

		public int ZoomLevel = 1;

		public Vector3 OrbitCenter;

		public Vector3 CurrentVehiclePosition;

		public Quaternion CurrentVehicleRotation;

		public Pipliz.Vector3Int TerminalPosition;

		public MeshedVehicleDescription Vehicle;

		public InventoryItem[] OriginalInventory = new InventoryItem[8];

		public ushort SelectedType;

		public double NextHudUpdateAt;

		public double KillFeedUntilAt;

		public int LastKillCount;

		public ushort AutoFireType;

		public double Weapon25NextAudioAt;

		public double Weapon40NextAudioAt;

		public double Weapon105NextAudioAt;

		public AudioManager.AudioClipPlayingID BackgroundLoopID;

		public AudioManager.AudioClipPlayingID WeaponLoopID;

		public string WeaponLoopClipName;

		public bool LastPauseRequestState;

		public bool IsPaused;

		public double PauseStartedAt;
	}

	private struct MonsterSplashDamage : MonsterTracker.IMonsterForeach
	{
		public Vector3 Center;

		public float Radius;

		public float Damage;

		public Players.Player SourcePlayer;

		public int Kills;

		public void IterateMonster(IMonster monster)
		{
			if (monster == null || !monster.IsValid)
			{
				return;
			}
			float num = Vector3.Distance(monster.PositionToAimFor, Center);
			if (!(num > Radius))
			{
				float num2 = 1f - num / Radius;
				float damage = Damage * (0.35f + 0.65f * num2);
				Vector3 vector = (monster.PositionToAimFor - Center).normalized;
				if (vector == Vector3.zero)
				{
					vector = Vector3.up;
				}
				float currentHealth = monster.CurrentHealth;
				monster.OnHit(damage, vector * (2f + Radius * 0.5f), SourcePlayer, ModLoader.OnHitData.EHitSourceType.Explosive);
				if (currentHealth > 0f && (!monster.IsValid || monster.CurrentHealth <= 0f))
				{
					Kills++;
				}
			}
		}
	}

	private sealed class PendingStrike
	{
		public Players.Player SourcePlayer;

		public ActiveSession Session;

		public Ac130WeaponConfig Weapon;

		public Vector3 FireOrigin;

		public Vector3 ImpactPoint;

		public double FiredAtSeconds;

		public double ImpactAtSeconds;

		public float TrailProgressSent;

		public float TrailProgressStep;

		public int ImpactBurstsRemaining;

		public double NextImpactBurstAt;
	}

	private const string HudTopImage = "betterac130.hud.top";

	private const string HudBottomImage = "betterac130.hud.bottom";

	private const string HudLeftImage = "betterac130.hud.left";

	private const string HudRightImage = "betterac130.hud.right";

	private const string FlirFullscreenImage = "betterac130.flir.fullscreen";

	private const string DefaultVehicleMeshPath = "./meshes/ac130_invisible_vehicle.ply";

	private const string RuntimeStateFileName = "ac130runtime.json";

	private const int ExitHotbarSlot = 7;

	private static readonly float[] ZoomRadiusScales = new float[3] { 1.14f, 1f, 0.82f };

	private static readonly float[] ZoomAltitudeScales = new float[3] { 1.2f, 1f, 0.74f };

	private readonly Dictionary<int, Ac130UpgradeState> colonyStateByGroup = new Dictionary<int, Ac130UpgradeState>();

	private readonly Dictionary<Players.PlayerIDShort, ActiveSession> activeSessions = new Dictionary<Players.PlayerIDShort, ActiveSession>();

	private readonly HashSet<Players.PlayerIDShort> flirEnabledPlayers = new HashSet<Players.PlayerIDShort>();

	private readonly List<PendingStrike> pendingStrikes = new List<PendingStrike>();

	private Ac130Config config;

	private string modRoot;

	private ushort terminalType;

	private ushort terminalTypeXp;

	private ushort terminalTypeXm;

	private ushort terminalTypeZp;

	private ushort terminalTypeZm;

	private ushort weapon25Type;

	private ushort weapon40Type;

	private ushort weapon105Type;

	private ushort zoomOutType;

	private ushort zoomInType;

	private ushort exitSignalType;

	private ushort airType;

	private MeshedObjectType gunshipVehicleType;

	private MeshedObjectTypeSettings gunshipVehicleSettings;

	private bool chatCommandsRegistered;

	private string runtimeStatePath;

	public static Ac130ServerEntry Instance { get; private set; }

	public Ac130ServerEntry()
	{
		Instance = this;
	}

	public void AfterItemTypesDefined()
	{
		if (config == null)
		{
			modRoot = ResolveModRoot();
			config = Ac130ConfigLoader.LoadFromModRoot(modRoot);
			runtimeStatePath = Path.Combine(modRoot, RuntimeStateFileName);
			LoadRuntimeState();
			SaveRuntimeState();
			terminalType = ItemTypes.IndexLookup.GetIndex("BetterAC130.GunshipTerminal");
			weapon25Type = ItemTypes.IndexLookup.GetIndex("BetterAC130.Weapon25mm");
			weapon40Type = ItemTypes.IndexLookup.GetIndex("BetterAC130.Weapon40mm");
			weapon105Type = ItemTypes.IndexLookup.GetIndex("BetterAC130.Weapon105mm");
			zoomOutType = ItemTypes.IndexLookup.GetIndex("BetterAC130.ZoomOut");
			zoomInType = ItemTypes.IndexLookup.GetIndex("BetterAC130.ZoomIn");
			exitSignalType = ItemTypes.IndexLookup.GetIndex("BetterAC130.ExitSignal");
			airType = BuiltinBlocks.Indices.air;
			ItemTypes.ItemType type = ItemTypes.GetType(terminalType);
			terminalTypeXp = ResolveRotatedType(type.RotatedXPlus, terminalType);
			terminalTypeXm = ResolveRotatedType(type.RotatedXMinus, terminalType);
			terminalTypeZp = ResolveRotatedType(type.RotatedZPlus, terminalType);
			terminalTypeZm = ResolveRotatedType(type.RotatedZMinus, terminalType);
			RegisterGunshipVehicleType();
			EnsureChatCommandsRegistered();
		}
	}

	public void IOnLoadingImages(Dictionary<string, string> imagesToLoad)
	{
		string path = modRoot ?? ResolveModRoot();
		RegisterHudImage(imagesToLoad, "betterac130.hud.top", Path.Combine(path, "images", "hud_top.png"));
		RegisterHudImage(imagesToLoad, "betterac130.hud.bottom", Path.Combine(path, "images", "hud_bottom.png"));
		RegisterHudImage(imagesToLoad, "betterac130.hud.left", Path.Combine(path, "images", "hud_left.png"));
		RegisterHudImage(imagesToLoad, "betterac130.hud.right", Path.Combine(path, "images", "hud_right.png"));
		RegisterHudImage(imagesToLoad, "betterac130.flir.fullscreen", Path.Combine(path, "images", "flir_fullscreen.png"));
	}

	public void OnConstructPointUpgradesMenu(Players.Player player, NetworkMenu menu)
	{
		ColonyGroup activeColonyGroup = player.ActiveColonyGroup;
		if (activeColonyGroup != null && config != null)
		{
			Ac130UpgradeState orCreateState = GetOrCreateState(activeColonyGroup.ColonyGroupID.Value);
			Table table = new Table(900, 160);
			menu.Items.Add(table);
			table.AutoExpandHeight = false;
			table.ExternalMarginHorizontal = 3f;
			table.Header = new BackgroundColor(new HorizontalRow(new List<(IItem, int)>
			{
				(new Label(new LabelData("Gunship Support", ELabelAlignment.Default, 16, LabelData.ELocalizationType.None), 30), 230),
				(new Label(new LabelData("Current", ELabelAlignment.Default, 16, LabelData.ELocalizationType.None), 30), 200),
				(new Label(new LabelData("Next", ELabelAlignment.Default, 16, LabelData.ELocalizationType.None), 30), 200),
				(new Label(new LabelData("Cost", ELabelAlignment.Default, 16, LabelData.ELocalizationType.None), 30), 90),
				(new Label(new LabelData("Action", ELabelAlignment.Default, 16, LabelData.ELocalizationType.None), 30), 110)
			}), -1, -1, 0f, 0f, 4f, 4f, Table.HEADER_COLOR);
			table.Rows = new List<IItem>
			{
				CreateUpgradeRow(activeColonyGroup, orCreateState, "Overwatch Time", orCreateState.DurationLevel, config.Upgrades.Duration.MaxLevel, "betterac130.upgrade.duration", DescribeDurationLevel(orCreateState.DurationLevel), DescribeDurationLevel(orCreateState.DurationLevel + 1), config.Upgrades.Duration.GetCostForLevel(orCreateState.DurationLevel)),
				CreateUpgradeRow(activeColonyGroup, orCreateState, "Rearm Protocol", orCreateState.CooldownLevel, config.Upgrades.Cooldown.MaxLevel, "betterac130.upgrade.cooldown", DescribeCooldownLevel(orCreateState.CooldownLevel), DescribeCooldownLevel(orCreateState.CooldownLevel + 1), config.Upgrades.Cooldown.GetCostForLevel(orCreateState.CooldownLevel)),
				CreateUpgradeRow(activeColonyGroup, orCreateState, "Magazine Feed", orCreateState.AmmoLevel, config.Upgrades.Ammo.MaxLevel, "betterac130.upgrade.ammo", DescribeAmmoLevel(orCreateState.AmmoLevel), DescribeAmmoLevel(orCreateState.AmmoLevel + 1), config.Upgrades.Ammo.GetCostForLevel(orCreateState.AmmoLevel)),
				CreateUpgradeRow(activeColonyGroup, orCreateState, "Fire Control", orCreateState.FireRateLevel, config.Upgrades.FireRate.MaxLevel, "betterac130.upgrade.firerate", DescribeFireRateLevel(orCreateState.FireRateLevel), DescribeFireRateLevel(orCreateState.FireRateLevel + 1), config.Upgrades.FireRate.GetCostForLevel(orCreateState.FireRateLevel)),
				CreateUpgradeRow(activeColonyGroup, orCreateState, "Warhead Package", orCreateState.DamageLevel, config.Upgrades.Damage.MaxLevel, "betterac130.upgrade.damage", DescribeDamageLevel(orCreateState.DamageLevel), DescribeDamageLevel(orCreateState.DamageLevel + 1), config.Upgrades.Damage.GetCostForLevel(orCreateState.DamageLevel))
			};
			table.Height = System.Math.Min(320, 41 + 38 * table.Rows.Count);
		}
	}

	public void OnLoadingColonyGroup(ColonyGroup colony, JObject json)
	{
		JObject jObject = json["betterac130"] as JObject;
		Ac130UpgradeState orCreateState = GetOrCreateState(colony.ColonyGroupID.Value);
		if (jObject != null)
		{
			orCreateState.DurationLevel = jObject.Value<int?>("durationLevel").GetValueOrDefault();
			orCreateState.CooldownLevel = jObject.Value<int?>("cooldownLevel").GetValueOrDefault();
			orCreateState.AmmoLevel = jObject.Value<int?>("ammoLevel").GetValueOrDefault();
			orCreateState.FireRateLevel = jObject.Value<int?>("fireRateLevel").GetValueOrDefault();
			orCreateState.DamageLevel = jObject.Value<int?>("damageLevel").GetValueOrDefault();
			orCreateState.LastActivatedDay = jObject.Value<double?>("lastActivatedDay") ?? (-10000.0);
		}
	}

	public void OnPlayerClicked(Players.Player player, PlayerClickedData click)
	{
		if (config == null)
		{
			return;
		}
		if (TryGetActivationTerminalPosition(click, out Pipliz.Vector3Int terminalPosition))
		{
			TryStartGunshipSession(player, terminalPosition);
		}
		else
		{
			if (!activeSessions.TryGetValue(player.ID.ID, out var value))
			{
				return;
			}
			if (click.TypeSelected == exitSignalType)
			{
				value.AutoFireType = 0;
				EndSession(value, "Gunship disengaged.");
			}
			else if (click.ClickType == PlayerClickedData.EClickType.Left && click.TypeSelected == zoomOutType)
			{
				TryChangeZoomLevel(value, -1);
			}
			else if (click.ClickType == PlayerClickedData.EClickType.Left && click.TypeSelected == zoomInType)
			{
				TryChangeZoomLevel(value, 1);
			}
			else if (click.ClickType == PlayerClickedData.EClickType.Left)
			{
				Ac130WeaponSlot slot;
				Ac130WeaponConfig ac130WeaponConfig = TryResolveWeapon(click.TypeSelected, out slot);
				if (ac130WeaponConfig != null && TryConsumeShot(value, slot, ac130WeaponConfig))
				{
					Vector3 impactPoint = ResolveImpactPoint(value, click.PlayerEyePosition, click.PlayerAimDirection);
					FireWeapon(player, value, slot, ac130WeaponConfig, impactPoint);
					UpdateAutoFireLatch(value, slot, click.TypeSelected);
				}
			}
		}
	}

	public void OnPlayerHit(Players.Player player, ModLoader.OnHitData hit)
	{
		if (activeSessions.ContainsKey(player.ID.ID))
		{
			hit.ResultDamage = 0f;
		}
	}

	public void OnPlayerPushedNetworkUIButton(ButtonPressCallbackData data)
	{
		if (config == null)
		{
			return;
		}
		ColonyGroup activeColonyGroup = data.Player.ActiveColonyGroup;
		if (activeColonyGroup == null)
		{
			return;
		}
		Ac130UpgradeState orCreateState = GetOrCreateState(activeColonyGroup.ColonyGroupID.Value);
		bool flag = false;
		if (data.ButtonIdentifier == "betterac130.upgrade.duration")
		{
			flag = TryUpgrade(activeColonyGroup, config.Upgrades.Duration.MaxLevel, orCreateState.DurationLevel, config.Upgrades.Duration.GetCostForLevel(orCreateState.DurationLevel));
			if (flag)
			{
				orCreateState.DurationLevel++;
			}
		}
		else if (data.ButtonIdentifier == "betterac130.upgrade.cooldown")
		{
			flag = TryUpgrade(activeColonyGroup, config.Upgrades.Cooldown.MaxLevel, orCreateState.CooldownLevel, config.Upgrades.Cooldown.GetCostForLevel(orCreateState.CooldownLevel));
			if (flag)
			{
				orCreateState.CooldownLevel++;
			}
		}
		else if (data.ButtonIdentifier == "betterac130.upgrade.ammo")
		{
			flag = TryUpgrade(activeColonyGroup, config.Upgrades.Ammo.MaxLevel, orCreateState.AmmoLevel, config.Upgrades.Ammo.GetCostForLevel(orCreateState.AmmoLevel));
			if (flag)
			{
				orCreateState.AmmoLevel++;
			}
		}
		else if (data.ButtonIdentifier == "betterac130.upgrade.firerate")
		{
			flag = TryUpgrade(activeColonyGroup, config.Upgrades.FireRate.MaxLevel, orCreateState.FireRateLevel, config.Upgrades.FireRate.GetCostForLevel(orCreateState.FireRateLevel));
			if (flag)
			{
				orCreateState.FireRateLevel++;
			}
		}
		else if (data.ButtonIdentifier == "betterac130.upgrade.damage")
		{
			flag = TryUpgrade(activeColonyGroup, config.Upgrades.Damage.MaxLevel, orCreateState.DamageLevel, config.Upgrades.Damage.GetCostForLevel(orCreateState.DamageLevel));
			if (flag)
			{
				orCreateState.DamageLevel++;
			}
		}
		if (flag)
		{
			NetworkMenuManager.RequestNetworkUI("points_upgrades", data.Player);
		}
	}

	public void OnInventorySelectionChanged(Inventory.InventorySelectionContext context)
	{
		if (!activeSessions.TryGetValue(context.Player.ID.ID, out var value))
		{
			return;
		}
		ushort num = context.NewType?.ItemIndex ?? 0;
		if (num == weapon25Type || num == weapon40Type || num == weapon105Type || num == zoomOutType || num == zoomInType || num == exitSignalType)
		{
			value.SelectedType = num;
			if (num != value.AutoFireType)
			{
				value.AutoFireType = 0;
			}
			value.NextHudUpdateAt = 0.0;
			UpdateWeaponLoop(value);
		}
		else
		{
			value.SelectedType = num;
			value.AutoFireType = 0;
			value.NextHudUpdateAt = 0.0;
			UpdateWeaponLoop(value);
		}
	}

	public void OnSavingColonyGroup(ColonyGroup colony, JObject json)
	{
		Ac130UpgradeState orCreateState = GetOrCreateState(colony.ColonyGroupID.Value);
		json["betterac130"] = new JObject
		{
			{ "durationLevel", orCreateState.DurationLevel },
			{ "cooldownLevel", orCreateState.CooldownLevel },
			{ "ammoLevel", orCreateState.AmmoLevel },
			{ "fireRateLevel", orCreateState.FireRateLevel },
			{ "damageLevel", orCreateState.DamageLevel },
			{ "lastActivatedDay", orCreateState.LastActivatedDay }
		};
	}

	public void OnPlayerDisconnected(Players.Player player)
	{
		if (player != null && activeSessions.TryGetValue(player.ID.ID, out var value))
		{
			EndSession(value, null);
		}
	}

	public void OnUpdate()
	{
		double secondsSinceStartDoubleThisFrame = Pipliz.Time.SecondsSinceStartDoubleThisFrame;
		if (activeSessions.Count == 0)
		{
			return;
		}
		List<(Players.PlayerIDShort, string)> list = null;
		foreach (KeyValuePair<Players.PlayerIDShort, ActiveSession> activeSession in activeSessions)
		{
			ActiveSession value = activeSession.Value;
			if (value.Player != null && value.Player.PauseRequestState)
			{
				if (list == null)
				{
					list = new List<(Players.PlayerIDShort, string)>();
				}
				list.Add((activeSession.Key, "Gunship disengaged."));
			}
		}
		if (list != null)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (activeSessions.TryGetValue(list[i].Item1, out var value2))
				{
					EndSession(value2, list[i].Item2);
				}
			}
			return;
		}
		bool pauseState = Players.GetPauseState();
		UpdateSessionPauseStates(secondsSinceStartDoubleThisFrame, pauseState);
		if (pauseState)
		{
			return;
		}
		UpdatePendingStrikes(secondsSinceStartDoubleThisFrame);
		list = null;
		foreach (KeyValuePair<Players.PlayerIDShort, ActiveSession> activeSession in activeSessions)
		{
			ActiveSession value = activeSession.Value;
			MeshedVehicleDescription description;
			if (value.Player == null || value.Player.ConnectionState != Players.EConnectionState.Connected)
			{
				if (list == null)
				{
					list = new List<(Players.PlayerIDShort, string)>();
				}
				list.Add((activeSession.Key, null));
			}
			else if (secondsSinceStartDoubleThisFrame >= value.EndsAtSeconds)
			{
				if (list == null)
				{
					list = new List<(Players.PlayerIDShort, string)>();
				}
				list.Add((activeSession.Key, "Gunship offline."));
			}
			else if (!MeshedObjectManager.TryGetVehicle(value.Player, out description) || description.Object.ObjectID.ID != value.Vehicle.Object.ObjectID.ID)
			{
				if (list == null)
				{
					list = new List<(Players.PlayerIDShort, string)>();
				}
				list.Add((activeSession.Key, null));
			}
			else
			{
				UpdateVehicleOrbit(value, secondsSinceStartDoubleThisFrame);
				UpdateAutoFire(value, secondsSinceStartDoubleThisFrame);
				UpdateHud(value, secondsSinceStartDoubleThisFrame);
			}
		}
		if (list == null)
		{
			return;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (activeSessions.TryGetValue(list[i].Item1, out var value2))
			{
				EndSession(value2, list[i].Item2);
			}
		}
	}

	private static bool TryUpgrade(ColonyGroup group, int maxLevel, int currentLevel, long cost)
	{
		if (currentLevel >= maxLevel)
		{
			return false;
		}
		if (!group.TryTakePoints(cost))
		{
			return false;
		}
		return true;
	}

	private IItem CreateUpgradeRow(ColonyGroup group, Ac130UpgradeState state, string title, int currentLevel, int maxLevel, string buttonId, string currentValue, string nextValue, long cost)
	{
		bool isInteractive = currentLevel < maxLevel && group.ColonyPoints >= cost;
		string text = ((currentLevel >= maxLevel) ? "MAX" : cost.ToString());
		string text2 = ((currentLevel >= maxLevel) ? "Installed" : "Upgrade");
		ButtonCallback item = new ButtonCallback(buttonId, new LabelData(text2, ELabelAlignment.MiddleCenter, 16, LabelData.ELocalizationType.None), 110, 30, ButtonCallback.EOnClickActions.DisableAllInteractive, null, 0f, 0f, isInteractive);
		List<(IItem, int)> list = new List<(IItem, int)>();
		list.Add((new Label(new LabelData(title + " (" + currentLevel + "/" + maxLevel + ")", ELabelAlignment.Default, 16, LabelData.ELocalizationType.None), 30), 230));
		list.Add((new Label(new LabelData(currentValue, ELabelAlignment.Default, 16, LabelData.ELocalizationType.None), 30), 200));
		list.Add((new Label(new LabelData(nextValue, ELabelAlignment.Default, 16, LabelData.ELocalizationType.None), 30), 200));
		list.Add((new Label(new LabelData(text, ELabelAlignment.Default, 16, LabelData.ELocalizationType.None), 30), 90));
		list.Add((item, 110));
		return new BackgroundColor(new HorizontalRow(list, 30), -1, 20, 0f, 0f, 4f, 4f, Table.ITEM_BG_COLOR);
	}

	private string DescribeDurationLevel(int level)
	{
		return Ac130Formatting.FormatSeconds(config.Session.DurationSeconds + config.Upgrades.Duration.DurationSecondsPerLevel * (float)System.Math.Min(level, config.Upgrades.Duration.MaxLevel));
	}

	private string DescribeCooldownLevel(int level)
	{
		return Ac130Formatting.FormatDays(System.Math.Max(0.5, config.Session.CooldownGameDays - config.Upgrades.Cooldown.CooldownDaysReductionPerLevel * (double)System.Math.Min(level, config.Upgrades.Cooldown.MaxLevel)));
	}

	private string DescribeAmmoLevel(int level)
	{
		Ac130UpgradeState state = new Ac130UpgradeState
		{
			AmmoLevel = System.Math.Min(level, config.Upgrades.Ammo.MaxLevel)
		};
		int ammoForWeapon = Ac130Balance.GetAmmoForWeapon(config, state, Ac130WeaponSlot.Weapon25mm);
		int ammoForWeapon2 = Ac130Balance.GetAmmoForWeapon(config, state, Ac130WeaponSlot.Weapon40mm);
		int ammoForWeapon3 = Ac130Balance.GetAmmoForWeapon(config, state, Ac130WeaponSlot.Weapon105mm);
		return ammoForWeapon + " / " + ammoForWeapon2 + " / " + ammoForWeapon3;
	}

	private string DescribeFireRateLevel(int level)
	{
		Ac130UpgradeState state = new Ac130UpgradeState
		{
			FireRateLevel = System.Math.Min(level, config.Upgrades.FireRate.MaxLevel)
		};
		float weaponCooldownSeconds = Ac130Balance.GetWeaponCooldownSeconds(config, state, Ac130WeaponSlot.Weapon25mm);
		float weaponCooldownSeconds2 = Ac130Balance.GetWeaponCooldownSeconds(config, state, Ac130WeaponSlot.Weapon40mm);
		float weaponCooldownSeconds3 = Ac130Balance.GetWeaponCooldownSeconds(config, state, Ac130WeaponSlot.Weapon105mm);
		return weaponCooldownSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " / " + weaponCooldownSeconds2.ToString("0.00", CultureInfo.InvariantCulture) + " / " + weaponCooldownSeconds3.ToString("0.00", CultureInfo.InvariantCulture) + "s";
	}

	private string DescribeDamageLevel(int level)
	{
		Ac130UpgradeState state = new Ac130UpgradeState
		{
			DamageLevel = System.Math.Min(level, config.Upgrades.Damage.MaxLevel)
		};
		int num = Mathf.RoundToInt(Ac130Balance.GetWeaponDamage(config, state, Ac130WeaponSlot.Weapon25mm));
		int num2 = Mathf.RoundToInt(Ac130Balance.GetWeaponDamage(config, state, Ac130WeaponSlot.Weapon40mm));
		int num3 = Mathf.RoundToInt(Ac130Balance.GetWeaponDamage(config, state, Ac130WeaponSlot.Weapon105mm));
		return num + " / " + num2 + " / " + num3;
	}

	private void TryChangeZoomLevel(ActiveSession session, int delta)
	{
		if (session == null || delta == 0)
		{
			return;
		}
		int num = Mathf.Clamp(session.ZoomLevel + delta, 0, ActiveSession.ZoomLevelCount - 1);
		if (num == session.ZoomLevel)
		{
			return;
		}
		session.ZoomLevel = num;
		ApplyZoomLevel(session, Pipliz.Time.SecondsSinceStartDoubleThisFrame);
		session.NextHudUpdateAt = 0.0;
		Chat.Send(session.Player, "Zoom " + GetZoomLabel(session.ZoomLevel) + ".");
	}

	private void ApplyZoomLevel(ActiveSession session, double now)
	{
		int num = Mathf.Clamp(session.ZoomLevel, 0, ZoomRadiusScales.Length - 1);
		session.OrbitRadius = Mathf.Max(18f, session.BaseOrbitRadius * ZoomRadiusScales[num]);
		session.OrbitAltitude = Mathf.Max(36f, session.BaseOrbitAltitude * ZoomAltitudeScales[num]);
		session.LastOrbitSampleAt = now;
		session.CurrentVehiclePosition = session.OrbitCenter + ComputeOrbitOffset(session.OrbitAngleRadians, session.OrbitRadius, session.OrbitAltitude);
		session.CurrentVehicleRotation = ComputeOrbitLookRotation(session.CurrentVehiclePosition, session.OrbitCenter, session.OrbitAngleRadians, session.OrbitRadius);
		session.Vehicle.Object.SendMoveToInterpolatedRenderDistance(session.CurrentVehiclePosition, session.CurrentVehicleRotation, gunshipVehicleSettings, config.Session.VehicleUpdateDelayMS);
		session.NextVehicleSendAt = now + (double)config.Session.VehicleUpdateDelayMS / 1000.0;
	}

	private static string GetZoomLabel(int zoomLevel)
	{
		return zoomLevel switch
		{
			0 => "wide", 
			2 => "tight", 
			_ => "medium", 
		};
	}

	public bool TryResetCooldownForPlayer(Players.Player player, out string message)
	{
		message = null;
		if (player == null)
		{
			message = "No player context available.";
			return false;
		}
		ColonyGroup activeColonyGroup = player.ActiveColonyGroup;
		if (activeColonyGroup == null)
		{
			message = "No active colony group selected.";
			return false;
		}
		GetOrCreateState(activeColonyGroup.ColonyGroupID.Value).LastActivatedDay = -10000.0;
		message = "AC130 cooldown reset for " + activeColonyGroup.Name + ".";
		return true;
	}

	public bool TrySetTerrainDamageEnabled(bool enabled, out string message)
	{
		message = null;
		if (config == null)
		{
			message = "BetterAC130 config is not ready yet.";
			return false;
		}
		config.Session.AllowBlockDamage = enabled;
		SaveRuntimeState();
		message = (enabled ? "AC130 terrain damage enabled." : "AC130 terrain damage disabled.");
		return true;
	}

	public bool TryToggleTerrainDamage(out string message)
	{
		bool enabled = config == null || !config.Session.AllowBlockDamage;
		return TrySetTerrainDamageEnabled(enabled, out message);
	}

	public bool TryGiveUplinkBlock(Players.Player player, out string message)
	{
		message = null;
		if (player == null)
		{
			message = "No player context available.";
			return false;
		}
		if (!player.Inventory.TryAdd(terminalType))
		{
			message = "Could not add Gunship Uplink. Inventory is full.";
			return false;
		}
		player.SendStockpileInventory(player.ActiveColonyGroup?.Stockpile);
		message = "Granted 1x Gunship Uplink.";
		return true;
	}

	public bool TrySetFlirEnabled(Players.Player player, bool enabled, out string message)
	{
		if (player == null)
		{
			message = "Player is not available.";
			return false;
		}
		Players.PlayerIDShort iD = player.ID.ID;
		if (enabled)
		{
			flirEnabledPlayers.Add(iD);
		}
		else
		{
			flirEnabledPlayers.Remove(iD);
		}
		RefreshFlirOverlay(player, enabled);
		message = "AC130 FLIR overlay " + (enabled ? "enabled." : "disabled.");
		return true;
	}

	public bool TryToggleFlir(Players.Player player, out string message)
	{
		if (player == null)
		{
			message = "Player is not available.";
			return false;
		}
		return TrySetFlirEnabled(player, !IsFlirEnabled(player.ID.ID), out message);
	}

	private Ac130UpgradeState GetOrCreateState(int colonyGroupId)
	{
		if (!colonyStateByGroup.TryGetValue(colonyGroupId, out var value))
		{
			value = new Ac130UpgradeState();
			colonyStateByGroup[colonyGroupId] = value;
		}
		return value;
	}

	private bool TryGetActivationTerminalPosition(PlayerClickedData click, out Pipliz.Vector3Int terminalPosition)
	{
		terminalPosition = default(Pipliz.Vector3Int);
		if (click.ConsumedType != PlayerClickedData.EConsumedType.Reserved || click.HitType != PlayerClickedData.EHitType.Block)
		{
			return false;
		}
		Vector3 exactHitPositionWorld = click.GetExactHitPositionWorld();
		Pipliz.Vector3Int center = ToVoxelPosition(exactHitPositionWorld);
		for (int x = -1; x <= 1; x++)
		{
			for (int y = -1; y <= 1; y++)
			{
				for (int z = -1; z <= 1; z++)
				{
					Pipliz.Vector3Int position = center.Add(x, y, z);
					if (World.TryGetTypeAt(position, out ItemTypes.ItemType type) && IsTerminalType(type.ItemIndex))
					{
						terminalPosition = position;
						return true;
					}
				}
			}
		}
		return false;
	}

	private bool IsTerminalType(ushort typeIndex)
	{
		if (typeIndex != terminalType && typeIndex != terminalTypeXp && typeIndex != terminalTypeXm && typeIndex != terminalTypeZp)
		{
			return typeIndex == terminalTypeZm;
		}
		return true;
	}

	private void TryStartGunshipSession(Players.Player player, Pipliz.Vector3Int terminalPosition)
	{
		if (activeSessions.ContainsKey(player.ID.ID))
		{
			Chat.Send(player, "Gunship support is already active.");
			return;
		}
		ColonyGroup activeColonyGroup = player.ActiveColonyGroup;
		Colony colony = activeColonyGroup?.MainColony ?? player.ActiveColony;
		if (activeColonyGroup == null || colony == null)
		{
			Chat.Send(player, "Select an active colony before using the gunship uplink.");
			return;
		}
		Ac130UpgradeState orCreateState = GetOrCreateState(activeColonyGroup.ColonyGroupID.Value);
		double totalDays = TimeCycle.TotalTime.Value.TotalDays;
		double num = orCreateState.LastActivatedDay + Ac130Balance.GetCooldownDays(config, orCreateState);
		if (totalDays < num)
		{
			Chat.Send(player, "Gunship rearming. Ready in " + Ac130Formatting.FormatDays(num - totalDays) + ".");
			return;
		}
		orCreateState.LastActivatedDay = totalDays;
		float num2 = Mathf.Max(18f, config.Session.OrbitRadius);
		float num3 = Mathf.Max(30f, config.Session.OrbitAltitude);
		float num4 = ResolveStartAngle(player, terminalPosition.Vector + new Vector3(0.5f, 0f, 0.5f));
		Vector3 vector = terminalPosition.Vector + new Vector3(0.5f, 0f, 0.5f);
		Vector3 vector2 = vector + ComputeOrbitOffset(num4, num2, num3);
		Quaternion quaternion = ComputeOrbitLookRotation(vector2, vector, num4, num2);
		MeshedVehicleDescription vehicle = new MeshedVehicleDescription(new ClientMeshedObject(gunshipVehicleType, MeshedObjectID.GetNew()), Vector3.zero, allowPlayerEditingBlocks: false, 0f, forceCrouch: false);
		MeshedObjectManager.Attach(player, vehicle);
		vehicle.Object.SendMoveToInterpolatedRenderDistance(vector2, quaternion, gunshipVehicleSettings, config.Session.VehicleUpdateDelayMS);
		double secondsSinceStartDoubleThisFrame = Pipliz.Time.SecondsSinceStartDoubleThisFrame;
		ActiveSession activeSession = new ActiveSession
		{
			Player = player,
			PlayerID = player.ID.ID,
			ColonyGroupID = activeColonyGroup.ColonyGroupID.Value,
			StartedAtSeconds = secondsSinceStartDoubleThisFrame,
			EndsAtSeconds = secondsSinceStartDoubleThisFrame + (double)Ac130Balance.GetDurationSeconds(config, orCreateState),
			LastOrbitSampleAt = secondsSinceStartDoubleThisFrame,
			NextVehicleSendAt = secondsSinceStartDoubleThisFrame + (double)config.Session.VehicleUpdateDelayMS / 1000.0,
			Weapon25Ammo = Ac130Balance.GetAmmoForWeapon(config, orCreateState, Ac130WeaponSlot.Weapon25mm),
			Weapon40Ammo = Ac130Balance.GetAmmoForWeapon(config, orCreateState, Ac130WeaponSlot.Weapon40mm),
			Weapon105Ammo = Ac130Balance.GetAmmoForWeapon(config, orCreateState, Ac130WeaponSlot.Weapon105mm),
			OrbitAngleRadians = num4,
			OrbitRadius = num2,
			OrbitAltitude = num3,
			BaseOrbitRadius = num2,
			BaseOrbitAltitude = num3,
			ZoomLevel = 1,
			OrbitCenter = vector,
			CurrentVehiclePosition = vector2,
			CurrentVehicleRotation = quaternion,
			TerminalPosition = terminalPosition,
			Vehicle = vehicle,
			SelectedType = weapon25Type
		};
		SnapshotInventory(activeSession);
		EquipSessionInventory(activeSession);
		activeSessions[activeSession.PlayerID] = activeSession;
		StartSessionAudio(activeSession);
		UpdateHud(activeSession, secondsSinceStartDoubleThisFrame, force: true);
		Chat.Send(player, "Gunship online. Press 3/4/5 to switch weapons, 6/7 to zoom out or in, 8 to disengage. Left-click fires 105MM. Left-click starts or stops the 25MM and 40MM stream. Duration: " + Ac130Formatting.FormatSeconds(activeSession.EndsAtSeconds - activeSession.StartedAtSeconds) + ".");
	}

	private Ac130WeaponConfig TryResolveWeapon(ushort typeSelected, out Ac130WeaponSlot slot)
	{
		if (typeSelected == weapon25Type)
		{
			slot = Ac130WeaponSlot.Weapon25mm;
			return config.GetWeapon(slot);
		}
		if (typeSelected == weapon40Type)
		{
			slot = Ac130WeaponSlot.Weapon40mm;
			return config.GetWeapon(slot);
		}
		if (typeSelected == weapon105Type)
		{
			slot = Ac130WeaponSlot.Weapon105mm;
			return config.GetWeapon(slot);
		}
		slot = Ac130WeaponSlot.Weapon25mm;
		return null;
	}

	private bool TryConsumeShot(ActiveSession session, Ac130WeaponSlot slot, Ac130WeaponConfig weapon)
	{
		double secondsSinceStartDoubleThisFrame = Pipliz.Time.SecondsSinceStartDoubleThisFrame;
		float weaponCooldownSeconds = Ac130Balance.GetWeaponCooldownSeconds(config, GetOrCreateState(session.ColonyGroupID), slot);
		switch (slot)
		{
		case Ac130WeaponSlot.Weapon40mm:
			if (secondsSinceStartDoubleThisFrame < session.Weapon40NextShotAt || session.Weapon40Ammo <= 0)
			{
				return false;
			}
			session.Weapon40NextShotAt = secondsSinceStartDoubleThisFrame + (double)weaponCooldownSeconds;
			session.Weapon40Ammo--;
			return true;
		case Ac130WeaponSlot.Weapon105mm:
			if (secondsSinceStartDoubleThisFrame < session.Weapon105NextShotAt || session.Weapon105Ammo <= 0)
			{
				return false;
			}
			session.Weapon105NextShotAt = secondsSinceStartDoubleThisFrame + (double)weaponCooldownSeconds;
			session.Weapon105Ammo--;
			return true;
		default:
			if (secondsSinceStartDoubleThisFrame < session.Weapon25NextShotAt || session.Weapon25Ammo <= 0)
			{
				return false;
			}
			session.Weapon25NextShotAt = secondsSinceStartDoubleThisFrame + (double)weaponCooldownSeconds;
			session.Weapon25Ammo--;
			return true;
		}
	}

	private void UpdateAutoFireLatch(ActiveSession session, Ac130WeaponSlot slot, ushort selectedType)
	{
		if (slot == Ac130WeaponSlot.Weapon105mm)
		{
			session.AutoFireType = 0;
			UpdateWeaponLoop(session);
		}
		else
		{
			session.AutoFireType = (ushort)((session.AutoFireType != selectedType) ? selectedType : 0);
			session.NextHudUpdateAt = 0.0;
			UpdateWeaponLoop(session);
		}
	}

	private void UpdateAutoFire(ActiveSession session, double now)
	{
		if (IsFireInputPaused(session))
		{
			return;
		}
		if (session.AutoFireType == 0 || session.SelectedType != session.AutoFireType)
		{
			return;
		}
		Ac130WeaponSlot slot;
		Ac130WeaponConfig ac130WeaponConfig = TryResolveWeapon(session.AutoFireType, out slot);
		if (ac130WeaponConfig == null || slot == Ac130WeaponSlot.Weapon105mm)
		{
			session.AutoFireType = 0;
			UpdateWeaponLoop(session);
		}
		else if (!TryConsumeShot(session, slot, ac130WeaponConfig))
		{
			if ((slot == Ac130WeaponSlot.Weapon25mm && session.Weapon25Ammo <= 0) || (slot == Ac130WeaponSlot.Weapon40mm && session.Weapon40Ammo <= 0))
			{
				session.AutoFireType = 0;
				session.NextHudUpdateAt = 0.0;
				UpdateWeaponLoop(session);
			}
		}
		else
		{
			Vector3 impactPoint = ResolveImpactPoint(session, session.Player.PositionCamera, session.Player.Forward);
			FireWeapon(session.Player, session, slot, ac130WeaponConfig, impactPoint);
		}
	}

	private void FireWeapon(Players.Player player, ActiveSession session, Ac130WeaponSlot slot, Ac130WeaponConfig weapon, Vector3 impactPoint)
	{
		Vector3 fireOrigin = ResolveFireOrigin(session, impactPoint, weapon);
		TryPlayWeaponFireAudio(player, session, slot, weapon);
		SpawnMuzzleSmoke(fireOrigin, impactPoint, weapon);
		pendingStrikes.Add(new PendingStrike
		{
			SourcePlayer = player,
			Session = session,
			Weapon = weapon,
			FireOrigin = fireOrigin,
			ImpactPoint = impactPoint,
			FiredAtSeconds = Pipliz.Time.SecondsSinceStartDoubleThisFrame,
			ImpactAtSeconds = Pipliz.Time.SecondsSinceStartDoubleThisFrame + (double)Mathf.Max(0.01f, weapon.ProjectileTravelSeconds),
			TrailProgressSent = 0f,
			TrailProgressStep = 1f / (float)Mathf.Max(1, weapon.ProjectileTrailSegments),
			ImpactBurstsRemaining = Mathf.Max(0, weapon.ImpactBurstCount - 1)
		});
	}

	private void UpdatePendingStrikes(double now)
	{
		if (pendingStrikes.Count == 0)
		{
			return;
		}
		for (int num = pendingStrikes.Count - 1; num >= 0; num--)
		{
			PendingStrike pendingStrike = pendingStrikes[num];
			EmitStrikeTrail(pendingStrike, now);
			if (pendingStrike.ImpactAtSeconds > 0.0 && now >= pendingStrike.ImpactAtSeconds)
			{
				ApplyWeaponImpact(pendingStrike.SourcePlayer, pendingStrike.Session, pendingStrike.Weapon, pendingStrike.ImpactPoint, playImpactAudio: true);
				pendingStrike.ImpactAtSeconds = 0.0;
				pendingStrike.NextImpactBurstAt = now + (double)pendingStrike.Weapon.ImpactBurstIntervalSeconds;
				if (pendingStrike.Weapon.ImpactSmokeSeconds > 0f)
				{
					SpawnImpactSmoke(pendingStrike.ImpactPoint, pendingStrike.Weapon);
				}
			}
			while (pendingStrike.ImpactAtSeconds == 0.0 && pendingStrike.ImpactBurstsRemaining > 0 && now >= pendingStrike.NextImpactBurstAt)
			{
				TriggerImpactVisual(pendingStrike.Session?.Player, pendingStrike.ImpactPoint, pendingStrike.Weapon, 0.62f, 0.86f, playImpactAudio: false);
				pendingStrike.ImpactBurstsRemaining--;
				pendingStrike.NextImpactBurstAt += pendingStrike.Weapon.ImpactBurstIntervalSeconds;
			}
			if (pendingStrike.ImpactAtSeconds == 0.0 && pendingStrike.ImpactBurstsRemaining <= 0)
			{
				pendingStrikes.RemoveAt(num);
			}
		}
	}

	private void EmitStrikeTrail(PendingStrike strike, double now)
	{
		double num = System.Math.Max(0.01, strike.Weapon.ProjectileTravelSeconds);
		float num2 = Mathf.Clamp01((float)((now - strike.FiredAtSeconds) / num));
		if (!(num2 <= strike.TrailProgressSent))
		{
			while (num2 > strike.TrailProgressSent + 0.0001f)
			{
				float num3 = Mathf.Min(num2, strike.TrailProgressSent + strike.TrailProgressStep);
				Vector3 start = Vector3.Lerp(strike.FireOrigin, strike.ImpactPoint, strike.TrailProgressSent);
				Vector3 end = Vector3.Lerp(strike.FireOrigin, strike.ImpactPoint, num3);
				SendParticleTrailToPlayer(strike.Session?.Player, start, end, Mathf.Max(0.18f, strike.Weapon.TrailFadeSeconds));
				strike.TrailProgressSent = num3;
			}
		}
	}

	private void ApplyWeaponImpact(Players.Player player, ActiveSession session, Ac130WeaponConfig weapon, Vector3 impactPoint, bool playImpactAudio)
	{
		TriggerImpactVisual(session.Player, impactPoint, weapon, 1f, 1f, playImpactAudio);
		Ac130UpgradeState orCreateState = GetOrCreateState(session.ColonyGroupID);
		float monsterSplashRadius = Ac130Balance.GetMonsterSplashRadius(weapon, config, orCreateState);
		float weaponDamage = Ac130Balance.GetWeaponDamage(weapon, config, orCreateState);
		int num = (int)System.Math.Ceiling(monsterSplashRadius);
		MonsterSplashDamage callbackInstance = new MonsterSplashDamage
		{
			Center = impactPoint,
			Radius = monsterSplashRadius,
			Damage = weaponDamage,
			SourcePlayer = player
		};
		MonsterTracker.IterateMonsters(new Pipliz.Vector3Int(impactPoint - Vector3.one * num), new Pipliz.Vector3Int(impactPoint + Vector3.one * num), ref callbackInstance);
		if (callbackInstance.Kills > 0)
		{
			session.LastKillCount = callbackInstance.Kills;
			session.KillFeedUntilAt = Pipliz.Time.SecondsSinceStartDoubleThisFrame + 1.7;
			session.NextHudUpdateAt = 0.0;
		}
		float blockDamageRadius = GetBlockDamageRadius(weapon);
		if (config.Session.AllowBlockDamage && blockDamageRadius > 0.01f)
		{
			ApplyBlockDamage(impactPoint, blockDamageRadius);
		}
	}

	private void TriggerImpactVisual(Players.Player viewer, Vector3 impactPoint, Ac130WeaponConfig weapon, float strengthMultiplier, float sizeMultiplier, bool playImpactAudio)
	{
		Vector3 vector = Vector3.zero;
		if (weapon.ImpactBurstJitterRadius > 0f && strengthMultiplier < 0.999f)
		{
			Vector2 vector2 = UnityEngine.Random.insideUnitCircle * weapon.ImpactBurstJitterRadius;
			vector = new Vector3(vector2.x, 0f, vector2.y);
		}
		Vector3 vector3 = impactPoint + vector + Vector3.up * Mathf.Max(0.18f, weapon.Radius * 0.1f);
		float num = weapon.ExplosionStrength * strengthMultiplier;
		float num2 = weapon.Radius * sizeMultiplier;
		float num3 = weapon.SmokeMultiplier * Mathf.Lerp(0.7f, 1f, strengthMultiplier);
		float num4 = Mathf.Max(weapon.ParticleSize * sizeMultiplier, num2 * 0.11f);
		SendExplosionEffectToPlayer(viewer, vector3, num * 1.12f, num2 * 1.22f, num3, num4);
		if (strengthMultiplier >= 0.99f)
		{
			SendExplosionEffectToPlayer(viewer, vector3 + Vector3.up * Mathf.Max(0.16f, num2 * 0.06f), num * 0.72f, num2 * 0.8f, num3 * 0.82f, num4 * 1.15f);
		}
		SpawnImpactFlash(viewer, vector3, weapon, strengthMultiplier);
		SpawnImpactDebris(viewer, vector3, weapon, strengthMultiplier);
		SpawnViewerProxyImpact(viewer, vector3, weapon, strengthMultiplier);
		if (playImpactAudio && !string.IsNullOrEmpty(weapon.ImpactAudio))
		{
			PlayDirectAudio(viewer, weapon.ImpactAudio);
		}
	}

	private void SpawnViewerProxyImpact(Players.Player viewer, Vector3 impactPoint, Ac130WeaponConfig weapon, float strengthMultiplier)
	{
		if (viewer == null || !viewer.IsConnectionReady)
		{
			return;
		}
		Vector3 positionCamera = viewer.PositionCamera;
		Vector3 vector = impactPoint - positionCamera;
		float magnitude = vector.magnitude;
		if (magnitude < 52f)
		{
			return;
		}
		Vector3 normalized = vector / magnitude;
		float num = Mathf.Clamp(magnitude - 4f, 38f, 47.5f);
		Vector3 vector2 = positionCamera + normalized * num;
		float t = Mathf.Clamp01(weapon.Radius / 10f);
		float strength = weapon.ExplosionStrength * Mathf.Lerp(0.18f, 0.34f, t) * Mathf.Lerp(0.92f, 1.08f, strengthMultiplier);
		float radius = Mathf.Lerp(0.9f, 2.1f, t) * Mathf.Lerp(0.95f, 1.1f, strengthMultiplier);
		float smokeMultiplier = Mathf.Lerp(0.45f, 0.9f, t);
		float particleSize = Mathf.Lerp(0.2f, 0.55f, t);
		SendExplosionEffectToPlayer(viewer, vector2, strength, radius, smokeMultiplier, particleSize);
		SendParticleTrailToPlayer(viewer, vector2 + Vector3.up * 0.1f, vector2 + Vector3.up * Mathf.Lerp(2.4f, 5.8f, t), Mathf.Lerp(0.16f, 0.3f, t));
	}

	private void SpawnImpactDebris(Players.Player viewer, Vector3 impactPoint, Ac130WeaponConfig weapon, float strengthMultiplier)
	{
		int num = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(4f, 10f, Mathf.Clamp01(weapon.Radius / 10f)) * Mathf.Lerp(0.75f, 1.15f, strengthMultiplier)), 3, 12);
		float fadeOutTimeSeconds = Mathf.Max(0.12f, weapon.TrailFadeSeconds * Mathf.Lerp(0.45f, 0.8f, strengthMultiplier));
		float num2 = Mathf.Max(0.8f, weapon.Radius * 0.55f);
		float maxInclusive = Mathf.Max(num2 + 0.4f, weapon.Radius * 1.2f);
		for (int i = 0; i < num; i++)
		{
			Vector2 vector = UnityEngine.Random.insideUnitCircle;
			if (vector.sqrMagnitude < 0.001f)
			{
				vector = Vector2.right;
			}
			vector.Normalize();
			Vector3 vector2 = impactPoint + new Vector3(vector.x, UnityEngine.Random.Range(0.05f, 0.35f), vector.y) * Mathf.Max(0.12f, weapon.Radius * 0.08f);
			Vector3 end = vector2 + new Vector3(vector.x * UnityEngine.Random.Range(num2, maxInclusive), UnityEngine.Random.Range(0.5f, Mathf.Max(1.1f, weapon.Radius * 0.55f)), vector.y * UnityEngine.Random.Range(num2, maxInclusive));
			SendParticleTrailToPlayer(viewer, vector2, end, fadeOutTimeSeconds);
		}
		if (viewer != null)
		{
			Vector3 vector3 = (viewer.PositionCamera - impactPoint).normalized;
			if (vector3.sqrMagnitude < 0.001f)
			{
				vector3 = Vector3.up;
			}
			float num3 = Mathf.Max(8f, weapon.Radius * 2.8f);
			Vector3 end2 = impactPoint + Vector3.up * Mathf.Max(3.5f, weapon.Radius * 1.6f);
			SendParticleTrailToPlayer(viewer, impactPoint + Vector3.up * 0.15f, end2, Mathf.Max(0.22f, weapon.TrailFadeSeconds * 0.95f));
			SendParticleTrailToPlayer(viewer, impactPoint + Vector3.up * 0.2f, impactPoint + vector3 * num3 + Vector3.up * Mathf.Max(1.8f, weapon.Radius * 0.55f), Mathf.Max(0.24f, weapon.TrailFadeSeconds));
			SendParticleTrailToPlayer(viewer, impactPoint + Vector3.up * 0.1f, impactPoint - vector3 * (num3 * 0.35f) + Vector3.up * Mathf.Max(1.2f, weapon.Radius * 0.35f), Mathf.Max(0.16f, weapon.TrailFadeSeconds * 0.72f));
		}
	}

	private void SpawnImpactFlash(Players.Player viewer, Vector3 impactPoint, Ac130WeaponConfig weapon, float strengthMultiplier)
	{
		if (viewer == null)
		{
			return;
		}
		float t = Mathf.Clamp01(weapon.Radius / 10f);
		int num = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(9f, 22f, t) * Mathf.Lerp(0.95f, 1.3f, strengthMultiplier)), 8, 24);
		float num2 = Mathf.Lerp(1.8f, 6.2f, t) * Mathf.Lerp(0.95f, 1.18f, strengthMultiplier);
		float num3 = Mathf.Lerp(0.12f, 0.24f, t);
		Vector3 vector = impactPoint + Vector3.up * Mathf.Lerp(0.22f, 0.65f, t);
		for (int i = 0; i < num; i++)
		{
			Vector3 vector2 = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(0.15f, 0.95f), UnityEngine.Random.Range(-1f, 1f));
			if (vector2.sqrMagnitude < 0.001f)
			{
				vector2 = Vector3.up;
			}
			vector2.Normalize();
			float num4 = num2 * UnityEngine.Random.Range(0.8f, 1.3f);
			Vector3 start = vector + vector2 * UnityEngine.Random.Range(0.08f, 0.28f);
			Vector3 end = vector + vector2 * num4;
			SendParticleTrailToPlayer(viewer, start, end, num3);
		}
		float num5 = Mathf.Lerp(2.6f, 7.2f, t) * Mathf.Lerp(0.98f, 1.24f, strengthMultiplier);
		SendParticleTrailToPlayer(viewer, vector, vector + Vector3.up * num5, Mathf.Lerp(0.12f, 0.26f, t));
		SendParticleTrailToPlayer(viewer, vector + Vector3.right * 0.35f, vector - Vector3.right * 0.35f, num3 * 0.9f);
		SendParticleTrailToPlayer(viewer, vector + Vector3.forward * 0.35f, vector - Vector3.forward * 0.35f, num3 * 0.9f);
	}

	private static void SendExplosionEffectToPlayer(Players.Player player, Vector3 position, float strength, float radius, float smokeMultiplier = 1f, float particleSize = 1f)
	{
		if (player == null || !player.IsConnectionReady)
		{
			return;
		}
		using ByteBuilder byteBuilder = ByteBuilder.Get();
		byteBuilder.Write(ClientMessageType.TriggerParticles);
		byteBuilder.Write((byte)2);
		byteBuilder.Write(position);
		byteBuilder.Write(strength);
		byteBuilder.Write(radius);
		byteBuilder.Write(smokeMultiplier);
		byteBuilder.Write(particleSize);
		NetworkWrapper.Send(byteBuilder, player);
	}

	private static void SendDirectionalExplosionEffectToPlayer(Players.Player player, Vector3 worldPosition, RotatedBounds localBounds, Vector3 direction, Vector3 particleSource, float strength, float smokeMultiplier = 1f, float particleSize = 1f)
	{
		if (player == null || !player.IsConnectionReady)
		{
			return;
		}
		using ByteBuilder byteBuilder = ByteBuilder.Get();
		byteBuilder.Write(ClientMessageType.TriggerParticles);
		byteBuilder.Write((byte)3);
		byteBuilder.Write(worldPosition);
		byteBuilder.Write(localBounds.Bounds.Min);
		byteBuilder.Write(localBounds.Bounds.Max);
		byteBuilder.Write(localBounds.Rotation);
		byteBuilder.Write(direction);
		byteBuilder.Write(particleSource);
		byteBuilder.Write(strength);
		byteBuilder.Write(smokeMultiplier);
		byteBuilder.Write(particleSize);
		NetworkWrapper.Send(byteBuilder, player);
	}

	private static void SendParticleTrailToPlayer(Players.Player player, Vector3 start, Vector3 end, float fadeOutTimeSeconds)
	{
		if (player == null || !player.IsConnectionReady)
		{
			return;
		}
		using ByteBuilder byteBuilder = ByteBuilder.Get();
		byteBuilder.Write(ClientMessageType.TriggerParticles);
		byteBuilder.Write((byte)0);
		byteBuilder.Write(start);
		byteBuilder.Write(end);
		byteBuilder.Write(fadeOutTimeSeconds);
		NetworkWrapper.Send(byteBuilder, player);
	}

	private static float GetBlockDamageRadius(Ac130WeaponConfig weapon)
	{
		if (weapon == null || string.Equals(weapon.Id, "25mm", StringComparison.OrdinalIgnoreCase))
		{
			return 0f;
		}
		if (string.Equals(weapon.Id, "40mm", StringComparison.OrdinalIgnoreCase))
		{
			return Mathf.Max(1.15f, weapon.Radius * 0.32f);
		}
		return weapon.Radius * 1.05f;
	}

	private void SpawnImpactSmoke(Vector3 impactPoint, Ac130WeaponConfig weapon)
	{
		float num = Mathf.Max(0.45f, weapon.Radius * 0.24f);
		ParticleManager.SpawnEmitter(new BoundsPip(impactPoint + new Vector3(0f - num, 0f, 0f - num), impactPoint + new Vector3(num, num * 1.8f, num)), Vector3.up * Mathf.Lerp(0.3f, 1.1f, Mathf.Clamp01(weapon.Radius / 8f)), weapon.ImpactSmokeSeconds, Mathf.RoundToInt(Mathf.Lerp(90f, 180f, Mathf.Clamp01(weapon.Radius / 10f))), EParticleEmitterType.Fuse);
	}

	private void SpawnMuzzleSmoke(Vector3 fireOrigin, Vector3 impactPoint, Ac130WeaponConfig weapon)
	{
		float t = Mathf.Clamp01(weapon.Radius / 10f);
		float num = Mathf.Lerp(0.12f, 0.55f, t);
		Vector3 vector = impactPoint - fireOrigin;
		Vector3 normalized = (((vector.sqrMagnitude > 0.001f) ? vector.normalized : Vector3.down) * Mathf.Lerp(0.08f, 0.2f, t) + Vector3.up * Mathf.Lerp(0.45f, 0.95f, t)).normalized;
		float timeoutSeconds = Mathf.Lerp(0.07f, 0.26f, t);
		ParticleManager.SpawnEmitter(new BoundsPip(fireOrigin + new Vector3(0f - num, (0f - num) * 0.15f, 0f - num), fireOrigin + new Vector3(num, num * Mathf.Lerp(0.65f, 1.15f, t), num)), normalized, timeoutSeconds, Mathf.RoundToInt(Mathf.Lerp(90f, 180f, t)), EParticleEmitterType.Fuse);
	}

	private void ApplyBlockDamage(Vector3 impactPoint, float radius)
	{
		int num = (int)System.Math.Ceiling(radius);
		Pipliz.Vector3Int vector3Int = new Pipliz.Vector3Int(impactPoint - Vector3.one * num);
		Pipliz.Vector3Int vector3Int2 = new Pipliz.Vector3Int(impactPoint + Vector3.one * num);
		for (int i = vector3Int.x; i <= vector3Int2.x; i++)
		{
			for (int j = vector3Int.y; j <= vector3Int2.y; j++)
			{
				for (int k = vector3Int.z; k <= vector3Int2.z; k++)
				{
					Pipliz.Vector3Int vector3Int3 = new Pipliz.Vector3Int(i, j, k);
					if (World.TryGetTypeAt(vector3Int3, out ItemTypes.ItemType val) && val.ItemIndex != airType && val.ItemIndex != BuiltinBlocks.Indices.water && !HasCategorySafe(val, "essential") && !HasCategorySafe(val, "job") && !HasBehaviourSafe(val, "banner") && !(Vector3.Distance(vector3Int3.Vector + Vector3.one * 0.5f, impactPoint) > radius))
					{
						ServerManager.TryChangeBlock(vector3Int3, airType);
					}
				}
			}
		}
	}

	private Vector3 ResolveImpactPoint(ActiveSession session, Vector3 eyePosition, Vector3 aimDirection)
	{
		aimDirection = ((aimDirection.sqrMagnitude > 0.001f) ? aimDirection.normalized : Vector3.down);
		Vector3 vector = ResolveFreeAimTargetPoint(session, eyePosition, aimDirection);
		if (TryResolveLineOfFireImpact(eyePosition, vector, out Vector3 impactPoint))
		{
			return impactPoint;
		}
		return ClampImpactAboveSurface(vector);
	}

	private Vector3 ResolveFreeAimTargetPoint(ActiveSession session, Vector3 eyePosition, Vector3 aimDirection)
	{
		float num = session.OrbitCenter.y + config.Camera.WorldReticleHeightOffset;
		if (aimDirection.y < -0.001f)
		{
			float num2 = (num - eyePosition.y) / aimDirection.y;
			if (num2 > 0f)
			{
				Vector3 vector = eyePosition + aimDirection * num2;
				Vector2 vector2 = new Vector2(vector.x - session.OrbitCenter.x, vector.z - session.OrbitCenter.z);
				float num3 = Mathf.Max(240f, session.BaseOrbitRadius * 2.6f);
				if (vector2.sqrMagnitude > num3 * num3)
				{
					vector2 = vector2.normalized * num3;
				}
				return new Vector3(session.OrbitCenter.x + vector2.x, num, session.OrbitCenter.z + vector2.y);
			}
		}
		return eyePosition + aimDirection * 640f;
	}

	private bool TryResolveLineOfFireImpact(Vector3 origin, Vector3 targetPoint, out Vector3 impactPoint)
	{
		Vector3 vector = targetPoint - origin;
		float magnitude = vector.magnitude;
		if (magnitude < 0.05f)
		{
			impactPoint = targetPoint;
			return false;
		}
		Vector3 normalized = vector / magnitude;
		float step = 0.24f;
		float maxDistance = Mathf.Max(0.1f, magnitude + 1.2f);
		Vector3 lastFreePoint = origin + normalized * 0.08f;
		Pipliz.Vector3Int lastFreeCell = ToVoxelPosition(lastFreePoint);
		for (float distance = 0.18f; distance <= maxDistance; distance += step)
		{
			Vector3 sample = origin + normalized * distance;
			Pipliz.Vector3Int sampleCell = ToVoxelPosition(sample);
			if (World.TryGetTypeAt(sampleCell, out ItemTypes.ItemType type) && IsSolidImpactBlock(type))
			{
				Vector3 surfaceNormal = ResolveTraversalSurfaceNormal(lastFreeCell, sampleCell, normalized);
				impactPoint = ResolveRaycastSurfaceImpact(surfaceNormal, sampleCell, lastFreePoint, sample, normalized);
				return true;
			}
			lastFreePoint = sample;
			lastFreeCell = sampleCell;
		}
		impactPoint = default(Vector3);
		return false;
	}

	private Vector3 ResolveRaycastSurfaceImpact(Vector3 voxelSideNormal, Pipliz.Vector3Int voxelPositionHit, Vector3 lastFreePoint, Vector3 solidSamplePoint, Vector3 rayDirection)
	{
		Vector3 exactHitPosition = Vector3.Lerp(lastFreePoint, solidSamplePoint, 0.55f);
		if (voxelSideNormal.y > 0.5f)
		{
			Vector3 impactPoint = exactHitPosition - rayDirection * 0.04f + voxelSideNormal * 0.08f;
			impactPoint.y = Mathf.Max(impactPoint.y, (float)voxelPositionHit.y + 1.02f);
			return EnsureTopImpactPointExposed(impactPoint);
		}
		return ResolveSideSurfaceImpact(exactHitPosition, voxelSideNormal);
	}

	private static Pipliz.Vector3Int ToVoxelPosition(Vector3 point)
	{
		return new Pipliz.Vector3Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y), Mathf.FloorToInt(point.z));
	}

	private static Vector3 ResolveTraversalSurfaceNormal(Pipliz.Vector3Int lastFreeCell, Pipliz.Vector3Int solidCell, Vector3 rayDirection)
	{
		Pipliz.Vector3Int delta = lastFreeCell - solidCell;
		if (delta.x != 0 || delta.y != 0 || delta.z != 0)
		{
			if (System.Math.Abs(delta.y) >= System.Math.Abs(delta.x) && System.Math.Abs(delta.y) >= System.Math.Abs(delta.z))
			{
				return delta.y > 0 ? Vector3.up : Vector3.down;
			}
			if (System.Math.Abs(delta.x) >= System.Math.Abs(delta.z))
			{
				return delta.x > 0 ? Vector3.right : Vector3.left;
			}
			return delta.z > 0 ? Vector3.forward : Vector3.back;
		}
		Vector3 inverse = -rayDirection.normalized;
		if (Mathf.Abs(inverse.y) >= Mathf.Abs(inverse.x) && Mathf.Abs(inverse.y) >= Mathf.Abs(inverse.z))
		{
			return inverse.y > 0f ? Vector3.up : Vector3.down;
		}
		if (Mathf.Abs(inverse.x) >= Mathf.Abs(inverse.z))
		{
			return inverse.x > 0f ? Vector3.right : Vector3.left;
		}
		return inverse.z > 0f ? Vector3.forward : Vector3.back;
	}

	private Vector3 EnsureTopImpactPointExposed(Vector3 impactPoint)
	{
		Vector3 vector = impactPoint;
		for (int i = 0; i < 6; i++)
		{
			if (!IsSolidImpactPosition(vector) && !IsSolidImpactPosition(vector + Vector3.up * 0.24f))
			{
				return ClampImpactAboveSurface(vector);
			}
			vector += Vector3.up * 0.14f;
		}
		return ClampImpactAboveSurface(impactPoint + Vector3.up * 0.72f);
	}

	private Vector3 ResolveSideSurfaceImpact(Vector3 exactHitPosition, Vector3 surfaceNormal)
	{
		Vector3 normalized = ((surfaceNormal.sqrMagnitude > 0.001f) ? surfaceNormal.normalized : Vector3.up);
		Vector3 vector = exactHitPosition + normalized * 0.42f + Vector3.up * 0.34f;
		for (int i = 0; i < 12; i++)
		{
			if (!IsSolidImpactPosition(vector) && !IsSolidImpactPosition(vector + Vector3.up * 0.24f) && !IsSolidImpactPosition(vector + normalized * 0.18f))
			{
				return vector;
			}
			vector += normalized * 0.18f + Vector3.up * 0.14f;
		}
		return exactHitPosition + normalized * 1.6f + Vector3.up * 1.1f;
	}

	private Vector3 ClampImpactAboveSurface(Vector3 impactPoint)
	{
		int num = Mathf.FloorToInt(impactPoint.x);
		int num2 = Mathf.FloorToInt(impactPoint.z);
		int num3 = Mathf.CeilToInt(impactPoint.y) + 4;
		int num4 = Mathf.FloorToInt(impactPoint.y) - 18;
		float num5 = float.NegativeInfinity;
		for (int i = -1; i <= 1; i++)
		{
			for (int j = -1; j <= 1; j++)
			{
				for (int num6 = num3; num6 >= num4; num6--)
				{
					if (World.TryGetTypeAt(new Pipliz.Vector3Int(num + i, num6, num2 + j), out ItemTypes.ItemType val) && IsSolidImpactBlock(val))
					{
						if ((float)num6 + 1f > num5)
						{
							num5 = (float)num6 + 1.02f;
						}
						break;
					}
				}
			}
		}
		if (float.IsNegativeInfinity(num5))
		{
			return impactPoint;
		}
		impactPoint.y = Mathf.Max(impactPoint.y, num5);
		return impactPoint;
	}

	private bool IsSolidImpactPosition(Vector3 position)
	{
		return World.TryGetTypeAt(new Pipliz.Vector3Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y), Mathf.FloorToInt(position.z)), out ItemTypes.ItemType val) && IsSolidImpactBlock(val);
	}

	private bool IsSolidImpactBlock(ItemTypes.ItemType type)
	{
		if (type != null && type.ItemIndex != airType && type.ItemIndex != BuiltinBlocks.Indices.water)
		{
			return !type.PathingImpactAsAir;
		}
		return false;
	}

	private static Vector3 GetVoxelSideNormal(VoxelSide side)
	{
		return side switch
		{
			VoxelSide.xMin => Vector3.left, 
			VoxelSide.xPlus => Vector3.right, 
			VoxelSide.yMin => Vector3.down, 
			VoxelSide.yPlus => Vector3.up, 
			VoxelSide.zMin => Vector3.back, 
			VoxelSide.zPlus => Vector3.forward, 
			_ => Vector3.up, 
		};
	}

	private static Vector3 ResolveFireOrigin(ActiveSession session, Vector3 impactPoint, Ac130WeaponConfig weapon)
	{
		Quaternion obj = session.Player?.Rotation ?? session.CurrentVehicleRotation;
		Vector3 normalized = (obj * Vector3.forward).normalized;
		Vector3 normalized2 = (obj * Vector3.up).normalized;
		Vector3 normalized3 = (obj * Vector3.right).normalized;
		float t = Mathf.Clamp01(weapon.Radius / 10f);
		Vector3 vector = (session.Player?.PositionCamera ?? session.CurrentVehiclePosition) + normalized * Mathf.Lerp(4.2f, 7.2f, t) - normalized2 * Mathf.Lerp(3.8f, 6.2f, t) - normalized3 * Mathf.Lerp(0.8f, 1.8f, t);
		Vector3 normalized4 = (impactPoint - vector).normalized;
		if (normalized4.sqrMagnitude > 0.001f)
		{
			vector += normalized4 * Mathf.Lerp(1.2f, 2.8f, t);
		}
		return vector;
	}

	private void RegisterGunshipVehicleType()
	{
		if (gunshipVehicleType.type == 0)
		{
			string text = (string.IsNullOrEmpty(config.Session.VehicleMeshPath) ? DefaultVehicleMeshPath : config.Session.VehicleMeshPath);
			if (!Path.IsPathRooted(text))
			{
				text = Path.GetFullPath(Path.Combine(modRoot, text));
			}
			ECachedFileType type = ItemTypesServer.MeshFileTypeFromPath(text);
			FileTable.FileID meshFileID = ServerManager.FileTable.StartLoading(text, type);
			gunshipVehicleSettings = new MeshedObjectTypeSettings("betterac130.gunshipvehicle", meshFileID, string.IsNullOrEmpty(config.Session.VehicleTextureMapping) ? "neutral" : config.Session.VehicleTextureMapping)
			{
				colliders = new List<ObjectCollider>(),
				sendUpdateRadius = System.Math.Max(100, config.Session.VehicleSendRadius),
				InterpolationLooseness = Mathf.Max(0.05f, config.Session.VehicleInterpolationLooseness),
				TimeoutSeconds = Mathf.Max(0.5f, config.Session.VehicleTimeoutSeconds)
			};
			gunshipVehicleType = MeshedObjectType.Register(gunshipVehicleSettings);
		}
	}

	private void EnsureChatCommandsRegistered()
	{
		if (!chatCommandsRegistered)
		{
			CommandManager.RegisterCommand(new Ac130ResetCommand());
			chatCommandsRegistered = true;
		}
	}

	private void LoadRuntimeState()
	{
		if (config == null)
		{
			return;
		}
		string text = runtimeStatePath;
		if (string.IsNullOrEmpty(text) || !File.Exists(text))
		{
			return;
		}
		try
		{
			JObject jObject = JObject.Parse(File.ReadAllText(text));
			bool? value = jObject.Value<bool?>("allowBlockDamage");
			if (value.HasValue)
			{
				config.Session.AllowBlockDamage = value.Value;
			}
		}
		catch
		{
		}
	}

	private void SaveRuntimeState()
	{
		if (config == null)
		{
			return;
		}
		string text = runtimeStatePath;
		if (string.IsNullOrEmpty(text))
		{
			string text2 = modRoot;
			if (string.IsNullOrEmpty(text2))
			{
				text2 = ResolveModRoot();
			}
			text = Path.Combine(text2, RuntimeStateFileName);
			runtimeStatePath = text;
		}
		try
		{
			JObject jObject = new JObject
			{
				["allowBlockDamage"] = config.Session.AllowBlockDamage
			};
			File.WriteAllText(text, jObject.ToString());
		}
		catch
		{
		}
	}

	private void UpdateVehicleOrbit(ActiveSession session, double now)
	{
		float num = Mathf.Max(6f, config.Session.OrbitSecondsPerRotation);
		float num2 = (float)System.Math.Max(0.0, now - session.LastOrbitSampleAt);
		session.LastOrbitSampleAt = now;
		session.OrbitAngleRadians += num2 * Mathf.PI * 2f / num;
		session.CurrentVehiclePosition = session.OrbitCenter + ComputeOrbitOffset(session.OrbitAngleRadians, session.OrbitRadius, session.OrbitAltitude);
		session.CurrentVehicleRotation = ComputeOrbitLookRotation(session.CurrentVehiclePosition, session.OrbitCenter, session.OrbitAngleRadians, session.OrbitRadius);
		if (!(now < session.NextVehicleSendAt))
		{
			session.Vehicle.Object.SendMoveToInterpolatedRenderDistance(session.CurrentVehiclePosition, session.CurrentVehicleRotation, gunshipVehicleSettings, config.Session.VehicleUpdateDelayMS);
			session.NextVehicleSendAt = now + (double)config.Session.VehicleUpdateDelayMS / 1000.0;
		}
	}

	private void EndSession(ActiveSession session, string message)
	{
		activeSessions.Remove(session.PlayerID);
		if (session.Player == null)
		{
			return;
		}
		StopAllSessionAudio(session);
		RestoreInventory(session);
		if (session.Player.ConnectionState == Players.EConnectionState.Connected)
		{
		ClearHud(session.Player);
			MeshedObjectManager.Detach(session.Player);
			if (session.Vehicle.IsValid)
			{
				session.Vehicle.Object.SendRemoval(session.CurrentVehiclePosition, gunshipVehicleSettings);
			}
			Teleport.TeleportTo(session.Player, ResolveSafeExitPosition(session.TerminalPosition));
			if (!string.IsNullOrEmpty(message))
			{
				Chat.Send(session.Player, message);
			}
		}
	}

	private static float ResolveStartAngle(Players.Player player, Vector3 orbitCenter)
	{
		Vector3 vector = player.PositionVoxelStanding.Vector + new Vector3(0.5f, 0f, 0.5f) - orbitCenter;
		vector.y = 0f;
		if (vector.sqrMagnitude < 0.001f)
		{
			return 0f;
		}
		return Mathf.Atan2(vector.z, vector.x);
	}

	private static Vector3 ComputeOrbitOffset(float angleRadians, float radius, float altitude)
	{
		return new Vector3(Mathf.Cos(angleRadians) * radius, altitude, Mathf.Sin(angleRadians) * radius);
	}

	private static Vector3 ComputeOrbitTangent(float angleRadians)
	{
		Vector3 vector = new Vector3(0f - Mathf.Sin(angleRadians), 0f, Mathf.Cos(angleRadians));
		if (!(vector.sqrMagnitude < 0.001f))
		{
			return vector.normalized;
		}
		return Vector3.forward;
	}

	private static Quaternion ComputeOrbitLookRotation(Vector3 currentPosition, Vector3 orbitCenter, float angleRadians, float orbitRadius)
	{
		Vector3 vector = ComputeOrbitTangent(angleRadians) * Mathf.Max(8f, orbitRadius * 0.22f);
		Vector3 vector2 = orbitCenter + vector + Vector3.up * 2f - currentPosition;
		if (vector2.sqrMagnitude < 0.001f)
		{
			vector2 = Vector3.down;
		}
		return Quaternion.LookRotation(vector2.normalized, Vector3.up);
	}

	private void SnapshotInventory(ActiveSession session)
	{
		for (int i = 0; i < session.OriginalInventory.Length; i++)
		{
			session.OriginalInventory[i] = session.Player.Inventory.GetAt(i);
		}
	}

	private void EquipSessionInventory(ActiveSession session)
	{
		session.Player.Inventory.SetAt(0, new InventoryItem(weapon25Type));
		session.Player.Inventory.SetAt(1, new InventoryItem(weapon40Type));
		session.Player.Inventory.SetAt(2, new InventoryItem(weapon105Type));
		session.Player.Inventory.SetAt(3, new InventoryItem(zoomOutType));
		session.Player.Inventory.SetAt(4, new InventoryItem(zoomInType));
		session.Player.Inventory.SetAt(5, InventoryItem.Empty);
		session.Player.Inventory.SetAt(6, InventoryItem.Empty);
		session.Player.Inventory.SetAt(7, new InventoryItem(exitSignalType));
		session.Player.SendStockpileInventory(session.Player.ActiveColonyGroup?.Stockpile);
	}

	private void RestoreInventory(ActiveSession session)
	{
		for (int i = 0; i < session.OriginalInventory.Length; i++)
		{
			session.Player.Inventory.SetAt(i, session.OriginalInventory[i]);
		}
		session.Player.SendStockpileInventory(session.Player.ActiveColonyGroup?.Stockpile);
	}

	private void UpdateHud(ActiveSession session, double now, bool force = false)
	{
		if (session.Player.ConnectionState == Players.EConnectionState.Connected && (force || !(now < session.NextHudUpdateAt)))
		{
			session.NextHudUpdateAt = now + 0.12;
			float f = session.CurrentVehiclePosition.y - session.OrbitCenter.y;
			double seconds = System.Math.Max(0.0, session.EndsAtSeconds - now);
			int num = ResolveRotationArrowOffset(session);
			if (IsFlirEnabled(session.PlayerID))
			{
				UIManager.AddorUpdateUIImage(GetHudKey(session.PlayerID, "flir_fullscreen"), UIElementDisplayType.Global, "betterac130.flir.fullscreen", new Pipliz.Vector3Int(0, 0, 0), AnchorPresets.MiddleCenter, session.Player);
			}
			else
			{
				UIManager.RemoveUIImage(GetHudKey(session.PlayerID, "flir_fullscreen"), session.Player);
			}
			UIManager.AddorUpdateUIImage(GetHudKey(session.PlayerID, "overlay_top"), UIElementDisplayType.Global, "betterac130.hud.top", HudLayout.OverlayTop, AnchorPresets.TopCenter, session.Player);
			UIManager.AddorUpdateUIImage(GetHudKey(session.PlayerID, "overlay_bottom"), UIElementDisplayType.Global, "betterac130.hud.bottom", HudLayout.OverlayBottom, AnchorPresets.BottonCenter, session.Player);
			UIManager.AddorUpdateUIImage(GetHudKey(session.PlayerID, "overlay_left"), UIElementDisplayType.Global, "betterac130.hud.left", HudLayout.OverlayLeft, AnchorPresets.MiddleLeft, session.Player);
			UIManager.AddorUpdateUIImage(GetHudKey(session.PlayerID, "overlay_right"), UIElementDisplayType.Global, "betterac130.hud.right", HudLayout.OverlayRight, AnchorPresets.MiddleRight, session.Player);
			UIManager.RemoveUILabel(GetHudKey(session.PlayerID, "mode"), session.Player);
			UIManager.RemoveUILabel(GetHudKey(session.PlayerID, "status"), session.Player);
			UIManager.RemoveUILabel(GetHudKey(session.PlayerID, "weapons"), session.Player);
			UIManager.AddorUpdateUILabel(GetHudKey(session.PlayerID, "alt_value"), UIElementDisplayType.Global, Mathf.RoundToInt(f).ToString(CultureInfo.InvariantCulture), HudLayout.AltValue, AnchorPresets.TopCenter, 160f, session.Player, 64f, FontType.AverageSans, "#F2F2F2", TextAlignmentOptions.Center);
			UIManager.AddorUpdateUILabel(GetHudKey(session.PlayerID, "orbit_value"), UIElementDisplayType.Global, Mathf.RoundToInt(session.OrbitRadius).ToString(CultureInfo.InvariantCulture), HudLayout.OrbitValue, AnchorPresets.TopCenter, 160f, session.Player, 64f, FontType.AverageSans, "#F2F2F2", TextAlignmentOptions.Center);
			UIManager.AddorUpdateUILabel(GetHudKey(session.PlayerID, "time_value"), UIElementDisplayType.Global, seconds.ToString("00.00", CultureInfo.InvariantCulture), HudLayout.TimeValue, AnchorPresets.TopCenter, 210f, session.Player, 64f, FontType.AverageSans, "#F2F2F2", TextAlignmentOptions.Center);
			UIManager.AddorUpdateUILabel(GetHudKey(session.PlayerID, "ammo25"), UIElementDisplayType.Global, FormatAmmoHud(session.Weapon25Ammo, session.SelectedType == weapon25Type), HudLayout.Ammo25, AnchorPresets.BottonCenter, 180f, session.Player, 52f, FontType.AverageSans, "#F2F2F2", TextAlignmentOptions.Center);
			UIManager.AddorUpdateUILabel(GetHudKey(session.PlayerID, "ammo40"), UIElementDisplayType.Global, FormatAmmoHud(session.Weapon40Ammo, session.SelectedType == weapon40Type), HudLayout.Ammo40, AnchorPresets.BottonCenter, 180f, session.Player, 52f, FontType.AverageSans, "#F2F2F2", TextAlignmentOptions.Center);
			UIManager.AddorUpdateUILabel(GetHudKey(session.PlayerID, "ammo105"), UIElementDisplayType.Global, FormatAmmoHud(session.Weapon105Ammo, session.SelectedType == weapon105Type), HudLayout.Ammo105, AnchorPresets.BottonCenter, 180f, session.Player, 52f, FontType.AverageSans, "#F2F2F2", TextAlignmentOptions.Center);
			UIManager.AddorUpdateUILabel(GetHudKey(session.PlayerID, "rotation_arrow"), UIElementDisplayType.Global, "^", HudLayout.RotationArrowBase + new Pipliz.Vector3Int(num, 0, 0), AnchorPresets.TopCenter, 60f, session.Player, 38f, FontType.AverageSans, "#F2F2F2", TextAlignmentOptions.Center);
			string displayText2 = ((session.KillFeedUntilAt > now && session.LastKillCount > 0) ? ("+" + session.LastKillCount) : string.Empty);
			UIManager.AddorUpdateUILabel(GetHudKey(session.PlayerID, "kill"), UIElementDisplayType.Global, displayText2, HudLayout.KillFeed, AnchorPresets.BottomLeft, 220f, session.Player, 28f, FontType.AverageSans, "#F2F2F2");
		}
	}

	private void ClearHud(Players.Player player)
	{
		string[] array = new string[15]
		{
			"flir_fullscreen", "overlay_top", "overlay_bottom", "overlay_left", "overlay_right", "mode", "status", "weapons", "kill", "alt_value", "orbit_value", "time_value", "ammo25", "ammo40", "ammo105"
		};
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i] == "flir_fullscreen" || array[i].StartsWith("overlay_", StringComparison.Ordinal))
			{
				UIManager.RemoveUIImage(GetHudKey(player.ID.ID, array[i]), player);
			}
			else
			{
				UIManager.RemoveUILabel(GetHudKey(player.ID.ID, array[i]), player);
			}
		}
		UIManager.RemoveUILabel(GetHudKey(player.ID.ID, "rotation_arrow"), player);
	}

	private static string FormatAmmoHud(int ammo, bool active)
	{
		string text = ammo.ToString("000", CultureInfo.InvariantCulture);
		if (!active)
		{
			return text;
		}
		return "[" + text + "]";
	}

	private static int ResolveRotationArrowOffset(ActiveSession session)
	{
		float y = session.CurrentVehicleRotation.eulerAngles.y;
		float value = Mathf.DeltaAngle(0f, y) / 180f;
		return Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * 205f);
	}

	private void TryPlayWeaponFireAudio(Players.Player player, ActiveSession session, Ac130WeaponSlot slot, Ac130WeaponConfig weapon)
	{
		if (!weapon.UseLoopedFireAudio && !string.IsNullOrEmpty(weapon.FireAudio))
		{
			double secondsSinceStartDoubleThisFrame = Pipliz.Time.SecondsSinceStartDoubleThisFrame;
			ref double nextAudioAtRef = ref GetNextAudioAtRef(session, slot);
			if (!(secondsSinceStartDoubleThisFrame < nextAudioAtRef))
			{
				double num = System.Math.Max(0.0, weapon.FireAudioCadenceSeconds);
				nextAudioAtRef = secondsSinceStartDoubleThisFrame + num;
				PlayDirectAudio(player, weapon.FireAudio);
			}
		}
	}

	private static ref double GetNextAudioAtRef(ActiveSession session, Ac130WeaponSlot slot)
	{
		if (slot == Ac130WeaponSlot.Weapon40mm)
		{
			return ref session.Weapon40NextAudioAt;
		}
		if (slot == Ac130WeaponSlot.Weapon105mm)
		{
			return ref session.Weapon105NextAudioAt;
		}
		return ref session.Weapon25NextAudioAt;
	}

	private void StartSessionAudio(ActiveSession session)
	{
		PlayDirectAudio(session.Player, config.Audio.StartClip);
		StartBackgroundLoop(session);
		UpdateWeaponLoop(session);
	}

	private void StartBackgroundLoop(ActiveSession session)
	{
		StopDirectedLoop(session, ref session.BackgroundLoopID);
		if (!string.IsNullOrEmpty(config.Audio.BackgroundLoopClip) && config.Audio.BackgroundLoopLengthMs > 0)
		{
			StartDirectedLoop(session, config.Audio.BackgroundLoopClip, config.Audio.BackgroundLoopLengthMs, ref session.BackgroundLoopID);
		}
	}

	private void UpdateWeaponLoop(ActiveSession session)
	{
		string text = null;
		int clipLengthMs = 0;
		if (!IsFireInputPaused(session) && session.AutoFireType != 0 && session.SelectedType == session.AutoFireType)
		{
			Ac130WeaponSlot slot;
			Ac130WeaponConfig ac130WeaponConfig = TryResolveWeapon(session.AutoFireType, out slot);
			if (ac130WeaponConfig != null && ac130WeaponConfig.UseLoopedFireAudio && !string.IsNullOrEmpty(ac130WeaponConfig.FireAudio) && ac130WeaponConfig.FireLoopLengthMs > 0)
			{
				text = ac130WeaponConfig.FireAudio;
				clipLengthMs = ac130WeaponConfig.FireLoopLengthMs;
			}
		}
		if (!string.Equals(session.WeaponLoopClipName, text, StringComparison.Ordinal) || !session.WeaponLoopID.IsValid)
		{
			StopDirectedLoop(session, ref session.WeaponLoopID);
			session.WeaponLoopClipName = null;
			if (!string.IsNullOrEmpty(text))
			{
				StartDirectedLoop(session, text, clipLengthMs, ref session.WeaponLoopID);
				session.WeaponLoopClipName = text;
			}
		}
	}

	private void StopAllSessionAudio(ActiveSession session)
	{
		StopDirectedLoop(session, ref session.WeaponLoopID);
		session.WeaponLoopClipName = null;
		StopDirectedLoop(session, ref session.BackgroundLoopID);
	}

	private void StartDirectedLoop(ActiveSession session, string clipName, int clipLengthMs, ref AudioManager.AudioClipPlayingID loopId)
	{
		if (session?.Player != null && session.Player.IsConnectionReady && !string.IsNullOrEmpty(clipName) && clipLengthMs > 0 && AudioManager.TryGetIndex(clipName, out var idx))
		{
			loopId = AudioManager.AudioClipPlayingID.GenerateNew();
			AudioManager.SendPlayLoopPacket(session.Player, Vector3.zero, idx, loopId, 0, clipLengthMs);
		}
	}

	private void StopDirectedLoop(ActiveSession session, ref AudioManager.AudioClipPlayingID loopId)
	{
		if (loopId.IsValid)
		{
			SendDirectedStopPacket(session?.Player, loopId);
			loopId = default(AudioManager.AudioClipPlayingID);
		}
	}

	private static void PlayDirectAudio(Players.Player player, string clipName)
	{
		if (player != null && player.IsConnectionReady && !string.IsNullOrEmpty(clipName))
		{
			AudioManager.SendAudio(player, clipName);
		}
	}

	private static void SendDirectedStopPacket(Players.Player player, AudioManager.AudioClipPlayingID loopId)
	{
		if (player == null || !player.IsConnectionReady || !loopId.IsValid)
		{
			return;
		}
		using ByteBuilder byteBuilder = ByteBuilder.Get();
		byteBuilder.Write(ClientMessageType.AudioStop);
		byteBuilder.WriteVariable(loopId.ID);
		NetworkWrapper.Send(byteBuilder, player);
	}

	private string GetSelectedHudLabel(ushort type)
	{
		if (type == weapon40Type)
		{
			return "40MM";
		}
		if (type == weapon105Type)
		{
			return "105MM";
		}
		if (type == zoomOutType)
		{
			return "ZOOM-";
		}
		if (type == zoomInType)
		{
			return "ZOOM+";
		}
		if (type == exitSignalType)
		{
			return "EXIT";
		}
		return "NONE";
	}

	private static string GetHudKey(Players.PlayerIDShort playerId, string suffix)
	{
		return "betterac130." + playerId.ID + "." + suffix;
	}

	private bool IsFlirEnabled(Players.PlayerIDShort playerId)
	{
		return flirEnabledPlayers.Contains(playerId);
	}

	private void RefreshFlirOverlay(Players.Player player, bool enabled)
	{
		if (player == null)
		{
			return;
		}
		if (activeSessions.TryGetValue(player.ID.ID, out var value))
		{
			if (enabled)
			{
				UIManager.AddorUpdateUIImage(GetHudKey(value.PlayerID, "flir_fullscreen"), UIElementDisplayType.Global, "betterac130.flir.fullscreen", new Pipliz.Vector3Int(0, 0, 0), AnchorPresets.MiddleCenter, player);
			}
			else
			{
				UIManager.RemoveUIImage(GetHudKey(value.PlayerID, "flir_fullscreen"), player);
			}
			value.NextHudUpdateAt = 0.0;
		}
	}

	private void UpdateSessionPauseStates(double now, bool isPaused)
	{
		foreach (KeyValuePair<Players.PlayerIDShort, ActiveSession> activeSession in activeSessions)
		{
			ActiveSession value = activeSession.Value;
			bool flag = value.Player != null && value.Player.PauseRequestState;
			if (flag != value.LastPauseRequestState)
			{
				value.LastPauseRequestState = flag;
				value.NextHudUpdateAt = 0.0;
			}
			if (isPaused)
			{
				if (!value.IsPaused)
				{
					value.IsPaused = true;
					value.PauseStartedAt = now;
					value.NextHudUpdateAt = 0.0;
					UpdateWeaponLoop(value);
				}
			}
			else if (value.IsPaused)
			{
				double num = System.Math.Max(0.0, now - value.PauseStartedAt);
				value.IsPaused = false;
				value.PauseStartedAt = 0.0;
				ApplyPauseOffset(value, num);
				value.NextHudUpdateAt = 0.0;
				UpdateWeaponLoop(value);
			}
		}
	}

	private void ApplyPauseOffset(ActiveSession session, double pauseDuration)
	{
		if (!(pauseDuration > 0.0))
		{
			return;
		}
		session.StartedAtSeconds += pauseDuration;
		session.EndsAtSeconds += pauseDuration;
		session.LastOrbitSampleAt += pauseDuration;
		session.NextVehicleSendAt += pauseDuration;
		session.Weapon25NextShotAt += pauseDuration;
		session.Weapon40NextShotAt += pauseDuration;
		session.Weapon105NextShotAt += pauseDuration;
		session.Weapon25NextAudioAt += pauseDuration;
		session.Weapon40NextAudioAt += pauseDuration;
		session.Weapon105NextAudioAt += pauseDuration;
		session.KillFeedUntilAt += pauseDuration;
		for (int i = 0; i < pendingStrikes.Count; i++)
		{
			PendingStrike pendingStrike = pendingStrikes[i];
			if (pendingStrike.Session != session)
			{
				continue;
			}
			pendingStrike.FiredAtSeconds += pauseDuration;
			if (pendingStrike.ImpactAtSeconds > 0.0)
			{
				pendingStrike.ImpactAtSeconds += pauseDuration;
			}
			if (pendingStrike.NextImpactBurstAt > 0.0)
			{
				pendingStrike.NextImpactBurstAt += pauseDuration;
			}
		}
	}

	private static bool IsFireInputPaused(ActiveSession session)
	{
		return session != null && (session.IsPaused || (session.Player != null && session.Player.PauseRequestState));
	}

	private static void RegisterHudImage(Dictionary<string, string> imagesToLoad, string imageKey, string imagePath)
	{
		if (imagesToLoad != null && !string.IsNullOrEmpty(imageKey) && !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
		{
			imagesToLoad[imageKey] = imagePath;
		}
	}

	private Vector3 ResolveSafeExitPosition(Pipliz.Vector3Int terminalPosition)
	{
		Pipliz.Vector3Int[] array = new Pipliz.Vector3Int[5]
		{
			terminalPosition.Add(0, 1, 0),
			terminalPosition.Add(1, 1, 0),
			terminalPosition.Add(-1, 1, 0),
			terminalPosition.Add(0, 1, 1),
			terminalPosition.Add(0, 1, -1)
		};
		for (int i = 0; i < array.Length; i++)
		{
			Pipliz.Vector3Int feet = array[i];
			if (IsWalkableExit(feet.Add(0, -1, 0), head: feet.Add(0, 1, 0), feet: feet))
			{
				return feet.Vector + new Vector3(0.5f, 0f, 0.5f);
			}
		}
		return terminalPosition.Vector + new Vector3(0.5f, 1.05f, 0.5f);
	}

	private static bool IsWalkableExit(Pipliz.Vector3Int ground, Pipliz.Vector3Int feet, Pipliz.Vector3Int head)
	{
		if (!World.TryGetTypeAt(ground, out ItemTypes.ItemType val) || !World.TryGetTypeAt(feet, out ItemTypes.ItemType val2) || !World.TryGetTypeAt(head, out ItemTypes.ItemType val3))
		{
			return false;
		}
		if (val.ItemIndex != BuiltinBlocks.Indices.air && val.ItemIndex != BuiltinBlocks.Indices.water && val2.ItemIndex == BuiltinBlocks.Indices.air)
		{
			return val3.ItemIndex == BuiltinBlocks.Indices.air;
		}
		return false;
	}

	private ushort ResolveRotatedType(string name, ushort fallback)
	{
		if (string.IsNullOrEmpty(name))
		{
			return fallback;
		}
		return ItemTypes.IndexLookup.GetIndex(name);
	}

	private static bool HasCategorySafe(ItemTypes.ItemType type, string category)
	{
		List<string> list = type?.Categories;
		if (list == null)
		{
			return false;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (string.Equals(list[i], category, StringComparison.InvariantCulture))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasBehaviourSafe(ItemTypes.ItemType type, string behaviour)
	{
		ItemTypes.ItemType.Behaviour[] array = type?.AttachedBehaviours;
		if (array == null)
		{
			return false;
		}
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].Identifier == behaviour)
			{
				return true;
			}
		}
		return false;
	}

	private string ResolveModRoot()
	{
		for (int i = 0; i < ModLoader.LoadedMods.Count; i++)
		{
			ModLoader.ModDescription modDescription = ModLoader.LoadedMods[i];
			if (string.Equals(modDescription.name, "Better AC130", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(modDescription.assemblyPath))
			{
				return Path.GetDirectoryName(modDescription.assemblyPath);
			}
		}
		throw new InvalidOperationException("BetterAC130 server runtime could not resolve its mod root.");
	}
}
