using System;

namespace BetterAC130.Shared;

public static class Ac130Balance
{
	public static float GetDurationSeconds(Ac130Config config, Ac130UpgradeState state)
	{
		float num = config.Session.DurationSeconds;
		if (state != null)
		{
			num += config.Upgrades.Duration.DurationSecondsPerLevel * (float)state.DurationLevel;
		}
		return num;
	}

	public static double GetCooldownDays(Ac130Config config, Ac130UpgradeState state)
	{
		double num = config.Session.CooldownGameDays;
		if (state != null)
		{
			num -= config.Upgrades.Cooldown.CooldownDaysReductionPerLevel * (double)state.CooldownLevel;
		}
		return Math.Max(0.5, num);
	}

	public static int GetAmmoForWeapon(Ac130Config config, Ac130UpgradeState state, Ac130WeaponSlot slot)
	{
		Ac130WeaponConfig weapon = config.GetWeapon(slot);
		if (weapon == null)
		{
			return 0;
		}
		float num = 1f;
		if (state != null)
		{
			num += config.Upgrades.Ammo.AmmoBonusPercentPerLevel * (float)state.AmmoLevel;
		}
		return Math.Max(1, (int)Math.Round((float)weapon.BaseAmmo * num, MidpointRounding.AwayFromZero));
	}

	public static float GetWeaponCooldownSeconds(Ac130Config config, Ac130UpgradeState state, Ac130WeaponSlot slot)
	{
		Ac130WeaponConfig weapon = config.GetWeapon(slot);
		if (weapon == null)
		{
			return 999f;
		}
		float num = weapon.CooldownSeconds;
		if (state != null)
		{
			float num2 = config.Upgrades.FireRate.CooldownReductionPercentPerLevel * (float)state.FireRateLevel;
			num *= Math.Max(0.2f, 1f - num2);
		}
		return Math.Max(0.03f, num);
	}

	public static float GetWeaponDamage(Ac130Config config, Ac130UpgradeState state, Ac130WeaponSlot slot)
	{
		return GetWeaponDamage(config.GetWeapon(slot), config, state);
	}

	public static float GetWeaponDamage(Ac130WeaponConfig weapon, Ac130Config config, Ac130UpgradeState state)
	{
		if (weapon == null)
		{
			return 0f;
		}
		float num = weapon.Damage;
		if (state != null)
		{
			num *= 1f + config.Upgrades.Damage.DamageBonusPercentPerLevel * (float)state.DamageLevel;
		}
		return Math.Max(1f, num);
	}

	public static float GetMonsterSplashRadius(Ac130Config config, Ac130UpgradeState state, Ac130WeaponSlot slot)
	{
		return GetMonsterSplashRadius(config.GetWeapon(slot), config, state);
	}

	public static float GetMonsterSplashRadius(Ac130WeaponConfig weapon, Ac130Config config, Ac130UpgradeState state)
	{
		if (weapon == null)
		{
			return 0f;
		}
		return Math.Max(0.5f, weapon.Radius);
	}
}
