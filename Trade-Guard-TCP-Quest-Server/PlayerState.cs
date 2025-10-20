using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Trade_Guard_TCP_Quest_Server;

public class PlayerState
{
	public string Id { get; set; }
	public string Username { get; set; }
	public Vector3 Position { get; set; }
	public Vector3 Rotation { get; set; }
	public int Health { get; set; }
	public List<string> Equipment { get; set; } 
}
