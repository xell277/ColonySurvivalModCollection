namespace BetterAC130.Shared;

public sealed class Ac130UpgradeState
{
	public int DurationLevel;

	public int CooldownLevel;

	public int AmmoLevel;

	public int FireRateLevel;

	public int DamageLevel;

	public double LastActivatedDay = -10000.0;

	public Ac130UpgradeState Clone()
	{
		return new Ac130UpgradeState
		{
			DurationLevel = DurationLevel,
			CooldownLevel = CooldownLevel,
			AmmoLevel = AmmoLevel,
			FireRateLevel = FireRateLevel,
			DamageLevel = DamageLevel,
			LastActivatedDay = LastActivatedDay
		};
	}
}
