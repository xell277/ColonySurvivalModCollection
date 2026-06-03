namespace BetterAC130.Shared;

public sealed class Ac130WeaponConfig
{
	public string Id = "25mm";

	public string DisplayName = "25MM";

	public string HudCountLabel = "RDS";

	public float Damage = 55f;

	public float Radius = 2.6f;

	public float CooldownSeconds = 0.2f;

	public int BaseAmmo = 180;

	public float ExplosionStrength = 4f;

	public float SmokeMultiplier = 0.25f;

	public float ParticleSize = 0.18f;

	public float TrailFadeSeconds = 0.1f;

	public float ProjectileTravelSeconds = 0.06f;

	public int ProjectileTrailSegments = 1;

	public int ImpactBurstCount = 1;

	public float ImpactBurstIntervalSeconds = 0.05f;

	public float ImpactBurstJitterRadius = 0.2f;

	public float ImpactSmokeSeconds = 0.15f;

	public string FireAudio = "handcannon";

	public float FireAudioCadenceSeconds;

	public bool UseLoopedFireAudio;

	public int FireLoopLengthMs;

	public string ImpactAudio = "bomb";

	public float MuzzleFlashSeconds = 0.06f;
}
