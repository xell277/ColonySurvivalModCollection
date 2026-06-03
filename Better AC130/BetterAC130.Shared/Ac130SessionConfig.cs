namespace BetterAC130.Shared;

public sealed class Ac130SessionConfig
{
	public float DurationSeconds = 30f;

	public double CooldownGameDays = 10.0;

	public bool AllowBlockDamage;

	public float BlockDamageRadiusMultiplier = 0.7f;

	public float OrbitRadius = 48f;

	public float OrbitAltitude = 82f;

	public float OrbitSecondsPerRotation = 18f;

	public int VehicleUpdateDelayMS = 80;

	public string VehicleMeshPath = "../../baseconfig/meshes/glider.ply";

	public string VehicleTextureMapping = "neutral";

	public int VehicleSendRadius = 450;

	public float VehicleInterpolationLooseness = 1.2f;

	public float VehicleTimeoutSeconds = 2.5f;
}
