namespace Trade_Guard_TCP_Quest_Server;

public class PlayerState
{
	public string Id { get; set; }
	public string Username { get; set; }
	public Vector3 Position { get; set; }
	public Vector3 Rotation { get; set; }

	public int Health { get; set; } = 100;
	public int MaxHealth { get; set; } = 100;

	public int Shield { get; set; } = 20; 
	public int MaxShield { get; set; } = 20;
	public DateTime LastDamageTime { get; set; }

	public int StrengthLevel { get; set; } = 0; 
	public int HealthPotions { get; set; } = 0;
	public int FreezePotions { get; set; } = 0;

	public int Points { get; set; } = 150;
	public bool IsReady { get; set; } = false;

	public bool IsDead { get; set; } = false;
	public DateTime RespawnTime { get; set; }
}
