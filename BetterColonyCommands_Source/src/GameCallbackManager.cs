using System.Collections.Generic;
using ModLoaderInterfaces;
using NPC;
using Recipes;
using Pipliz;
using Shared;
using UnityEngine;

namespace ColonyCommands {

	public class GameCallbackManager:
		IAfterItemTypesDefined,
		IAfterWorldLoad,
		IAfterModsLoaded,
		IOnAssemblyLoaded,
		IOnPlayerClicked,
		IOnTryChangeBlock,
		IOnNPCHit,
		IOnPlayerConnectedLate,
		IOnPlayerMoved2,
		IOnAutoSaveWorld,
		IOnPlayerDisconnected,
		IOnQuit
	{

		public void AfterItemTypesDefined()
		{
			AntiGrief.AfterItemTypesDefined();
		}


		public void AfterWorldLoad()
		{
			Announcements.AfterWorldLoad();
			AntiGrief.AfterWorldLoad();
			JailManager.Load();
            TravelManager.Load();
			WarManager.Load();
            ChatColors.LoadChatColors();
            RoleplayManager.Load();
            BannedPlayerManager.Load();
			ActivityTracker.Load();
		}


		public void AfterModsLoaded(List<ModLoader.ModDescription> mods)
		{
			AntiGrief.AfterModsLoaded(mods);
		}


		public void OnAssemblyLoaded(string path)
		{
			AntiGrief.OnAssemblyLoaded(path);
		}


		public void OnTryChangeBlock(ModLoader.OnTryChangeBlockData userData)
		{
			AntiGrief.OnTryChangeBlock(userData);
		}


		public void OnPlayerClicked(Players.Player player, PlayerClickedData click)
		{
			TransportCompatibility.OnPlayerClicked(player, click);
		}


		public void OnPlayerConnectedLate(Players.Player player)
		{
			Announcements.OnPlayerConnectedLate(player);
			AntiGrief.OnPlayerConnectedLate(player);
			ActivityTracker.OnPlayerConnectedLate(player);
			JailManager.OnPlayerConnectedLate(player);
		}


		public void OnPlayerMoved2(Players.Player player, Vector3 oldStandingPosition, Vector3 oldCameraPosition)
		{
			JailManager.OnPlayerMoved(player, player.PositionStanding);
		}


		public void OnNPCHit(NPC.NPCBase npc, ModLoader.OnHitData data)
		{
			AntiGrief.OnNPCHit(npc, data);
		}


		public void OnAutoSaveWorld()
		{
			ActivityTracker.OnAutoSaveWorld();
		}


		public void OnPlayerDisconnected(Players.Player player)
		{
			ActivityTracker.OnPlayerDisconnected(player);
			JailManager.OnPlayerDisconnected(player);
		}


		public void OnQuit()
		{
			ActivityTracker.OnQuit();
		}

	}

}
