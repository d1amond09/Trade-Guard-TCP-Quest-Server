namespace Trade_Guard_TCP_Quest_Server;

public class EnemyWave
{
	public Vector3 TriggerPosition { get; set; } 
	public List<EnemyState> Enemies { get; set; } 
	public bool IsTriggered { get; set; } = false; 
}
