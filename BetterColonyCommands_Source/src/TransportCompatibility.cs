using ModLoaderInterfaces;
using Pipliz;
using Shared;
using Transport;
using Transport.Elevator;
using Transport.Rail;

namespace ColonyCommands
{
	public static class TransportCompatibility
	{
		private static readonly Glider.Callbacks GliderCallbacks = new Glider.Callbacks();

		public static void OnPlayerClicked(Players.Player player, PlayerClickedData click)
		{
			if (click == null ||
				click.IsHoldingButton ||
				click.ClickSource == PlayerClickedData.EClickSource.TopDown ||
				click.ClickType != PlayerClickedData.EClickType.Right ||
				click.HitType != PlayerClickedData.EHitType.Block)
			{
				return;
			}

			if (click.ConsumedType != PlayerClickedData.EConsumedType.Not &&
				click.ConsumedType != PlayerClickedData.EConsumedType.Reserved)
			{
				return;
			}

			PlayerClickedData.VoxelHit voxelHit = click.GetVoxelHit();
			ItemTypes.ItemType type = ItemTypes.GetType(voxelHit.TypeHit);
			if (type == null)
			{
				return;
			}

			if (RailManager.Instance != null && RailManager.Instance.TryGetGatewayConfig(type, out _))
			{
				EnsureReserved(click);
				Log.Write($"ColonyCommands: forwarding rail click for {type.Name}");
				RailManager.Instance.OnPlayerClicked(player, click);
				return;
			}

			if (ElevatorManager.Instance != null && ElevatorManager.Instance.TryGetGatewayConfig(type, out _))
			{
				EnsureReserved(click);
				Log.Write($"ColonyCommands: forwarding elevator click for {type.Name}");
				ElevatorManager.Instance.OnPlayerClicked(player, click);
				return;
			}

			if (type.HasBehaviour("gliderlauncher", out _))
			{
				EnsureReserved(click);
				Log.Write($"ColonyCommands: forwarding glider click for {type.Name}");
				GliderCallbacks.OnPlayerClicked(player, click);
			}
		}

		private static void EnsureReserved(PlayerClickedData click)
		{
			if (click.ConsumedType == PlayerClickedData.EConsumedType.Not)
			{
				click.ConsumedType = PlayerClickedData.EConsumedType.Reserved;
			}
		}
	}
}
