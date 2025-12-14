using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Trade_Guard_TCP_Quest_Server;

public class ClientHandler
{
	private TcpClient client;
	private NetworkStream stream;
	private GameServer server;
	public string PlayerId { get; set; }

	public ClientHandler(TcpClient client, GameServer server)
	{
		this.client = client;
		this.server = server;
		stream = client.GetStream();
	}

	public void HandleClientComm()
	{
		byte[] buffer = new byte[4096];
		int bytesRead;

		try
		{
			while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
			{
				string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
				Console.WriteLine($"Получено от клиента ({PlayerId ?? "new"}): {data.Trim()}");
				ProcessClientMessage(data);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Ошибка в обработчике клиента {PlayerId}: {ex.Message}");
		}
		finally
		{
			if (!string.IsNullOrEmpty(PlayerId))
			{
				server.RemovePlayer(PlayerId);
			}
			client.Close();
		}
	}

	private void ProcessClientMessage(string rawMessage)
	{
		string[] messages = rawMessage.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);

		foreach (var message in messages)
		{
			string[] parts = message.Split(':');
			string command = parts[0];

			switch (command)
			{
				case "JOIN":
					server.AddPlayer(parts[1], this);
					break;
				case "READY":
					if (!string.IsNullOrEmpty(PlayerId))
						server.ProcessPlayerReady(PlayerId);
					break;
				case "PLAYER_ANIM":
					if (!string.IsNullOrEmpty(PlayerId) && parts.Length > 1)
					{
						string animTrigger = parts[1]; 
						server.BroadcastMessage($"PLAYER_ANIM:{PlayerId},{animTrigger}", this);
					}
					break;
				case "MOVE":
					if (!string.IsNullOrEmpty(PlayerId) && parts.Length > 1)
					{
						string[] posAndRot = parts[1].Split(',');
						if (posAndRot.Length == 6)
						{
							try
							{
								float x = float.Parse(posAndRot[0], CultureInfo.InvariantCulture);
								float y = float.Parse(posAndRot[1], CultureInfo.InvariantCulture);
								float z = float.Parse(posAndRot[2], CultureInfo.InvariantCulture);
								float rotX = float.Parse(posAndRot[3], CultureInfo.InvariantCulture);
								float rotY = float.Parse(posAndRot[4], CultureInfo.InvariantCulture);
								float rotZ = float.Parse(posAndRot[5], CultureInfo.InvariantCulture);
								server.UpdatePlayerPosition(PlayerId, x, y, z, rotX, rotY, rotZ);
							}
							catch (FormatException ex)
							{
								Console.WriteLine($"Ошибка парсинга MOVE от {PlayerId}: {ex.Message}");
							}
						}
					}
					break;
				case "ATTACK":
					if (!string.IsNullOrEmpty(PlayerId) && parts.Length > 1)
					{
						server.ProcessPlayerAction(PlayerId, command, new string[] { parts[1] });
					}
					break;
				case "USE_ITEM":
					server.ProcessUseItem(PlayerId, parts[1]);
					break;
				case "CHAT":
					if (!string.IsNullOrEmpty(PlayerId) && parts.Length > 1)
					{
						server.BroadcastMessage($"CHAT:{PlayerId}:{parts[1]}");
					}
					break;
				case "MERCHANT_MOVE_REQUEST":
					server.ProcessPlayerAction(PlayerId, command, new string[0]);
					break;
				case "BUY":
					if (!string.IsNullOrEmpty(PlayerId) && parts.Length > 1)
						server.ProcessBuyItem(PlayerId, parts[1]);
					break;
				case "EXIT":
					Console.WriteLine($"Игрок {PlayerId} отправил команду выхода.");
					client.Close();
					return;
				default:
					Console.WriteLine($"Неизвестная команда: {command}");
					break;
			}
		}
	}

	public void SendMessage(string message)
	{
		if (!client.Connected) return;

		byte[] data = Encoding.UTF8.GetBytes(message + "\n");
		try
		{
			stream.Write(data, 0, data.Length);
			stream.Flush();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Ошибка при отправке сообщения клиенту {PlayerId}: {ex.Message}");
		}
	}
}	