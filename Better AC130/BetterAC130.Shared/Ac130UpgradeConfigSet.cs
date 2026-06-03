namespace BetterAC130.Shared;

public sealed class Ac130UpgradeConfigSet
{
	public Ac130DurationUpgradeConfig Duration = new Ac130DurationUpgradeConfig();

	public Ac130CooldownUpgradeConfig Cooldown = new Ac130CooldownUpgradeConfig();

	public Ac130AmmoUpgradeConfig Ammo = new Ac130AmmoUpgradeConfig();

	public Ac130FireRateUpgradeConfig FireRate = new Ac130FireRateUpgradeConfig();

	public Ac130DamageUpgradeConfig Damage = new Ac130DamageUpgradeConfig();
}
