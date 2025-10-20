namespace Trade_Guard_TCP_Quest_Server;

public class Vector3(float x, float y, float z)
{
	public static Vector3 Zero => new(0, 0, 0);

	public float x = x, y = y, z = z;

	public static float Distance(Vector3 a, Vector3 b)
	{
		float dx = a.x - b.x;
		float dy = a.y - b.y;
		float dz = a.z - b.z;
		return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
	}

	public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
	{
		float dist = Distance(current, target);
		if (dist <= maxDistanceDelta || dist == 0)
		{
			return target;
		}
		return current + (target - current).Normalize() * maxDistanceDelta;
	}

	public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
	public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
	public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);

	public float Magnitude() => (float)Math.Sqrt(x * x + y * y + z * z);
	public Vector3 Normalize() => this * (1f / Magnitude());
}