namespace BetterAC130.Shared;

public abstract class Ac130UpgradeConfig
{
	public int MaxLevel = 4;

	public long BaseCost = 1500L;

	public long CostStep = 1000L;

	public long GetCostForLevel(int level)
	{
		return BaseCost + CostStep * level;
	}
}
