using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Platform;
using UnityEngine;

// EOS: anonymous device id login + p2p (relays if direct fails)
public class CoopEos
{
	public enum LoginState
	{
		Off,
		Working,
		Ready,
		Failed
	}

	private const string socketName = "GLTPS1coop";

	private const byte reliableChannel = 0;

	private const byte unreliableChannel = 1;

	// reliable too, but not held up behind channel 0
	private const byte sideChannel = 2;

	// first byte of reliable packets, longer messages get split
	private const byte wholeMessage = 0;

	private const byte morePieces = 1;

	private const byte lastPiece = 2;

	private const int maxPacket = P2PInterface.MAX_PACKET_SIZE;

	private PlatformInterface platform;

	private P2PInterface p2p;

	private readonly SocketId socket = new SocketId { SocketName = socketName };

	private readonly byte[] receiveBuffer = new byte[maxPacket];

	private readonly byte[] sendBuffer = new byte[maxPacket];

	// per user + channel
	private readonly Dictionary<string, List<byte>> pieces = new Dictionary<string, List<byte>>();

	public LoginState State { get; private set; }

	public string Error { get; private set; } = "";

	public ProductUserId LocalUser { get; private set; }

	public string LocalId => LocalUser != null ? LocalUser.ToString() : null;

	public Func<ProductUserId, bool> AcceptConnection;

	public event Action<ProductUserId, NetworkConnectionType> Connected;

	public event Action<ProductUserId, ConnectionClosedReason> Closed;

	// last bool: came reliable
	public event Action<ProductUserId, byte[], int, bool> MessageReceived;

	public void Start()
	{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
		if (State != LoginState.Off)
		{
			return;
		}
		State = LoginState.Working;
		InitializeOptions initialize = new InitializeOptions
		{
			ProductName = "GLTPS1",
			ProductVersion = Application.version
		};
		Result result = PlatformInterface.Initialize(ref initialize);
		if (result != Result.Success && result != Result.AlreadyConfigured)
		{
			Fail("couldn't start (" + result + ")");
			return;
		}
		LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.Warning);
		LoggingInterface.SetCallback((ref LogMessage message) => Debug.Log("EOS " + message.Category + ": " + message.Message));
		WindowsOptions options = new WindowsOptions
		{
			ProductId = EosCredentials.ProductId,
			SandboxId = EosCredentials.SandboxId,
			DeploymentId = EosCredentials.DeploymentId,
			ClientCredentials = new ClientCredentials
			{
				ClientId = EosCredentials.ClientId,
				ClientSecret = EosCredentials.ClientSecret
			},
			Flags = PlatformFlags.DisableOverlay,
			CacheDirectory = Application.temporaryCachePath,
			IsServer = false
		};
		platform = PlatformInterface.Create(ref options);
		if (platform == null)
		{
			Fail("couldn't start");
			return;
		}
		p2p = platform.GetP2PInterface();
#if DUMP_WORLDGEN
		// -coopnewdevice: new anon user so 2 copies on one pc can connect
		if (Array.IndexOf(Environment.GetCommandLineArgs(), "-coopnewdevice") >= 0)
		{
			DeleteDeviceIdOptions deleteOptions = default;
			platform.GetConnectInterface().DeleteDeviceId(ref deleteOptions, null, (ref DeleteDeviceIdCallbackInfo info) => CreateDeviceIdAndLogin());
			return;
		}
#endif
		CreateDeviceIdAndLogin();
#else
		Fail("only available in the Windows game");
#endif
	}

	private void CreateDeviceIdAndLogin()
	{
		CreateDeviceIdOptions deviceOptions = new CreateDeviceIdOptions
		{
			DeviceModel = "PC"
		};
		platform.GetConnectInterface().CreateDeviceId(ref deviceOptions, null, (ref CreateDeviceIdCallbackInfo info) =>
		{
			if (info.ResultCode == Result.Success || info.ResultCode == Result.DuplicateNotAllowed)
			{
				Login();
			}
			else
			{
				Fail("couldn't sign in (" + info.ResultCode + ")");
			}
		});
	}

	private void Login()
	{
		LoginOptions options = new LoginOptions
		{
			Credentials = new Credentials
			{
				Type = ExternalCredentialType.DeviceidAccessToken,
				Token = null
			},
			UserLoginInfo = new UserLoginInfo
			{
				DisplayName = "Player"
			}
		};
		platform.GetConnectInterface().Login(ref options, null, (ref LoginCallbackInfo info) =>
		{
			if (info.ResultCode == Result.Success)
			{
				LoggedIn(info.LocalUserId);
			}
			else if (info.ResultCode == Result.InvalidUser)
			{
				CreateUserOptions create = new CreateUserOptions
				{
					ContinuanceToken = info.ContinuanceToken
				};
				platform.GetConnectInterface().CreateUser(ref create, null, (ref CreateUserCallbackInfo created) =>
				{
					if (created.ResultCode == Result.Success)
					{
						LoggedIn(created.LocalUserId);
					}
					else
					{
						Fail("couldn't sign in (" + created.ResultCode + ")");
					}
				});
			}
			else
			{
				Fail("couldn't sign in (" + info.ResultCode + ")");
			}
		});
	}

	private void LoggedIn(ProductUserId user)
	{
		LocalUser = user;
		AddNotifyPeerConnectionRequestOptions request = new AddNotifyPeerConnectionRequestOptions
		{
			LocalUserId = LocalUser,
			SocketId = socket
		};
		p2p.AddNotifyPeerConnectionRequest(ref request, null, (ref OnIncomingConnectionRequestInfo info) =>
		{
			if (AcceptConnection != null && AcceptConnection(info.RemoteUserId))
			{
				Accept(info.RemoteUserId);
			}
		});
		AddNotifyPeerConnectionEstablishedOptions established = new AddNotifyPeerConnectionEstablishedOptions
		{
			LocalUserId = LocalUser,
			SocketId = socket
		};
		p2p.AddNotifyPeerConnectionEstablished(ref established, null, (ref OnPeerConnectionEstablishedInfo info) =>
		{
			Connected?.Invoke(info.RemoteUserId, info.NetworkType);
		});
		AddNotifyPeerConnectionClosedOptions closed = new AddNotifyPeerConnectionClosedOptions
		{
			LocalUserId = LocalUser,
			SocketId = socket
		};
		p2p.AddNotifyPeerConnectionClosed(ref closed, null, (ref OnRemoteConnectionClosedInfo info) =>
		{
			ForgetPieces(info.RemoteUserId);
			Closed?.Invoke(info.RemoteUserId, info.Reason);
		});
#if DUMP_WORLDGEN
		// -coopforcerelay
		if (Array.IndexOf(Environment.GetCommandLineArgs(), "-coopforcerelay") >= 0)
		{
			SetRelayControlOptions relay = new SetRelayControlOptions
			{
				RelayControl = RelayControl.ForceRelays
			};
			p2p.SetRelayControl(ref relay);
		}
#endif
		State = LoginState.Ready;
		Debug.Log("Coop: signed in to Epic Online Services as " + LocalId);
	}

	private void Fail(string error)
	{
		State = LoginState.Failed;
		Error = error;
		Debug.Log("Coop: Epic Online Services " + error);
	}

	private void Accept(ProductUserId remote)
	{
		AcceptConnectionOptions options = new AcceptConnectionOptions
		{
			LocalUserId = LocalUser,
			RemoteUserId = remote,
			SocketId = socket
		};
		p2p.AcceptConnection(ref options);
	}

	public void Tick()
	{
		if (platform == null)
		{
			return;
		}
		platform.Tick();
		if (State != LoginState.Ready)
		{
			return;
		}
		GetNextReceivedPacketSizeOptions sizeOptions = new GetNextReceivedPacketSizeOptions
		{
			LocalUserId = LocalUser
		};
		ReceivePacketOptions receiveOptions = new ReceivePacketOptions
		{
			LocalUserId = LocalUser,
			MaxDataSizeBytes = maxPacket
		};
		while (p2p.GetNextReceivedPacketSize(ref sizeOptions, out uint _) == Result.Success)
		{
			ProductUserId from = null;
			SocketId fromSocket = default;
			if (p2p.ReceivePacket(ref receiveOptions, ref from, ref fromSocket, out byte channel, new ArraySegment<byte>(receiveBuffer), out uint length) != Result.Success)
			{
				break;
			}
			Received(from, channel, (int)length);
		}
	}

	private void Received(ProductUserId from, byte channel, int length)
	{
		if (length < 1)
		{
			return;
		}
		if (channel == unreliableChannel)
		{
			MessageReceived?.Invoke(from, Copy(receiveBuffer, 1, length - 1), length - 1, false);
			return;
		}
		byte kind = receiveBuffer[0];
		string key = from + "/" + channel;
		if (kind == wholeMessage && !pieces.ContainsKey(key))
		{
			MessageReceived?.Invoke(from, Copy(receiveBuffer, 1, length - 1), length - 1, true);
			return;
		}
		if (!pieces.TryGetValue(key, out List<byte> message))
		{
			message = new List<byte>();
			pieces[key] = message;
		}
		for (int i = 1; i < length; i++)
		{
			message.Add(receiveBuffer[i]);
		}
		if (kind != morePieces)
		{
			pieces.Remove(key);
			MessageReceived?.Invoke(from, message.ToArray(), message.Count, true);
		}
	}

	private static byte[] Copy(byte[] source, int start, int length)
	{
		byte[] copy = new byte[length];
		Array.Copy(source, start, copy, 0, length);
		return copy;
	}

	public void Send(ProductUserId to, byte[] data, int length, bool reliable, bool side = false)
	{
		if (State != LoginState.Ready || to == null)
		{
			return;
		}
		if (!reliable && length > maxPacket - 1)
		{
			reliable = true;
		}
		if (!reliable)
		{
			SendPacket(to, unreliableChannel, PacketReliability.UnreliableUnordered, wholeMessage, data, 0, length);
			return;
		}
		int offset = 0;
		do
		{
			int piece = Math.Min(maxPacket - 1, length - offset);
			byte kind = (offset == 0 && piece == length) ? wholeMessage : ((offset + piece < length) ? morePieces : lastPiece);
			SendPacket(to, side ? sideChannel : reliableChannel, PacketReliability.ReliableOrdered, kind, data, offset, piece);
			offset += piece;
		}
		while (offset < length);
	}

	private void SendPacket(ProductUserId to, byte channel, PacketReliability reliability, byte kind, byte[] data, int offset, int length)
	{
		sendBuffer[0] = kind;
		Array.Copy(data, offset, sendBuffer, 1, length);
		SendPacketOptions options = new SendPacketOptions
		{
			LocalUserId = LocalUser,
			RemoteUserId = to,
			SocketId = socket,
			Channel = channel,
			Data = new ArraySegment<byte>(sendBuffer, 0, length + 1),
			AllowDelayedDelivery = true,
			Reliability = reliability
		};
		p2p.SendPacket(ref options);
	}

	public void Close(ProductUserId remote)
	{
		if (State != LoginState.Ready || remote == null)
		{
			return;
		}
		ForgetPieces(remote);
		CloseConnectionOptions options = new CloseConnectionOptions
		{
			LocalUserId = LocalUser,
			RemoteUserId = remote,
			SocketId = socket
		};
		p2p.CloseConnection(ref options);
	}

	private void ForgetPieces(ProductUserId remote)
	{
		pieces.Remove(remote + "/" + reliableChannel);
		pieces.Remove(remote + "/" + sideChannel);
	}

	public void Shutdown()
	{
		if (platform != null)
		{
			platform.Release();
			platform = null;
			PlatformInterface.Shutdown();
		}
		State = LoginState.Off;
	}
}
