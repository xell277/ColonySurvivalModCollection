using System;
using System.Collections.Generic;

namespace BetterAC130.Shared;

public sealed class Ac130Config
{
	public Ac130CameraConfig Camera = new Ac130CameraConfig();

	public Ac130SessionConfig Session = new Ac130SessionConfig();

	public Ac130AudioConfig Audio = new Ac130AudioConfig();

	public Ac130UpgradeConfigSet Upgrades = new Ac130UpgradeConfigSet();

	public List<Ac130WeaponConfig> Weapons = new List<Ac130WeaponConfig>();

	public Ac130WeaponConfig GetWeapon(Ac130WeaponSlot slot)
	{
		if (Weapons == null || Weapons.Count == 0)
		{
			return null;
		}
		string b = slot switch
		{
			Ac130WeaponSlot.Weapon25mm => "25mm", 
			Ac130WeaponSlot.Weapon40mm => "40mm", 
			Ac130WeaponSlot.Weapon105mm => "105mm", 
			_ => "25mm", 
		};
		for (int i = 0; i < Weapons.Count; i++)
		{
			if (string.Equals(Weapons[i].Id, b, StringComparison.OrdinalIgnoreCase))
			{
				return Weapons[i];
			}
		}
		return Weapons[0];
	}
}
