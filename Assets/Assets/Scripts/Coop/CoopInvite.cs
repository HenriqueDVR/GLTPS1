using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

public static class CoopInvite
{
	private const string prefix = "GLT-";

	private const byte version = 1;

	private const byte hasPublic = 1;

	private const byte hasLan = 2;

	private const byte hasIPv6 = 4;

	public static string Encode(int port, IPAddress publicIPv4, IPAddress lanIPv4, IPAddress ipv6)
	{
		List<byte> list = new List<byte> { version, (byte)(port >> 8), (byte)port, 0 };
		byte b = 0;
		if (publicIPv4 != null)
		{
			b |= hasPublic;
			list.AddRange(publicIPv4.GetAddressBytes());
		}
		if (lanIPv4 != null)
		{
			b |= hasLan;
			list.AddRange(lanIPv4.GetAddressBytes());
		}
		if (ipv6 != null)
		{
			b |= hasIPv6;
			list.AddRange(ipv6.GetAddressBytes());
		}
		list[3] = b;
		return prefix + Convert.ToBase64String(list.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	// also takes a plain address for lan/testing
	public static List<IPEndPoint> Decode(string text, int defaultPort)
	{
		List<IPEndPoint> list = new List<IPEndPoint>();
		text = (text ?? "").Trim();
		if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		{
			string text2 = text.Substring(prefix.Length).Replace('-', '+').Replace('_', '/');
			byte[] array;
			try
			{
				array = Convert.FromBase64String(text2.PadRight(text2.Length + (4 - text2.Length % 4) % 4, '='));
			}
			catch (FormatException)
			{
				return list;
			}
			if (array.Length < 4 || array[0] != version)
			{
				return list;
			}
			int port = (array[1] << 8) | array[2];
			int num = 4;
			byte b = array[3];
			if ((b & hasPublic) != 0 && TryRead(array, ref num, 4, out var address))
			{
				list.Add(new IPEndPoint(address, port));
			}
			if ((b & hasLan) != 0 && TryRead(array, ref num, 4, out var address2))
			{
				list.Add(new IPEndPoint(address2, port));
			}
			if ((b & hasIPv6) != 0 && TryRead(array, ref num, 16, out var address3))
			{
				list.Add(new IPEndPoint(address3, port));
			}
			return list;
		}
		if (text.Equals("localhost", StringComparison.OrdinalIgnoreCase))
		{
			text = "127.0.0.1";
		}
		if (TryParseEndPoint(text, defaultPort, out var result))
		{
			list.Add(result);
		}
		return list;
	}

	private static bool TryParseEndPoint(string text, int defaultPort, out IPEndPoint endPoint)
	{
		endPoint = null;
		string ipString = text;
		int result = defaultPort;
		if (text.StartsWith("["))
		{
			int num = text.IndexOf(']');
			if (num < 0)
			{
				return false;
			}
			ipString = text.Substring(1, num - 1);
			if (num + 1 < text.Length && (text[num + 1] != ':' || !int.TryParse(text.Substring(num + 2), out result)))
			{
				return false;
			}
		}
		else if (text.Count((char c) => c == ':') == 1)
		{
			int num2 = text.IndexOf(':');
			ipString = text.Substring(0, num2);
			if (!int.TryParse(text.Substring(num2 + 1), out result))
			{
				return false;
			}
		}
		if (!IPAddress.TryParse(ipString, out var address) || result <= 0 || result > 65535)
		{
			return false;
		}
		endPoint = new IPEndPoint(address, result);
		return true;
	}

	private static bool TryRead(byte[] bytes, ref int offset, int length, out IPAddress address)
	{
		address = null;
		if (offset + length > bytes.Length)
		{
			return false;
		}
		byte[] array = new byte[length];
		Array.Copy(bytes, offset, array, 0, length);
		offset += length;
		address = new IPAddress(array);
		return true;
	}

	public static void FindLocalAddresses(out IPAddress lanIPv4, out IPAddress ipv6)
	{
		lanIPv4 = null;
		ipv6 = null;
		try
		{
			foreach (NetworkInterface item in from n in NetworkInterface.GetAllNetworkInterfaces()
				where n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
				orderby (n.GetIPProperties().GatewayAddresses.Count <= 0) ? 1 : 0
				select n)
			{
				foreach (UnicastIPAddressInformation unicastAddress in item.GetIPProperties().UnicastAddresses)
				{
					IPAddress address = unicastAddress.Address;
					// skip vpn adapters (hamachi 25.x, radmin 26.x)
					if (lanIPv4 == null && address.AddressFamily == AddressFamily.InterNetwork && IsPrivateIPv4(address))
					{
						lanIPv4 = address;
					}
					if (ipv6 == null && IsGlobalIPv6(address))
					{
						ipv6 = address;
					}
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static bool IsGlobalIPv6(IPAddress address)
	{
		if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || IPAddress.IsLoopback(address))
		{
			return false;
		}
		byte[] addressBytes = address.GetAddressBytes();
		// 2000::/3, skip teredo and 6to4
		if ((addressBytes[0] & 0xE0) != 32)
		{
			return false;
		}
		if (addressBytes[0] == 32 && addressBytes[1] == 1 && addressBytes[2] == 0 && addressBytes[3] == 0)
		{
			return false;
		}
		if (addressBytes[0] == 32 && addressBytes[1] == 2)
		{
			return false;
		}
		return true;
	}

	// 10/8, 172.16/12, 192.168/16
	private static bool IsPrivateIPv4(IPAddress address)
	{
		byte[] addressBytes = address.GetAddressBytes();
		if (addressBytes.Length != 4)
		{
			return false;
		}
		if (addressBytes[0] != 10 && (addressBytes[0] != 172 || (addressBytes[1] & 0xF0) != 16))
		{
			if (addressBytes[0] == 192)
			{
				return addressBytes[1] == 168;
			}
			return false;
		}
		return true;
	}

	// private + CGNAT, unreachable from outside
	public static bool IsUnreachableIPv4(IPAddress address)
	{
		byte[] addressBytes = address.GetAddressBytes();
		if (!IsPrivateIPv4(address))
		{
			if (addressBytes.Length == 4 && addressBytes[0] == 100)
			{
				return (addressBytes[1] & 0xC0) == 64;
			}
			return false;
		}
		return true;
	}
}
