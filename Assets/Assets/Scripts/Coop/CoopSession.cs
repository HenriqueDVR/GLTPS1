using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

// p2p connection. host listens on EOS and direct (LiteNetLib + upnp), friend tries both,
// first one to connect wins. host picks the world and sends seed + difficulty
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
		ElevatorClosed = 18,
		Ammo = 19,
		// yuuri's ammo for the HUD
		AmmoState = 20,
		// rejoin: yuuri + ket
		Rejoin = 21,
		KanazawaPoses = 22,
		// pipe levels
		Pipes = 23,
		Music = 24,
		MusicNext = 25,
		// numbered wrapper so stale unreliable ones get dropped (EOS has no sequenced)
		Sequenced = 26,
		// EOS only (no handshake there)
		Hello = 100,
		Welcome = 101,
		Refused = 102,
		Ping = 103,
		Pong = 104
	}

	public enum ConnectionChoice
	{
		Auto,
		DirectOnly,
		EpicOnly
	}

	public const int DefaultPort = 27015;

	private const string connectionChoiceKey = "coopConnection";

	// head start for direct before EOS kicks in (port opened / lan only)
	private const float directHeadStart = 4f;

	private const float lanHeadStart = 1f;

	// bump on protocol changes
	private const string connectionKey = "GLTPS1-coop-19";

	private const int gameplayScene = 1;

	private const int titleScene = 0;

	private const string difficultyKey = "difficultySetting";

	private const float eosJoinTimeout = 30f;

	private const float eosPingInterval = 1f;

	// partner this quiet is gone, let a new join take the slot
	private const float stalePartnerSeconds = 3f;

	private ushort nextSequence;

	private readonly Dictionary<byte, ushort> lastSequence = new Dictionary<byte, ushort>();

	private readonly NetDataWriter sequencedWriter = new NetDataWriter();

	private float lastEosPacket;

	private NetManager manager;

	private NetPeer peer;

	private readonly CoopEos eos = new CoopEos();

	private ProductUserId eosPeer;

	private ProductUserId eosTarget;

	private bool eosHelloSent;

	private float eosStartTime;

	private float eosDeadline;

	private float nextEosPing;

	private int eosRoundTrip;

	// can arrive before the hello
	private readonly Dictionary<string, NetworkConnectionType> eosLinkTypes = new Dictionary<string, NetworkConnectionType>();

	private CoopEos.LoginState shownEosState;

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

#if DUMP_WORLDGEN
	// -coopeosonly
	private bool eosOnly;

	// -cooplag <ms> -coopjitter <ms> -cooploss <%>: held back on arrival, both links
	private struct Delayed
	{
		public float At;

		public bool Direct;

		public int Link;

		public ProductUserId From;

		public byte[] Data;

		public bool Reliable;
	}

	private readonly List<Delayed> delayed = new List<Delayed>();

	private float simLag;

	private float simJitter;

	private float simLoss;

	private float lastReliableAt;

	private bool releasing;

	// drops held packets from an old connection
	private int link;

	private bool Simulating => simLag > 0f || simJitter > 0f || simLoss > 0f;
#endif

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

	// stays true while the partner is away so they can rejoin
	public static bool InGame
	{
		get
		{
			if (Active && CoopPlayers.Instance != null)
			{
				return SceneManager.GetActiveScene().buildIndex == gameplayScene;
			}
			return false;
		}
	}

	public static bool Together => InGame && Instance.Connected;

	public event Action PartnerJoined;

	public event Action PartnerLeft;

	public SessionMode Mode { get; private set; }

	public bool IsHost => Mode == SessionMode.Hosting;

	public bool Connected => peer != null || eosPeer != null;

	public int Ping
	{
		get
		{
			if (peer != null)
			{
#if DUMP_WORLDGEN
				// litenetlib's own pings skip the fake lag
				return peer.RoundTripTime + Mathf.RoundToInt(simLag * 1000f);
#else
				return peer.RoundTripTime;
#endif
			}
			return (eosPeer != null) ? eosRoundTrip : 0;
		}
	}

	public string LinkKind { get; private set; } = "";

	public bool InviteReady => (PortOpener == null || PortOpener.State != PortOpener.Result.Working) && (Choice == ConnectionChoice.DirectOnly || eos.State != CoopEos.LoginState.Working);

	public ConnectionChoice Choice
	{
		get
		{
			return (ConnectionChoice)Mathf.Clamp(PlayerPrefs.GetInt(connectionChoiceKey, 0), 0, 2);
		}
		set
		{
			PlayerPrefs.SetInt(connectionChoiceKey, (int)value);
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

	public event Action<NetDataReader> PlayerStateReceived;

	public event Action<NetDataReader> SuppliesReceived;

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
#if DUMP_WORLDGEN
			else if (commandLineArgs[i] == "-coopeosonly")
			{
				eosOnly = true;
			}
			// -background
			else if (commandLineArgs[i] == "-background")
			{
				Application.runInBackground = true;
			}
			else if (commandLineArgs[i] == "-cooplag" && float.TryParse(text, out var lag))
			{
				simLag = lag / 1000f;
			}
			else if (commandLineArgs[i] == "-coopjitter" && float.TryParse(text, out var jitter))
			{
				simJitter = jitter / 1000f;
			}
			else if (commandLineArgs[i] == "-cooploss" && float.TryParse(text, out var loss))
			{
				simLoss = loss / 100f;
			}
#endif
		}
		SceneManager.sceneLoaded += OnSceneLoaded;
		eos.AcceptConnection = AcceptEosConnection;
		eos.MessageReceived += OnEosMessage;
		eos.Connected += OnEosConnected;
		eos.Closed += OnEosClosed;
		// sign in early so the invite is ready
		eos.Start();
		shownEosState = eos.State;
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
		eos.Shutdown();
	}

	private void Update()
	{
		manager?.PollEvents();
		eos.Tick();
		PortOpener?.Poll();
		if (IsHost && PortOpener != null && PortOpener.Detail != lastPortDetail)
		{
			lastPortDetail = PortOpener.Detail;
			Debug.Log("Coop: " + lastPortDetail);
			RefreshInvite();
		}
		if (eos.State != shownEosState)
		{
			shownEosState = eos.State;
			if (IsHost)
			{
				RefreshInvite();
			}
		}
		UpdateEosJoin();
#if DUMP_WORLDGEN
		ReleaseDelayed();
#endif
		if (eosPeer != null && Time.unscaledTime >= nextEosPing)
		{
			nextEosPing = Time.unscaledTime + eosPingInterval;
			writer.Reset();
			writer.Put((byte)MessageType.Ping);
			writer.Put(Time.unscaledTime);
			eos.Send(eosPeer, writer.Data, writer.Length, reliable: false);
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
		if (Choice == ConnectionChoice.EpicOnly)
		{
			manager = null;
			if (eos.State == CoopEos.LoginState.Failed)
			{
				Status = "Couldn't start hosting: Epic Online Services " + eos.Error + ".";
				Leave();
				return;
			}
		}
		else if (!manager.Start(Port))
		{
			manager.Stop();
			manager = null;
			if (eos.State == CoopEos.LoginState.Failed || Choice == ConnectionChoice.DirectOnly)
			{
				Status = "Couldn't start hosting: port " + Port + " is in use.";
				Leave();
				return;
			}
		}
		Mode = SessionMode.Hosting;
		Status = "Waiting for your friend to join...";
		Warning = "";
		if (manager != null)
		{
			PortOpener = new PortOpener();
			PortOpener.Open(Port);
		}
		RefreshInvite();
	}

	public void Join(string inviteText)
	{
		List<IPEndPoint> list = CoopInvite.Decode(inviteText, DefaultPort, out string eosId, out bool publicAddress);
		if (list.Count == 0 && eosId == null)
		{
			Status = "That invite doesn't look right. Copy it again from your friend.";
			return;
		}
		if (Choice == ConnectionChoice.DirectOnly)
		{
			eosId = null;
		}
		else if (Choice == ConnectionChoice.EpicOnly)
		{
			list.Clear();
		}
		if (list.Count == 0 && eosId == null)
		{
			Status = (Choice == ConnectionChoice.DirectOnly) ? "That invite only works through Epic: set the connection to auto." : "That invite has no Epic connection: set the connection to auto.";
			return;
		}
		Leave();
		CreateManager();
		manager.Start();
		Mode = SessionMode.Joining;
		Status = "Connecting...";
		Warning = "";
#if DUMP_WORLDGEN
		if (eosOnly)
		{
			list.Clear();
		}
#endif
		foreach (IPEndPoint item in list)
		{
			NetPeer netPeer = manager.Connect(item, connectionKey);
			if (netPeer != null)
			{
				pendingConnections.Add(netPeer);
			}
		}
		if (eosId != null)
		{
			eosTarget = ProductUserId.FromString(eosId);
			eosHelloSent = false;
			// direct gets a head start
			eosStartTime = Time.unscaledTime + ((list.Count == 0) ? 0f : (publicAddress ? directHeadStart : lanHeadStart));
			eosDeadline = eosStartTime + eosJoinTimeout;
		}
	}

	private void UpdateEosJoin()
	{
		if (Mode != SessionMode.Joining || Connected || eosTarget == null)
		{
			return;
		}
		if (eos.State == CoopEos.LoginState.Ready && !eosHelloSent && Time.unscaledTime >= eosStartTime)
		{
			eosHelloSent = true;
			writer.Reset();
			writer.Put((byte)MessageType.Hello);
			writer.Put(connectionKey);
			eos.Send(eosTarget, writer.Data, writer.Length, reliable: true);
		}
		if (eos.State == CoopEos.LoginState.Failed || Time.unscaledTime > eosDeadline)
		{
			Debug.Log("Coop: no answer over Epic Online Services" + ((eos.State == CoopEos.LoginState.Failed) ? (": " + eos.Error) : ""));
			eos.Close(eosTarget);
			eosTarget = null;
			FailIfNothingLeft("no answer");
		}
	}

	private void FailIfNothingLeft(string reason)
	{
		if (Mode == SessionMode.Joining && !Connected && pendingConnections.Count == 0 && eosTarget == null)
		{
			Status = "Couldn't connect (" + reason + "). Check the invite, and that the host is still hosting.";
			Leave();
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
		if (eosPeer != null)
		{
			eos.Close(eosPeer);
		}
		if (eosTarget != null)
		{
			eos.Close(eosTarget);
		}
		eosPeer = null;
		eosTarget = null;
		LinkKind = "";
#if DUMP_WORLDGEN
		link++;
#endif
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
		CoopMusic.ResetLevel();
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
			DisconnectTimeout = 20000,
			// 1 = shots/ammo, so a resend there doesn't hold up the rest
			ChannelsCount = 2
		};
		previousRunInBackground = Application.runInBackground;
		Application.runInBackground = true;
	}

	private void RefreshInvite()
	{
		CoopInvite.FindLocalAddresses(out var lanIPv4, out var ipv);
		IPAddress publicIPv = ((PortOpener != null && PortOpener.State == PortOpener.Result.Opened) ? PortOpener.ExternalIP : null);
		// port in use, EOS only
		if (manager == null)
		{
			lanIPv4 = null;
			ipv = null;
		}
		Invite = CoopInvite.Encode(Port, publicIPv, lanIPv4, ipv, (eos.State == CoopEos.LoginState.Ready && Choice != ConnectionChoice.DirectOnly) ? eos.LocalId : null);
#if DUMP_WORLDGEN
		Debug.Log("Coop: invite " + Invite);
#endif
	}

	private void OnConnectionRequest(ConnectionRequest request)
	{
		if (IsHost && PartnerIsStale())
		{
			DropStalePartner();
		}
		if (IsHost && !Connected)
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
		if (Connected)
		{
			connected.Disconnect();
			return;
		}
		peer = connected;
		LinkKind = "direct";
		LinkUp();
	}

	private void LinkUp()
	{
		foreach (NetPeer pendingConnection in pendingConnections)
		{
			if (pendingConnection != peer)
			{
				pendingConnection.Disconnect();
			}
		}
		pendingConnections.Clear();
		if (peer != null && eosTarget != null)
		{
			eos.Close(eosTarget);
		}
		eosTarget = null;
		lastSequence.Clear();
		Debug.Log("Coop: connected (" + LinkKind + ")");
		if (IsHost)
		{
			Status = "Your friend joined.";
			if (SceneManager.GetActiveScene().buildIndex == gameplayScene && hostSeed != 0L)
			{
				SendStart();
				PartnerJoined?.Invoke();
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
			FailIfNothingLeft(info.Reason.ToString());
			return;
		}
		peer = null;
		LinkDown(info.Reason.ToString());
	}

	private void LinkDown(string reason)
	{
		LinkKind = "";
		if (IsHost)
		{
			Status = "Your friend left. Waiting for someone to join...";
			PartnerLeft?.Invoke();
			return;
		}
		Status = "Disconnected from the host (" + reason + ").";
		Leave();
		if (SceneManager.GetActiveScene().buildIndex == gameplayScene)
		{
			SceneManager.LoadScene(titleScene);
		}
	}

	private bool PartnerIsStale()
	{
		if (peer != null)
		{
			return peer.TimeSinceLastPacket > stalePartnerSeconds * 1000f;
		}
		if (eosPeer != null)
		{
			return Time.unscaledTime - lastEosPacket > stalePartnerSeconds;
		}
		return false;
	}

	private void DropStalePartner()
	{
		Debug.Log("Coop: the old connection went quiet; letting the new one in");
		if (peer != null)
		{
			NetPeer old = peer;
			peer = null;
			old.Disconnect();
		}
		if (eosPeer != null)
		{
			eos.Close(eosPeer);
			eosPeer = null;
		}
		LinkDown("timed out");
	}

	private static bool SameUser(ProductUserId a, ProductUserId b)
	{
		return a != null && b != null && a.ToString() == b.ToString();
	}

	private bool AcceptEosConnection(ProductUserId remote)
	{
		if (IsHost)
		{
			return Choice != ConnectionChoice.DirectOnly && (!Connected || SameUser(remote, eosPeer));
		}
		return SameUser(remote, eosTarget) || SameUser(remote, eosPeer);
	}

	private void OnEosConnected(ProductUserId remote, NetworkConnectionType type)
	{
		eosLinkTypes[remote.ToString()] = type;
		Debug.Log("Coop: Epic Online Services link to " + remote + " is " + type);
		if (SameUser(remote, eosPeer))
		{
			LinkKind = EosLinkKind(remote);
		}
	}

	private string EosLinkKind(ProductUserId remote)
	{
		if (!eosLinkTypes.TryGetValue(remote.ToString(), out NetworkConnectionType type))
		{
			return "Epic";
		}
		return (type == NetworkConnectionType.RelayedConnection) ? "Epic relay" : "Epic, direct";
	}

	private void OnEosClosed(ProductUserId remote, ConnectionClosedReason reason)
	{
		if (SameUser(remote, eosPeer))
		{
			eosPeer = null;
			LinkDown(reason.ToString());
		}
		else if (SameUser(remote, eosTarget) && !Connected)
		{
			eosTarget = null;
			FailIfNothingLeft(reason.ToString());
		}
	}

	private void OnEosMessage(ProductUserId from, byte[] data, int length, bool reliable)
	{
		if (length < 1)
		{
			return;
		}
#if DUMP_WORLDGEN
		if (Simulating && !releasing)
		{
			Hold(false, from, data, length, reliable);
			return;
		}
#endif
		NetDataReader reader = new NetDataReader(data, 0, length);
		MessageType messageType = (MessageType)data[0];
		switch (messageType)
		{
		case MessageType.Hello:
			reader.GetByte();
			if (IsHost && PartnerIsStale())
			{
				DropStalePartner();
			}
			if (IsHost && !Connected)
			{
				writer.Reset();
				if (reader.GetString() == connectionKey)
				{
					eosPeer = from;
					lastEosPacket = Time.unscaledTime;
					writer.Put((byte)MessageType.Welcome);
					eos.Send(from, writer.Data, writer.Length, reliable: true);
					LinkKind = EosLinkKind(from);
					LinkUp();
				}
				else
				{
					writer.Put((byte)MessageType.Refused);
					eos.Send(from, writer.Data, writer.Length, reliable: true);
				}
			}
			return;
		case MessageType.Welcome:
			if (Mode == SessionMode.Joining && !Connected && SameUser(from, eosTarget))
			{
				eosPeer = from;
				lastEosPacket = Time.unscaledTime;
				LinkKind = EosLinkKind(from);
				LinkUp();
			}
			return;
		case MessageType.Refused:
			if (Mode == SessionMode.Joining && !Connected)
			{
				Leave();
				Status = "Couldn't connect: you and the host have different versions of the mod.";
			}
			return;
		case MessageType.Ping:
			writer.Reset();
			writer.Put((byte)MessageType.Pong);
			writer.Put(data, 1, length - 1);
			eos.Send(from, writer.Data, writer.Length, reliable: false);
			return;
		case MessageType.Pong:
			reader.GetByte();
			int roundTrip = Mathf.RoundToInt((Time.unscaledTime - reader.GetFloat()) * 1000f);
			// smoothed so the HUD doesn't flicker
			eosRoundTrip = (eosRoundTrip == 0) ? roundTrip : Mathf.RoundToInt(Mathf.Lerp(eosRoundTrip, roundTrip, 0.5f));
			return;
		}
		if (SameUser(from, eosPeer))
		{
			lastEosPacket = Time.unscaledTime;
			Dispatch(reader);
		}
	}

	private void OnReceive(NetPeer from, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
	{
		if (from == peer)
		{
#if DUMP_WORLDGEN
			if (Simulating)
			{
				byte[] data = reader.GetRemainingBytes();
				Hold(true, null, data, data.Length, deliveryMethod == DeliveryMethod.ReliableOrdered || deliveryMethod == DeliveryMethod.ReliableUnordered || deliveryMethod == DeliveryMethod.ReliableSequenced);
				reader.Recycle();
				return;
			}
#endif
			Dispatch(reader);
		}
		reader.Recycle();
	}

#if DUMP_WORLDGEN
	private void Hold(bool direct, ProductUserId from, byte[] data, int length, bool reliable)
	{
		float at = Time.unscaledTime + simLag + UnityEngine.Random.Range(0f, simJitter);
		if (UnityEngine.Random.value < simLoss)
		{
			if (!reliable)
			{
				return;
			}
			// lost and sent again
			at += simLag * 2f + 0.1f;
		}
		if (reliable)
		{
			at = Mathf.Max(at, lastReliableAt);
			lastReliableAt = at;
		}
		byte[] copy = new byte[length];
		Array.Copy(data, copy, length);
		delayed.Add(new Delayed
		{
			At = at,
			Direct = direct,
			Link = link,
			From = from,
			Data = copy,
			Reliable = reliable
		});
	}

	private void ReleaseDelayed()
	{
		releasing = true;
		for (int i = 0; i < delayed.Count; i++)
		{
			Delayed item = delayed[i];
			if (item.At > Time.unscaledTime)
			{
				continue;
			}
			delayed.RemoveAt(i--);
			if (item.Link != link)
			{
				continue;
			}
			if (!item.Direct)
			{
				OnEosMessage(item.From, item.Data, item.Data.Length, item.Reliable);
			}
			else if (peer != null)
			{
				Dispatch(new NetDataReader(item.Data));
			}
		}
		releasing = false;
	}
#endif

	private void Dispatch(NetDataReader reader)
	{
		MessageType messageType = (MessageType)reader.GetByte();
		switch (messageType)
		{
		case MessageType.Sequenced:
		{
			ushort sequence = reader.GetUShort();
			if (reader.AvailableBytes < 1)
			{
				break;
			}
			byte inner = reader.PeekByte();
			if (lastSequence.TryGetValue(inner, out ushort last) && (short)(sequence - last) <= 0)
			{
				break;
			}
			lastSequence[inner] = sequence;
			Dispatch(reader);
			break;
		}
		case MessageType.Start:
			if (!IsHost)
			{
				GameSave.RestoreTaken = null;
				CoopPipes.SetRestore(null);
				ulong @ulong = reader.GetULong();
				string @string = reader.GetString();
				if (!string.IsNullOrEmpty(@string))
				{
					PlayerPrefs.SetString(difficultyKey, @string);
				}
				WorldGenerator.RequestedSeed = @ulong;
				CoopItem.ResetLevel();
				CoopWeather.ResetLevel();
				CoopMusic.ResetLevel();
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
				CoopMusic.SendCurrent();
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
		case MessageType.Music:
			if (!IsHost)
			{
				CoopMusic.OnMessage(reader);
			}
			break;
		case MessageType.MusicNext:
			if (IsHost)
			{
				CoopMusic.OnNextRequest();
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
		case MessageType.Ammo:
			CoopShots.OnAmmo(reader);
			break;
		case MessageType.AmmoState:
			CoopShots.OnAmmoState(reader);
			break;
		case MessageType.Rejoin:
			if (!IsHost)
			{
				CoopGirlState.OnRejoin(reader);
			}
			break;
		case MessageType.KanazawaPoses:
			if (!IsHost)
			{
				CoopKanazawa.OnPoses(reader);
			}
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
		case MessageType.Pipes:
			if (!IsHost)
			{
				CoopPipes.OnLevels(reader);
			}
			break;
		case MessageType.TakenItems:
			if (!IsHost)
			{
				CoopItem.OnTakenList(reader);
			}
			break;
		}
	}

	public void OnWorldGenerated(WorldGenerator generator)
	{
		if (IsHost)
		{
			hostSeed = generator.Seed;
			hostLayoutHash = generator.LayoutHash;
			CoopItem.ResetLevel();
			Warning = "";
			if (Connected)
			{
				SendStart();
			}
		}
		else if (Connected)
		{
			writer.Reset();
			writer.Put((byte)MessageType.Ready);
			writer.Put(generator.LayoutHash);
			Send(writer, DeliveryMethod.ReliableOrdered);
			Status = "Playing together.";
		}
	}

	private void SendStart()
	{
		writer.Reset();
		writer.Put((byte)MessageType.Start);
		writer.Put(hostSeed);
		writer.Put(PlayerPrefs.GetString(difficultyKey, ""));
		Send(writer, DeliveryMethod.ReliableOrdered);
		CoopItem.SendTakenList();
		CoopPipes.Send();
		CoopKanazawa.SendUnlocks();
		CoopPlayers players = CoopPlayers.Instance;
		CoopKettenkrad vehicle = CoopKettenkrad.Instance;
		if (players != null && vehicle != null && vehicle.Ready)
		{
			CoopGirlState.SendRejoin(writer, true, players.ToLayout(vehicle.VehiclePosition), vehicle.VehicleRotation);
		}
		else
		{
			CoopGirlState.SendRejoin(writer, false, Vector3.zero, Quaternion.identity);
		}
		Status = "Playing together.";
	}

	public void ResendRejoin()
	{
		CoopKanazawa.SendUnlocks();
		CoopPlayers players = CoopPlayers.Instance;
		CoopKettenkrad vehicle = CoopKettenkrad.Instance;
		if (players != null && vehicle != null && vehicle.Ready)
		{
			CoopGirlState.SendRejoin(writer, true, players.ToLayout(vehicle.VehiclePosition), vehicle.VehicleRotation);
		}
	}

	public void SendPlayerState(NetDataWriter state)
	{
		Send(state, DeliveryMethod.Sequenced);
		// out now instead of on litenetlib's next tick
		manager?.TriggerUpdate();
	}

	public void RestartLevel()
	{
		if (IsHost)
		{
			CoopGirlState.HostKept = null;
			SceneManager.LoadScene(gameplayScene);
			return;
		}
		writer.Reset();
		writer.Put((byte)MessageType.RestartLevel);
		Send(writer, DeliveryMethod.ReliableOrdered);
	}

	// side: own reliable lane, for things nothing else waits on (shots, ammo)
	public void Send(NetDataWriter message, DeliveryMethod method, bool side = false)
	{
		if (!Connected)
		{
			return;
		}
		if (method == DeliveryMethod.Unreliable || method == DeliveryMethod.Sequenced)
		{
			sequencedWriter.Reset();
			sequencedWriter.Put((byte)MessageType.Sequenced);
			sequencedWriter.Put(nextSequence++);
			sequencedWriter.Put(message.Data, 0, message.Length);
			message = sequencedWriter;
		}
		if (peer != null)
		{
			// too big for unreliable, send reliable (gets split)
			if ((method == DeliveryMethod.Unreliable || method == DeliveryMethod.Sequenced) && message.Length > peer.GetMaxSinglePacketSize(method))
			{
				method = DeliveryMethod.ReliableOrdered;
			}
			peer.Send(message, side ? (byte)1 : (byte)0, method);
		}
		else if (eosPeer != null)
		{
			bool reliable = method == DeliveryMethod.ReliableOrdered || method == DeliveryMethod.ReliableUnordered || method == DeliveryMethod.ReliableSequenced;
			eos.Send(eosPeer, message.Data, message.Length, reliable, side);
		}
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
	{
		if (scene.buildIndex == titleScene)
		{
			CoopGirlState.HostKept = null;
			CoopGirlState.ClearPending();
			CoopPipes.SetRestore(null);
		}
		if (scene.buildIndex == titleScene && IsHost)
		{
			hostSeed = 0uL;
			if (Connected)
			{
				writer.Reset();
				writer.Put((byte)MessageType.Menu);
				Send(writer, DeliveryMethod.ReliableOrdered);
				Status = "Your friend is connected. Start the game when you're ready.";
			}
		}
		else if (scene.buildIndex == titleScene && Mode == SessionMode.Joining && Connected)
		{
			Status = "Connected. Waiting for the host to start the game...";
		}
	}
}
