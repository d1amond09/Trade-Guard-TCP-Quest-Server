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
	private List<EnemyWave> pendingWaves = new List<EnemyWave>();

	private int totalWavesCount = 0;
	private int currentWaveCount = 0;

	private Timer gameLoopTimer;
	private GameState currentGameState = GameState.WaitingForPlayers;

	private int merchantMaxHealth = 500;
	private int merchantHealth;

	private List<Vector3> merchantPath = new List<Vector3>();
	private int currentWaypointIndex = 0; 
	private Random random = new Random();

	private readonly Dictionary<string, int> itemPrices = new()
	{
		{ "StrengthUpgrade", 50 },
		{ "HealthPotion", 20 },    
		{ "ShieldUpgrade", 40 }, 
		{ "FreezePotion", 60 }
	};

	private int mapSeed;

	public GameServer()
	{
		tcpListener = new TcpListener(IPAddress.Any, 8888);
		listenThread = new Thread(ListenForClients);
		listenThread.Start();
		Console.WriteLine("Сервер запущен. Ожидание подключений...");

		destinationPosition = new Vector3(25, 0, 0);

		ResetServerState();
		
		gameLoopTimer = new Timer(GameLoop, null, 0, 100);
	}

	private void ResetServerState()
	{
		Console.WriteLine(">>> СБРОС СОСТОЯНИЯ СЕРВЕРА (Нет игроков) <<<");

		nextPlayerId = 1;

		currentGameState = GameState.WaitingForPlayers;

		merchantHealth = merchantMaxHealth;

		mapSeed = random.Next();
		Console.WriteLine($"Генерация карты с сидом: {mapSeed}");

		GenerateRandomPath();

		enemyStates.Clear();
		SpawnEnemiesAlongPath();


		gameLoopTimer?.Change(0, 100);
	}

	private void SpawnEnemiesAlongPath()
	{
		enemyStates.Clear(); 
		pendingWaves.Clear(); 

		int enemyCounter = 100;

		for (int i = 1; i < merchantPath.Count - 1; i++)
		{
			Vector3 waypoint = merchantPath[i];

			if (random.Next(0, 10) > 2)
			{
				EnemyWave wave = new EnemyWave
				{
					TriggerPosition = waypoint,
					Enemies = new List<EnemyState>()
				};

				int enemiesInGroup = random.Next(2, 5);

				for (int j = 0; j < enemiesInGroup; j++)
				{
					float offsetX = random.Next(-10, 11);
					float offsetZ = random.Next(-10, 11);
					int randomType = random.Next(0, 2);
					wave.Enemies.Add(new EnemyState
					{
						Id = enemyCounter++,
						Position = new Vector3(waypoint.x + offsetX, 0, waypoint.z + offsetZ),
						Health = 100,
						NextAttackTime = DateTime.Now,
						EnemyType = randomType
					});
				}

				pendingWaves.Add(wave);
			}
		}
		totalWavesCount = pendingWaves.Count;
		currentWaveCount = 0;

		Console.WriteLine($"Всего волн запланировано: {totalWavesCount}");
	}

	private void CheckWaveSpawns()
	{
		float activationRadius = 15.0f;

		foreach (var wave in pendingWaves)
		{
			if (!wave.IsTriggered)
			{
				if (Vector3.Distance(merchantPosition, wave.TriggerPosition) <= activationRadius)
				{
					wave.IsTriggered = true;
					currentWaveCount++;

					lock (enemyStates)
					{
						foreach (var enemy in wave.Enemies)
						{
							enemyStates.Add(enemy);

							string msg = string.Format(CultureInfo.InvariantCulture,
								"ENEMY_SPAWN:{0},{1},{2},{3},{4},{5}",
								enemy.Id, enemy.Position.x, enemy.Position.y, enemy.Position.z, enemy.Health, enemy.EnemyType);
							BroadcastMessage(msg);
						}
					}
					BroadcastMessage($"WAVE_STATUS:{currentWaveCount},{totalWavesCount}");
					Console.WriteLine($"Волна {currentWaveCount}/{totalWavesCount} активирована!");
				}
			}
		}
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
				Points = 150,
				IsReady = false
			};
			playerStates.Add(playerId, newPlayer);
			clientHandler.PlayerId = playerId;

			clientHandler.SendMessage($"YOUR_ID:{playerId}");
			clientHandler.SendMessage($"MAP_SEED:{mapSeed}");

			string spawnMessage = string.Format(CultureInfo.InvariantCulture,
				"PLAYER_SPAWN:{0},{1},{2},{3},{4},{5},{6},{7}",
				newPlayer.Id, newPlayer.Username,
				newPlayer.Position.x, newPlayer.Position.y, newPlayer.Position.z,
				newPlayer.Rotation.x, newPlayer.Rotation.y, newPlayer.Rotation.z);
			clientHandler.SendMessage(spawnMessage);

			clientHandler.SendMessage($"PLAYER_STATUS:{playerId},{newPlayer.Health},{newPlayer.Shield}");
			clientHandler.SendMessage($"POINTS:{newPlayer.Points}");
			clientHandler.SendMessage(string.Format(CultureInfo.InvariantCulture, "MERCHANT_POS:{0},{1},{2}", merchantPosition.x, merchantPosition.y, merchantPosition.z));
			clientHandler.SendMessage(string.Format(CultureInfo.InvariantCulture, "DESTINATION_POS:{0},{1},{2}", destinationPosition.x, destinationPosition.y, destinationPosition.z));
			clientHandler.SendMessage($"MERCHANT_HEALTH:{merchantHealth},{merchantMaxHealth}");

			lock (enemyStates)
			{
				foreach (var enemy in enemyStates)
				{
					clientHandler.SendMessage(string.Format(CultureInfo.InvariantCulture, "ENEMY_SPAWN:{0},{1},{2},{3},{4},{5}", enemy.Id, enemy.Position.x, enemy.Position.y, enemy.Position.z, enemy.Health, enemy.EnemyType));
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
					clientHandler.SendMessage($"PLAYER_STATUS:{existingPlayer.Id},{existingPlayer.Health},{existingPlayer.Shield}");
				}
			}

			BroadcastMessage(spawnMessage, clientHandler);
			BroadcastMessage($"PLAYER_STATUS:{newPlayer.Id},{newPlayer.Health},{newPlayer.Shield}", clientHandler);

			Console.WriteLine($"Игрок {username} ({playerId}) присоединился.");
		}
	}

	public void ProcessShopAction(string playerId, string command, string itemType)
	{
		lock (playerStates)
		{
			if (playerStates.TryGetValue(playerId, out PlayerState player))
			{
				if (itemPrices.TryGetValue(itemType, out int price))
				{
					if (command == "BUY")
					{
						if (player.Points >= price)
						{
							player.Points -= price;
							ApplyItem(player, itemType, 1);
							SendShopUpdate(playerId, player, itemType, "BOUGHT");
						}
					}

					else if (command == "SELL")
					{
						if (CanSell(player, itemType))
						{
							player.Points += price;
							ApplyItem(player, itemType, -1);
							SendShopUpdate(playerId, player, itemType, "SOLD");
						}
					}
				}
			}
		}
	}

	private void SendShopUpdate(string playerId, PlayerState player, string itemType, string action)
	{
		ClientHandler client = connectedClients.Find(c => c.PlayerId == playerId);
		if (client != null)
		{
			client.SendMessage($"POINTS:{player.Points}");
			int shieldUpgrades = (player.MaxShield - 20) / 20;
			client.SendMessage($"SHOP_STATE:{player.StrengthLevel},{shieldUpgrades},{player.HealthPotions},{player.FreezePotions}");
			Console.WriteLine($"Игрок {playerId} {action} {itemType}.");
		}
	}

	private void ApplyItem(PlayerState player, string itemType, int amount)
	{
		switch (itemType)
		{
			case "StrengthUpgrade": player.StrengthLevel += amount; break;
			case "HealthPotion": player.HealthPotions += amount; break;
			case "ShieldUpgrade":
				if (amount > 0) player.MaxShield += 20;
				else player.MaxShield -= 20;
				player.Shield = player.MaxShield;
				break;
			case "FreezePotion": player.FreezePotions += amount; break;
		}
		if (player.StrengthLevel < 0) player.StrengthLevel = 0;
		if (player.MaxShield < 20) player.MaxShield = 20; 
	}

	private bool CanSell(PlayerState player, string itemType)
	{
		switch (itemType)
		{
			case "StrengthUpgrade": return player.StrengthLevel > 0;
			case "HealthPotion": return player.HealthPotions > 0;
			case "ShieldUpgrade": return player.MaxShield > 20; 
			case "FreezePotion": return player.FreezePotions > 0;
			default: return false;
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
				Console.WriteLine($"Игрок {playerId} покинул игру. Осталось игроков: {playerStates.Count}");
				if (playerStates.Count == 0)
				{
					ResetServerState();
				}
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
		PlayerState attacker = null;
		lock (playerStates)
		{
			if (!playerStates.TryGetValue(attackingPlayerId, out attacker))
			{
				return; 
			}
		}

		lock (enemyStates)
		{
			EnemyState targetEnemy = enemyStates.Find(e => e.Id == targetEnemyId);
			if (targetEnemy != null)
			{
				int damage = 10 + (attacker.StrengthLevel * 10);

				targetEnemy.Health -= damage;

				BroadcastMessage($"ENEMY_HEALTH_UPDATE:{targetEnemy.Id},{targetEnemy.Health}");

				Console.WriteLine($"Игрок {attackingPlayerId} ударил врага {targetEnemyId} на {damage} урона.");

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
		if (currentGameState != GameState.Playing) return;


		RegenerateShields();
		UpdateMerchantAI();

		CheckWaveSpawns();

		UpdateEnemiesAI();
		CheckGameEndConditions();
	}

	private void RegenerateShields()
	{
		lock (playerStates)
		{
			foreach (var player in playerStates.Values)
			{
				if ((DateTime.Now - player.LastDamageTime).TotalSeconds > 5.0 && player.Shield < player.MaxShield)
				{
					player.Shield++;
					if (player.Shield > player.MaxShield) player.Shield = player.MaxShield;
					BroadcastMessage($"PLAYER_STATUS:{player.Id},{player.Health},{player.Shield}");
				}
			}
		}
	}

	private void UpdateMerchantAI()
	{
		bool isEnemyNear = false;
		lock (enemyStates)
		{
			foreach (var enemy in enemyStates)
			{
				if (Vector3.Distance(merchantPosition, enemy.Position) < 8f && !enemy.IsFrozen)
				{
					isEnemyNear = true;
					break;
				}
			}
		}

		if (!isEnemyNear && merchantPath.Count > 0 && currentWaypointIndex < merchantPath.Count)
		{
			Vector3 targetWaypoint = merchantPath[currentWaypointIndex];
			merchantPosition = Vector3.MoveTowards(merchantPosition, targetWaypoint, 2.0f * 0.1f); 

			if (Vector3.Distance(merchantPosition, targetWaypoint) < 0.5f)
			{
				currentWaypointIndex++;
				Console.WriteLine($"Торговец достиг точки {currentWaypointIndex} из {merchantPath.Count}");
			}

			BroadcastMessage(string.Format(CultureInfo.InvariantCulture, "MERCHANT_POS:{0},{1},{2}", merchantPosition.x, merchantPosition.y, merchantPosition.z));
		}
	}

	private void UpdateEnemiesAI()
	{
		lock (enemyStates)
		{
			foreach (var enemy in enemyStates)
			{
				if (enemy.IsFrozen)
				{
					if (DateTime.Now >= enemy.UnfreezeTime)
					{
						enemy.IsFrozen = false;
						BroadcastMessage($"ENEMY_FREEZE:{enemy.Id},0");
					}
					else
					{
						continue;
					}
				}

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

					float stopDistance = 2f;

					if (targetId.Equals("Merchant"))
					{
						stopDistance = 4f;
					}
					if (minDistance <= stopDistance)
					{
						if (DateTime.Now >= enemy.NextAttackTime)
						{
							enemy.NextAttackTime = DateTime.Now.AddSeconds(3.0);

							BroadcastMessage($"ENEMY_ANIM:{enemy.Id},Attack");

							if (targetId.Equals("Merchant"))
							{
								merchantHealth -= 5;
								BroadcastMessage($"MERCHANT_HEALTH:{merchantHealth},{merchantMaxHealth}");
								if (merchantHealth <= 0)
								{
									currentGameState = GameState.Defeat;
									BroadcastMessage("GAME_END:DEFEAT");
									gameLoopTimer.Change(Timeout.Infinite, Timeout.Infinite);
								}
							}
							else
							{
								DamagePlayer(targetId, 5);
							}
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

	private void FreezeEnemies()
	{
		lock (enemyStates)
		{
			Console.WriteLine("Использовано зелье заморозки! Все враги остановлены.");
			foreach (var enemy in enemyStates)
			{
				enemy.UnfreezeTime = DateTime.Now.AddSeconds(5.0);

				if (!enemy.IsFrozen)
				{
					enemy.IsFrozen = true;
					BroadcastMessage($"ENEMY_FREEZE:{enemy.Id},1");
				}
			}
		} 
	}

	public void DamagePlayer(string playerId, int damage)
	{
		lock (playerStates)
		{
			if (playerStates.TryGetValue(playerId, out PlayerState? player))
			{
				player.LastDamageTime = DateTime.Now; 

				int damageRemaining = damage;

				if (player.Shield > 0)
				{
					if (player.Shield >= damageRemaining)
					{
						player.Shield -= damageRemaining;
						damageRemaining = 0;
					}
					else
					{
						damageRemaining -= player.Shield;
						player.Shield = 0;
					}
				}

				if (damageRemaining > 0)
				{
					player.Health -= damageRemaining;
					if (player.Health < 0) player.Health = 0;
				}

				BroadcastMessage($"PLAYER_STATUS:{playerId},{player.Health},{player.Shield}");

				Console.WriteLine($"Игрок {playerId}: Урон {damage}. Стало: HP={player.Health}, Shield={player.Shield}");

				if (player.Health <= 0) Console.WriteLine($"Игрок {player.Username} погиб!");
			}
		}
	}

	public void ProcessUseItem(string playerId, string itemType)
	{
		lock (playerStates)
		{
			PlayerState player = playerStates[playerId];

			if (itemType == "HealthPotion" && player.HealthPotions > 0)
			{
				player.HealthPotions--;
				player.Health += 50;
				if (player.Health > player.MaxHealth) player.Health = player.MaxHealth;

				ClientHandler client = connectedClients.Find(c => c.PlayerId == playerId);
				client.SendMessage($"INVENTORY:{player.HealthPotions},{player.FreezePotions}");
				BroadcastMessage($"PLAYER_STATUS:{playerId},{player.Health},{player.Shield}");
			}
			else if (itemType == "FreezePotion" && player.FreezePotions > 0)
			{
				player.FreezePotions--;
				FreezeEnemies();

				ClientHandler client = connectedClients.Find(c => c.PlayerId == playerId);
				client.SendMessage($"INVENTORY:{player.HealthPotions},{player.FreezePotions}");
			}
		}
	}

	public void ProcessPlayerReady(string playerId)
	{
		lock (playerStates)
		{
			if (playerStates.ContainsKey(playerId))
			{
				playerStates[playerId].IsReady = true;
				Console.WriteLine($"Игрок {playerId} готов!");

				CheckStartGame();
			}
		}
	}

	private void CheckStartGame()
	{
		if (playerStates.Count > 0 && playerStates.Values.All(p => p.IsReady))
		{
			currentGameState = GameState.Playing;
			Console.WriteLine("Все игроки готовы. Игра началась!");
			BroadcastMessage("GAME_START");
			BroadcastMessage($"WAVE_STATUS:{currentWaveCount},{totalWavesCount}");
		}
	}

	private void CheckGameEndConditions()
	{
		bool isAtFinalDestination = currentWaypointIndex >= merchantPath.Count 
			|| Vector3.Distance(merchantPosition, merchantPath[merchantPath.Count - 1]) < 1.0f;

		if (isAtFinalDestination)
		{
			currentGameState = GameState.Victory;
			BroadcastMessage("GAME_END:VICTORY");
			Console.WriteLine("Победа! Торговец прошел весь маршрут.");
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


	private void GenerateRandomPath()
	{
		merchantPath.Clear();
		currentWaypointIndex = 0;

		Vector3 startPoint = new Vector3(5, 0, 0);
		merchantPath.Add(startPoint);
		merchantPosition = startPoint;

		float finalX = 300f;

		int segments = 20;

		float startX = 5f;
		float stepX = (finalX - startX) / segments;

		Random pathRandom = new Random(mapSeed); 

		float pathWidth = pathRandom.Next(15, 36);

		float direction = (pathRandom.Next(0, 2) == 0) ? 1f : -1f;

		for (int i = 1; i < segments; i++)
		{
			float x = startX + (stepX * i);

			float progress = (float)i / segments;

			float waveZ = (float)Math.Sin(progress * Math.PI * 1.5f) * pathWidth * direction;

			float noiseZ = pathRandom.Next(-3, 4);

			float z = waveZ + noiseZ;

			merchantPath.Add(new Vector3(x, 0, z));
		}

		destinationPosition = new Vector3(finalX, 0, 0);
		merchantPath.Add(destinationPosition);

		Console.WriteLine($"Сгенерирован ГЛАДКИЙ маршрут. Амплитуда: {pathWidth}");
	}
}