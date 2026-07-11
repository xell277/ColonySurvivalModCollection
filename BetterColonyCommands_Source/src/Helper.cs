using UnityEngine;
using Chatting;
using MeshedObjects;
using Pipliz;
using Shared.Networking;

namespace ColonyCommands {

	public static class Helper
	{

		public static void TeleportPlayer(Players.Player target, Vector3 position, bool force = false)
		{
			// avoid teleporting while mounted
			if (MeshedObjectManager.HasVehicle(target)) {
				if (!force) {
					Chat.Send(target, "Please dismount before teleporting");
					return;
				} else {
					MeshedObjectManager.Detach(target);
				}
			}

			using (ByteBuilder byteBuilder = ByteBuilder.Get()) {
				byteBuilder.Write(ClientMessageType.ReceivePosition);
				byteBuilder.Write(position);
				NetworkWrapper.Send(byteBuilder, target);
			}
		}

	}

}

