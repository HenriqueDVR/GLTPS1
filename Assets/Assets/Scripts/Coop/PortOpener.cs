using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;
using Mono.Nat;
using UnityEngine;

// upnp / nat-pmp port forward, also gets the public ip
public sealed class PortOpener
{
	public enum Result
	{
		Working,
		Opened,
		NoRouter,
		Failed
	}

	private const float discoveryTimeout = 8f;

	// leased so a crash doesn't leave it open forever
	private const int leaseSeconds = 7200;

	private const float renewAfter = 3000f;

	private const string description = "GLTPS1 co-op";

	private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();

	private readonly object deviceLock = new object();

	private int port;

	private float startTime;

	private float mappedTime;

	private INatDevice device;

	private Mapping mapping;

	public Result State { get; private set; }

	public IPAddress ExternalIP { get; private set; }

	public string Detail { get; private set; }

	public void Open(int port)
	{
		this.port = port;
		State = Result.Working;
		Detail = "Asking your router to open the port...";
		startTime = Time.unscaledTime;
		NatUtility.DeviceFound += OnDeviceFound;
		NatUtility.StartDiscovery(NatProtocol.Upnp, NatProtocol.Pmp);
	}

	public void Poll()
	{
		Action result;
		while (mainThread.TryDequeue(out result))
		{
			result();
		}
		if (State == Result.Working && device == null && Time.unscaledTime - startTime > discoveryTimeout)
		{
			StopDiscovery();
			State = Result.NoRouter;
			Detail = "Couldn't open the port automatically: your router didn't answer (UPnP may be off). Friends on your network or with IPv6 can still join.";
		}
		if (State == Result.Opened && mapping != null && mapping.Lifetime > 0 && Time.unscaledTime - mappedTime > renewAfter)
		{
			mappedTime = Time.unscaledTime;
			MapAsync();
		}
	}

	public void Close()
	{
		StopDiscovery();
		INatDevice natDevice;
		Mapping mapping;
		lock (deviceLock)
		{
			natDevice = device;
			mapping = this.mapping;
			this.mapping = null;
		}
		if (natDevice != null && mapping != null)
		{
			try
			{
				natDevice.DeletePortMapAsync(mapping).Wait(1500);
			}
			catch (Exception)
			{
			}
		}
	}

	private void StopDiscovery()
	{
		NatUtility.DeviceFound -= OnDeviceFound;
		NatUtility.StopDiscovery();
	}

	// background thread
	private void OnDeviceFound(object sender, DeviceEventArgs args)
	{
		lock (deviceLock)
		{
			if (device != null)
			{
				return;
			}
			device = args.Device;
		}
		MapAsync();
	}

	private async void MapAsync()
	{
		try
		{
			Mapping created;
			try
			{
				created = await device.CreatePortMapAsync(new Mapping(Protocol.Udp, port, port, leaseSeconds, description));
			}
			catch (MappingException)
			{
				// some routers only do permanent ones, Close() removes it
				created = await device.CreatePortMapAsync(new Mapping(Protocol.Udp, port, port, 0, description));
			}
			IPAddress ip = await device.GetExternalIPAsync();
			mainThread.Enqueue(delegate
			{
				lock (deviceLock)
				{
					mapping = created;
				}
				mappedTime = Time.unscaledTime;
				ExternalIP = ip;
				if (ip == null || ip.Equals(IPAddress.Any) || CoopInvite.IsUnreachableIPv4(ip))
				{
					State = Result.Failed;
					Detail = "Port opened, but your internet provider shares one address between customers (CGNAT), so friends can't reach you over IPv4. IPv6 or LAN may still work.";
				}
				else
				{
					State = Result.Opened;
					Detail = "Port opened on your router (" + device.NatProtocol.ToString().ToUpperInvariant() + ").";
				}
			});
		}
		catch (Exception ex)
		{
			string message = ex.Message;
			mainThread.Enqueue(delegate
			{
				State = Result.Failed;
				Detail = "Your router refused to open the port (" + message + "). Friends on your network or with IPv6 can still join.";
			});
		}
	}
}
