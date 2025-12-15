
namespace Trade_Guard_TCP_Quest_Server;

public class EnemyState
{
	public int Id { get; set; }
	public Vector3 Position { get; set; }
	public int Health { get; set; }
	public DateTime NextAttackTime { get; set; } = DateTime.Now;
	public bool IsFrozen { get; set; } = false;
	public DateTime UnfreezeTime { get; set; }
	public int EnemyType { get; set; } = 0;
}
