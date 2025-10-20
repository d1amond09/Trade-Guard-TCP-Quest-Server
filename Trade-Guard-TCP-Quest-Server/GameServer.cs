using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Trade_Guard_TCP_Quest_Server;

public class GameServer
{
	private TcpListener tcpListener;
	private Thread listenThread;
	private List<ClientHandler> connectedClients = [];
	private Dictionary<string, PlayerState> playerStates = [];
	private int nextPlayerId = 1;

	private Vector3 merchantPosition;
	private Vector3 destinationPosition;
	private List<EnemyState> enemyStates = [];

	private Timer gameLoopTimer;
	private GameState currentGameState = GameState.WaitingForPlayers;

	public GameServer()
	{
		tcpListener = new TcpListener(IPAddress.Any, 8888);
		listenThread = new Thread(ListenForClients);
		listenThread.Start();
		Console.WriteLine("Сервер запущен. Ожидание подключений...");

		merchantPosition = new Vector3(5, 0, 0);
		destinationPosition = new Vector3(25, 0, 0);

		enemyStates.Add(new EnemyState { Id = 101, Position = new Vector3(30, 0, 15), Health = 100 });
		enemyStates.Add(new EnemyState { Id = 102, Position = new Vector3(25, 0, -10), Health = 100 });
		enemyStates.Add(new EnemyState { Id = 103, Position = new Vector3(29, 0, -14), Health = 100 });
		
		gameLoopTimer = new Timer(GameLoop, null, 0, 100);
	}

	private void ListenForClients()
	{
		tcpListener.Start();
		while (true)
		{
			try
			{
				TcpClient client = tcpListener.AcceptTcpClient();
				Console.WriteLine("Новый клиент подключился!");
				ClientHandler clientHandler = new (client, this);
				lock (connectedClients)
				{
					connectedClients.Add(clientHandler);
				}
				Thread clientThread = new (clientHandler.HandleClientComm);
				clientThread.Start();
			}
			catch (SocketException ex)
			{
				Console.WriteLine($"Ошибка при приеме клиента: {ex.Message}");
				break;
			}
		}
	}

	public void BroadcastMessage(string message, ClientHandler excludeClient = null)
	{
		lock (connectedClients)
		{
			foreach (var client in connectedClients)
			{
				if (client != excludeClient)
				{
					client.SendMessage(message);
				}
			}
		}
	}

	public void AddPlayer(string username, ClientHandler clientHandler)
	{
		lock (playerStates)
		{
			string playerId = "Player" + nextPlayerId++;
			PlayerState newPlayer = new()
			{
				Id = playerId,
				Username = username,
				Position = new Vector3(0, 0, 0),
				Rotation = new Vector3(0, 0, 0),
				Health = 100,
				Equipment = []
			};
			playerStates.Add(playerId, newPlayer);
			clientHandler.PlayerId = playerId;

			clientHandler.SendMessage($"YOUR_ID:{playerId}");

			string spawnMessage = string.Format(CultureInfo.InvariantCulture,
				"PLAYER_SPAWN:{0},{1},{2},{3},{4},{5},{6},{7}",
				newPlayer.Id, newPlayer.Username,
				newPlayer.Position.x, newPlayer.Position.y, newPlayer.Position.z,
				newPlayer.Rotation.x, newPlayer.Rotation.y, newPlayer.Rotation.z);
			clientHandler.SendMessage(spawnMessage);

			clientHandler.SendMessage($"PLAYER_HEALTH_UPDATE:{playerId},{newPlayer.Health}");

			clientHandler.SendMessage(string.Format(CultureInfo.InvariantCulture, "MERCHANT_POS:{0},{1},{2}", merchantPosition.x, merchantPosition.y, merchantPosition.z));
			clientHandler.SendMessage(string.Format(CultureInfo.InvariantCulture, "DESTINATION_POS:{0},{1},{2}", destinationPosition.x, destinationPosition.y, destinationPosition.z));

			lock (enemyStates)
			{
				foreach (var enemy in enemyStates)
				{
					clientHandler.SendMessage(string.Format(CultureInfo.InvariantCulture, "ENEMY_SPAWN:{0},{1},{2},{3},{4}", enemy.Id, enemy.Position.x, enemy.Position.y, enemy.Position.z, enemy.Health));
				}
			}

			foreach (var existingPlayer in playerStates.Values)
			{
				if (existingPlayer.Id != playerId)
				{
					string existingPlayerSpawn = string.Format(CultureInfo.InvariantCulture,
						"PLAYER_SPAWN:{0},{1},{2},{3},{4},{5},{6},{7}",
						existingPlayer.Id, existingPlayer.Username,
						existingPlayer.Position.x, existingPlayer.Position.y, existingPlayer.Position.z,
						existingPlayer.Rotation.x, existingPlayer.Rotation.y, existingPlayer.Rotation.z);
					clientHandler.SendMessage(existingPlayerSpawn);
					clientHandler.SendMessage($"PLAYER_HEALTH_UPDATE:{existingPlayer.Id},{existingPlayer.Health}");
				}
			}

			BroadcastMessage(spawnMessage, clientHandler);
			BroadcastMessage($"PLAYER_HEALTH_UPDATE:{newPlayer.Id},{newPlayer.Health}", clientHandler);

			Console.WriteLine($"Игрок {username} ({playerId}) присоединился.");
		}
	}


	public void RemovePlayer(string playerId)
	{
		lock (playerStates)
		{
			if (playerStates.ContainsKey(playerId))
			{
				playerStates.Remove(playerId);
				BroadcastMessage($"PLAYER_DESPAWN:{playerId}");
				Console.WriteLine($"Игрок {playerId} покинул игру.");
			}
		}
		lock (connectedClients)
		{
			connectedClients.RemoveAll(c => c.PlayerId == playerId);
		}
	}

	public void UpdatePlayerPosition(string playerId, float x, float y, float z, float rotX, float rotY, float rotZ)
	{
		lock (playerStates)
		{
			if (playerStates.TryGetValue(playerId, out PlayerState? value))
			{
				value.Position = new Vector3(x, y, z);
				value.Rotation = new Vector3(rotX, rotY, rotZ);

				string updateMessage = string.Format(CultureInfo.InvariantCulture,
					"PLAYER_UPDATE:{0},{1},{2},{3},{4},{5},{6}",
					playerId, x, y, z, rotX, rotY, rotZ);

				ClientHandler sender = null;
				lock (connectedClients)
				{
					sender = connectedClients.FirstOrDefault(c => c.PlayerId == playerId);
				}


				Console.WriteLine($"Broadcasting update for {playerId}");
				BroadcastMessage(updateMessage);
			}
		}
	}

	public void UpdatePlayerEquipment(string playerId, List<string> equipment)
	{
		lock (playerStates)
		{
			if (playerStates.ContainsKey(playerId))
			{
				playerStates[playerId].Equipment = equipment;
				BroadcastMessage($"PLAYER_EQUIPMENT_UPDATE:{playerId},{string.Join(",", equipment)}");
			}
		}
	}

	public void ProcessPlayerAction(string playerId, string actionType, string[] args)
	{
		if (actionType == "ATTACK")
		{
			if (args.Length > 0 && int.TryParse(args[0], out int targetEnemyId))
			{
				HandlePlayerAttackEnemy(playerId, targetEnemyId);
			}
		}
		else if (actionType == "MERCHANT_MOVE_REQUEST")
		{
			// Placeholder for now
		}
	}

	private void HandlePlayerAttackEnemy(string attackingPlayerId, int targetEnemyId)
	{
		lock (enemyStates)
		{
			EnemyState targetEnemy = enemyStates.Find(e => e.Id == targetEnemyId);
			if (targetEnemy != null)
			{
				targetEnemy.Health -= 10;
				BroadcastMessage($"ENEMY_HEALTH_UPDATE:{targetEnemy.Id},{targetEnemy.Health}");

				if (targetEnemy.Health <= 0)
				{
					enemyStates.Remove(targetEnemy);
					BroadcastMessage($"ENEMY_DESPAWN:{targetEnemy.Id}");
					Console.WriteLine($"Враг {targetEnemy.Id} уничтожен игроком {attackingPlayerId}.");
				}
			}
		}
	}

	#region Game Loop and AI
	private void GameLoop(object state)
	{
		if (currentGameState != GameState.Playing)
		{
			if (currentGameState == GameState.WaitingForPlayers && playerStates.Count > 0)
			{
				currentGameState = GameState.Playing;
				Console.WriteLine("Игра началась!");
			}
			return;
		}

		UpdateMerchantAI();
		UpdateEnemiesAI();
		CheckGameEndConditions();
	}

	private void UpdateMerchantAI()
	{
		bool isEnemyNear = false;
		lock (enemyStates)
		{
			foreach (var enemy in enemyStates)
			{
				if (Vector3.Distance(merchantPosition, enemy.Position) < 8f)
				{
					isEnemyNear = true;
					break;
				}
			}
		}

		if (!isEnemyNear && Vector3.Distance(merchantPosition, destinationPosition) > 1.0f)
		{
			merchantPosition = Vector3.MoveTowards(merchantPosition, destinationPosition, 2.0f * 0.1f);
			BroadcastMessage(string.Format(CultureInfo.InvariantCulture, "MERCHANT_POS:{0},{1},{2}", merchantPosition.x, merchantPosition.y, merchantPosition.z));
		}
	}

	private void UpdateEnemiesAI()
	{
		lock (enemyStates)
		{
			foreach (var enemy in enemyStates)
			{
				Vector3 targetPosition = Vector3.Zero;
				float minDistance = float.MaxValue;
				string targetId = null;

				if (Vector3.Distance(enemy.Position, merchantPosition) < minDistance)
				{
					minDistance = Vector3.Distance(enemy.Position, merchantPosition);
					targetPosition = merchantPosition;
					targetId = "Merchant";
				}

				lock (playerStates)
				{
					foreach (var player in playerStates.Values.Where(p => p.Health > 0))
					{
						float dist = Vector3.Distance(enemy.Position, player.Position);
						if (dist < minDistance)
						{
							minDistance = dist;
							targetPosition = player.Position;
							targetId = player.Id;
						}
					}
				}

				if (targetId != null)
				{
					if (minDistance < 1.5f)
					{
						if (!targetId.Equals("Merchant"))
						{
							DamagePlayer(targetId, 5);
						}
					}
					else
					{
						enemy.Position = Vector3.MoveTowards(enemy.Position, targetPosition, 3.0f * 0.1f);
						BroadcastMessage(string.Format(CultureInfo.InvariantCulture, "ENEMY_UPDATE:{0},{1},{2},{3}", enemy.Id, enemy.Position.x, enemy.Position.y, enemy.Position.z));
					}
				}
			}
		}
	}

	public void DamagePlayer(string playerId, int damage)
	{
		lock (playerStates)
		{
			if (playerStates.ContainsKey(playerId))
			{
				PlayerState player = playerStates[playerId];
				player.Health -= damage;
				if (player.Health < 0) player.Health = 0;

				BroadcastMessage($"PLAYER_HEALTH_UPDATE:{playerId},{player.Health}");

				if (player.Health <= 0)
				{
					Console.WriteLine($"Игрок {playerId} погиб.");
				}
			}
		}
	}

	private void CheckGameEndConditions()
	{
		if (Vector3.Distance(merchantPosition, destinationPosition) <= 1.0f)
		{
			currentGameState = GameState.Victory;
			BroadcastMessage("GAME_END:VICTORY");
			Console.WriteLine("Победа! Торговец достиг цели.");
			gameLoopTimer.Change(Timeout.Infinite, Timeout.Infinite);
		}

		bool allPlayersDead = playerStates.Count > 0 && playerStates.Values.All(p => p.Health <= 0);

		if (allPlayersDead)
		{
			currentGameState = GameState.Defeat;
			BroadcastMessage("GAME_END:DEFEAT");
			Console.WriteLine("Поражение! Все игроки погибли.");
			gameLoopTimer.Change(Timeout.Infinite, Timeout.Infinite);
		}
	}
	#endregion
}