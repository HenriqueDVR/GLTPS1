using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CoopSession : MonoBehaviour
{
	public enum SessionMode
	{
		Off,
		Hosting,
		Joining
	}

	public enum MessageType : byte
	{
		// host->friend seed + difficulty
		Start = 1,
		// friend->host layout hash
		Ready = 2,
		// player state
		PlayerState = 3,
		// host went back to title
		Menu = 4,
		// friend asks for an item
		TakeItem = 5,
		// item gone (+ did the friend get it)
		ItemTaken = 6,
		// all taken items
		TakenItems = 7,
		// food/water/stats
		Supplies = 8,
		Weather = 9,
		CampfirePlaced = 10,
		CampfireOut = 11,
		// friend's ket fell out of the world, restart
		RestartLevel = 12,
		Barrel = 13,
		Shot = 14,
		Downed = 15,
		Revived = 16,
		Kanazawa = 17,
		ElevatorClosed = 18
	}

	public const int DefaultPort = 27015;

	// bump on protocol changes
	private const string connectionKey = "GLTPS1-coop-12";

	private const int gameplayScene = 1;

	private const int titleScene = 0;

	private const string difficultyKey = "difficultySetting";

	private NetManager manager;

	private NetPeer peer;

	private readonly NetDataWriter writer = new NetDataWriter();

	private readonly List<NetPeer> pendingConnections = new List<NetPeer>();

	private bool previousRunInBackground;

	private ulong hostSeed;

	private uint hostLayoutHash;

	private string status = "";

	private string lastPortDetail;

	private bool autoHost;

	private string autoJoin;

	private bool autoStart;

	public static CoopSession Instance { get; private set; }

	public static bool Active
	{
		get
		{
			if (Instance != null)
			{
				return Instance.Mode != SessionMode.Off;
			}
			return false;
		}
	}

	public static bool InGame
	{
		get
		{
			if (Active && Instance.peer != null)
			{
				return SceneManager.GetActiveScene().buildIndex == gameplayScene;
			}
			return false;
		}
	}

	public SessionMode Mode { get; private set; }

	public bool IsHost => Mode == SessionMode.Hosting;

	public bool Connected => peer != null;

	public int Ping
	{
		get
		{
			if (peer == null)
			{
				return 0;
			}
			return peer.RoundTripTime;
		}
	}

	public int Port { get; private set; } = DefaultPort;

	public PortOpener PortOpener { get; private set; }

	public string Invite { get; private set; }

	public string Status
	{
		get
		{
			return status;
		}
		private set
		{
			if (value != status && value != "")
			{
				Debug.Log("Coop: " + value);
			}
			status = value;
		}
	}

	public string Warning { get; private set; } = "";

	public event Action<NetPacketReader> PlayerStateReceived;

	public event Action<NetPacketReader> SuppliesReceived;

	public static CoopSession EnsureExists()
	{
		if (Instance == null)
		{
			GameObject obj = new GameObject("CoopSession");
			UnityEngine.Object.DontDestroyOnLoad(obj);
			Instance = obj.AddComponent<CoopSession>();
			obj.AddComponent<CoopMenu>();
		}
		return Instance;
	}

	private void Awake()
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		for (int i = 0; i < commandLineArgs.Length; i++)
		{
			string text = ((i + 1 < commandLineArgs.Length) ? commandLineArgs[i + 1] : null);
			if (commandLineArgs[i] == "-coopport" && int.TryParse(text, out var result))
			{
				Port = result;
			}
			// test switches
			else if (commandLineArgs[i] == "-coophost")
			{
				autoHost = true;
			}
			else if (commandLineArgs[i] == "-coopjoin" && text != null)
			{
				autoJoin = text;
			}
			else if (commandLineArgs[i] == "-coopautostart")
			{
				autoStart = true;
			}
		}
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void Start()
	{
		if (autoHost)
		{
			Host();
		}
		else if (autoJoin != null)
		{
			Join(autoJoin);
		}
	}

	// -coopautostart
	private void AutoStartGame()
	{
		SetDifficultySetting setDifficultySetting = UnityEngine.Object.FindObjectOfType<SetDifficultySetting>(includeInactive: true);
		if (setDifficultySetting != null)
		{
			if (setDifficultySetting.save == null)
			{
				setDifficultySetting.save = new Save();
			}
			setDifficultySetting.SetEasyDifficulty();
		}
	}

	private void OnDestroy()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		Leave();
	}

	private void OnApplicationQuit()
	{
		Leave();
	}

	private void Update()
	{
		manager?.PollEvents();
		PortOpener?.Poll();
		if (IsHost && PortOpener != null && PortOpener.Detail != lastPortDetail)
		{
			lastPortDetail = PortOpener.Detail;
			Debug.Log("Coop: " + lastPortDetail);
			RefreshInvite();
		}
	}

	public void Stop()
	{
		Leave();
		Status = "";
		Warning = "";
	}

	public void Host()
	{
		Leave();
		CreateManager();
		if (!manager.Start(Port))
		{
			Status = "Couldn't start hosting: port " + Port + " is in use.";
			Leave();
			return;
		}
		Mode = SessionMode.Hosting;
		Status = "Waiting for your friend to join...";
		Warning = "";
		PortOpener = new PortOpener();
		PortOpener.Open(Port);
		RefreshInvite();
	}

	public void Join(string inviteText)
	{
		List<IPEndPoint> list = CoopInvite.Decode(inviteText, DefaultPort);
		if (list.Count == 0)
		{
			Status = "That invite doesn't look right. Copy it again from your friend.";
			return;
		}
		Leave();
		CreateManager();
		manager.Start();
		Mode = SessionMode.Joining;
		Status = "Connecting...";
		Warning = "";
		foreach (IPEndPoint item in list)
		{
			NetPeer netPeer = manager.Connect(item, connectionKey);
			if (netPeer != null)
			{
				pendingConnections.Add(netPeer);
			}
		}
	}

	public void Leave()
	{
		if (manager != null)
		{
			manager.Stop(sendDisconnectMessages: true);
			manager = null;
		}
		peer = null;
		pendingConnections.Clear();
		PortOpener?.Close();
		PortOpener = null;
		lastPortDetail = null;
		if (Mode != SessionMode.Off)
		{
			Application.runInBackground = previousRunInBackground;
		}
		Mode = SessionMode.Off;
		Invite = null;
		WorldGenerator.RequestedSeed = 0uL;
		hostSeed = 0uL;
		CoopItem.ResetLevel();
		CoopWeather.ResetLevel();
	}

	private void CreateManager()
	{
		EventBasedNetListener eventBasedNetListener = new EventBasedNetListener();
		eventBasedNetListener.ConnectionRequestEvent += OnConnectionRequest;
		eventBasedNetListener.PeerConnectedEvent += OnPeerConnected;
		eventBasedNetListener.PeerDisconnectedEvent += OnPeerDisconnected;
		eventBasedNetListener.NetworkReceiveEvent += OnReceive;
		manager = new NetManager(eventBasedNetListener)
		{
			IPv6Enabled = true,
			// level load can stall for a few secs
			DisconnectTimeout = 20000
		};
		previousRunInBackground = Application.runInBackground;
		Application.runInBackground = true;
	}

	private void RefreshInvite()
	{
		CoopInvite.FindLocalAddresses(out var lanIPv4, out var ipv);
		IPAddress publicIPv = ((PortOpener != null && PortOpener.State == PortOpener.Result.Opened) ? PortOpener.ExternalIP : null);
		Invite = CoopInvite.Encode(Port, publicIPv, lanIPv4, ipv);
#if DUMP_WORLDGEN
		Debug.Log("Coop: invite " + Invite);
#endif
	}

	private void OnConnectionRequest(ConnectionRequest request)
	{
		if (IsHost && peer == null)
		{
			request.AcceptIfKey(connectionKey);
		}
		else
		{
			request.Reject();
		}
	}

	private void OnPeerConnected(NetPeer connected)
	{
		if (peer != null)
		{
			connected.Disconnect();
			return;
		}
		peer = connected;
		foreach (NetPeer pendingConnection in pendingConnections)
		{
			if (pendingConnection != connected)
			{
				pendingConnection.Disconnect();
			}
		}
		pendingConnections.Clear();
		if (IsHost)
		{
			Status = "Your friend joined.";
			if (SceneManager.GetActiveScene().buildIndex == gameplayScene && hostSeed != 0L)
			{
				SendStart();
			}
			else if (autoStart && SceneManager.GetActiveScene().buildIndex == titleScene)
			{
				AutoStartGame();
			}
		}
		else
		{
			Status = "Connected. Waiting for the host to start the game...";
		}
	}

	private void OnPeerDisconnected(NetPeer disconnected, DisconnectInfo info)
	{
		pendingConnections.Remove(disconnected);
		if (disconnected != peer)
		{
			if (!IsHost && peer == null && pendingConnections.Count == 0 && Mode == SessionMode.Joining)
			{
				Status = "Couldn't connect (" + info.Reason.ToString() + "). Check the invite, and that the host is still hosting.";
				Leave();
			}
			return;
		}
		peer = null;
		if (IsHost)
		{
			Status = "Your friend left. Waiting for someone to join...";
			return;
		}
		Status = "Disconnected from the host (" + info.Reason.ToString() + ").";
		Leave();
		if (SceneManager.GetActiveScene().buildIndex == gameplayScene)
		{
			SceneManager.LoadScene(titleScene);
		}
	}

	private void OnReceive(NetPeer from, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
	{
		if (from != peer)
		{
			reader.Recycle();
			return;
		}
		MessageType messageType = (MessageType)reader.GetByte();
		switch (messageType)
		{
		case MessageType.Start:
			if (!IsHost)
			{
				ulong @ulong = reader.GetULong();
				string @string = reader.GetString();
				if (!string.IsNullOrEmpty(@string))
				{
					PlayerPrefs.SetString(difficultyKey, @string);
				}
				WorldGenerator.RequestedSeed = @ulong;
				CoopItem.ResetLevel();
				CoopWeather.ResetLevel();
				Status = "Loading the host's world...";
				SceneManager.LoadScene(gameplayScene);
			}
			break;
		case MessageType.Ready:
			if (IsHost)
			{
				uint @uint = reader.GetUInt();
				Warning = ((@uint == hostLayoutHash) ? "" : "Your friend's world came out different from yours. Make sure you both have the same version of the mod.");
				CoopWeather.SendCurrent();
			}
			break;
		case MessageType.PlayerState:
			this.PlayerStateReceived?.Invoke(reader);
			break;
		case MessageType.Menu:
			if (!IsHost && SceneManager.GetActiveScene().buildIndex == gameplayScene)
			{
				Status = "The host went back to the title screen.";
				SceneManager.LoadScene(titleScene);
			}
			break;
		case MessageType.TakeItem:
			if (IsHost)
			{
				ulong ulong3 = reader.GetULong();
				CoopItem.OnTakeRequest(ulong3, reader.GetBool());
			}
			break;
		case MessageType.ItemTaken:
			if (!IsHost)
			{
				ulong ulong2 = reader.GetULong();
				CoopItem.OnTaken(ulong2, reader.GetBool());
			}
			break;
		case MessageType.Weather:
			if (!IsHost)
			{
				CoopWeather.OnMessage(reader);
			}
			break;
		case MessageType.Downed:
		case MessageType.Revived:
			if (CoopDowned.Instance != null)
			{
				CoopDowned.Instance.OnMessage(messageType);
			}
			break;
		case MessageType.Kanazawa:
			if (CoopKanazawa.Instance != null)
			{
				CoopKanazawa.Instance.OnMessage(reader);
			}
			break;
		case MessageType.ElevatorClosed:
			CoopElevator.OnGateClosed();
			break;
		case MessageType.Shot:
			CoopShots.OnShot(reader);
			break;
		case MessageType.Barrel:
			CoopBarrel.OnMessage(reader);
			break;
		case MessageType.RestartLevel:
			if (IsHost && SceneManager.GetActiveScene().buildIndex == gameplayScene)
			{
				RestartLevel();
			}
			break;
		case MessageType.CampfirePlaced:
			CoopCampfire.OnPlaced(reader);
			break;
		case MessageType.CampfireOut:
			CoopCampfire.OnOut(reader);
			break;
		case MessageType.Supplies:
			this.SuppliesReceived?.Invoke(reader);
			break;
		case MessageType.TakenItems:
			if (!IsHost)
			{
				CoopItem.OnTakenList(reader);
			}
			break;
		}
		reader.Recycle();
	}

	public void OnWorldGenerated(WorldGenerator generator)
	{
		if (IsHost)
		{
			hostSeed = generator.Seed;
			hostLayoutHash = generator.LayoutHash;
			CoopItem.ResetLevel();
			Warning = "";
			if (peer != null)
			{
				SendStart();
			}
		}
		else if (peer != null)
		{
			writer.Reset();
			writer.Put((byte)MessageType.Ready);
			writer.Put(generator.LayoutHash);
			peer.Send(writer, DeliveryMethod.ReliableOrdered);
			Status = "Playing together.";
		}
	}

	private void SendStart()
	{
		writer.Reset();
		writer.Put((byte)MessageType.Start);
		writer.Put(hostSeed);
		writer.Put(PlayerPrefs.GetString(difficultyKey, ""));
		peer.Send(writer, DeliveryMethod.ReliableOrdered);
		CoopItem.SendTakenList();
		Status = "Playing together.";
	}

	public void SendPlayerState(NetDataWriter state)
	{
		peer?.Send(state, DeliveryMethod.Sequenced);
	}

	public void RestartLevel()
	{
		if (IsHost)
		{
			SceneManager.LoadScene(gameplayScene);
			return;
		}
		writer.Reset();
		writer.Put((byte)MessageType.RestartLevel);
		peer?.Send(writer, DeliveryMethod.ReliableOrdered);
	}

	public void Send(NetDataWriter message, DeliveryMethod method)
	{
		peer?.Send(message, method);
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
	{
		if (scene.buildIndex == titleScene && IsHost)
		{
			hostSeed = 0uL;
			if (peer != null)
			{
				writer.Reset();
				writer.Put((byte)MessageType.Menu);
				peer.Send(writer, DeliveryMethod.ReliableOrdered);
				Status = "Your friend is connected. Start the game when you're ready.";
			}
		}
		else if (scene.buildIndex == titleScene && Mode == SessionMode.Joining && peer != null)
		{
			Status = "Connected. Waiting for the host to start the game...";
		}
	}
}
