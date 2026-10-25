using System.Collections.Generic;
using UnityEngine;

// window mode, resolution and render resolution from the settings menu.
// unity remembers mode + resolution itself between launches, render height is ours
public static class DisplaySettings
{
	private static readonly FullScreenMode[] modes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

	// 0 = as tall as the window
	private static readonly int[] renderHeights = { 360, 480, 720, 0 };

	private const string renderKey = "psxRenderHeight";

	// what we asked for, Screen only catches up next frame
	private static FullScreenMode? requestedMode;

	private static Vector2Int? requestedSize;

	public static FullScreenMode Mode
	{
		get
		{
			FullScreenMode mode = requestedMode ?? Screen.fullScreenMode;
			return (mode == FullScreenMode.MaximizedWindow) ? FullScreenMode.Windowed : mode;
		}
	}

	public static Vector2Int Size => requestedSize ?? new Vector2Int(Screen.width, Screen.height);

	public static int RenderHeight
	{
		get
		{
			int saved = PlayerPrefs.GetInt(renderKey, 360);
			return System.Array.IndexOf(renderHeights, saved) >= 0 ? saved : 360;
		}
	}

	public static string ModeLabel
	{
		get
		{
			switch (Mode)
			{
			case FullScreenMode.ExclusiveFullScreen:
				return "display: fullscreen";
			case FullScreenMode.FullScreenWindow:
				return "display: borderless";
			default:
				return "display: windowed";
			}
		}
	}

	public static string SizeLabel => "resolution: " + Size.x + "x" + Size.y;

	public static string RenderLabel
	{
		get
		{
			switch (RenderHeight)
			{
			case 360:
				return "render: 360p (ps1)";
			case 0:
				return "render: native";
			default:
				return "render: " + RenderHeight + "p";
			}
		}
	}

	public static void NextMode()
	{
		int index = System.Array.IndexOf(modes, Mode);
		FullScreenMode mode = modes[(index + 1) % modes.Length];
		Vector2Int size = Size;
		// fullscreen modes need a size the screen actually has
		if (mode != FullScreenMode.Windowed && !Sizes().Contains(size))
		{
			List<Vector2Int> sizes = Sizes();
			size = (sizes.Count > 0) ? sizes[sizes.Count - 1] : size;
		}
		Apply(mode, size);
	}

	public static void NextSize()
	{
		List<Vector2Int> sizes = Sizes();
		if (sizes.Count == 0)
		{
			return;
		}
		int index = sizes.IndexOf(Size);
		Apply(Mode, sizes[(index + 1) % sizes.Count]);
	}

	public static void NextRenderHeight()
	{
		int index = System.Array.IndexOf(renderHeights, RenderHeight);
		PlayerPrefs.SetInt(renderKey, renderHeights[(index + 1) % renderHeights.Length]);
		PlayerPrefs.Save();
		PSXScreenFit.Refresh();
	}

	private static void Apply(FullScreenMode mode, Vector2Int size)
	{
		requestedMode = mode;
		requestedSize = size;
		if (mode == FullScreenMode.ExclusiveFullScreen)
		{
			// fastest refresh the screen has at that size, or it can drop to 60
			Resolution best = default;
			foreach (Resolution resolution in Screen.resolutions)
			{
				if (resolution.width == size.x && resolution.height == size.y && resolution.refreshRateRatio.value >= best.refreshRateRatio.value)
				{
					best = resolution;
				}
			}
			if (best.width > 0)
			{
				Screen.SetResolution(size.x, size.y, mode, best.refreshRateRatio);
				return;
			}
		}
		Screen.SetResolution(size.x, size.y, mode);
	}

	// each size once, smallest first
	private static List<Vector2Int> Sizes()
	{
		List<Vector2Int> sizes = new List<Vector2Int>();
		foreach (Resolution resolution in Screen.resolutions)
		{
			Vector2Int size = new Vector2Int(resolution.width, resolution.height);
			if (!sizes.Contains(size))
			{
				sizes.Add(size);
			}
		}
		sizes.Sort((Vector2Int a, Vector2Int b) => (a.x * a.y).CompareTo(b.x * b.y));
		return sizes;
	}
}
