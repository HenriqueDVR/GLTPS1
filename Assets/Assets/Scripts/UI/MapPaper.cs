using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

// map as a sheet of paper held in first person: Chito's pencil sketch of what's been seen (MapSheet), the marks of what
// was found there, you on top. north's up, arrow keys or the right stick move the sheet, the wheel or the shoulder buttons
// change the scale. own overlay camera so it never clips into walls
public class MapPaper : MonoBehaviour
{
	public static bool Holding { get; private set; }

	// background key, inked as bare paper
	public static readonly Color KeyColor = new Color(1f, 0f, 1f);

	private static MapPaper instance;

	private PlayerMapController owner;

	private const int width = 960;

	private const int height = 540;

	// unused layer
	private const int sheetLayer = 31;

	private const float liftTime = 0.25f;

	private const float sheetWidth = 0.56f;

	private float lift;

	private Material style;

	private Material draw;

	private Material sheetMaterial;

	private RenderTexture styled;

	private RenderTexture paper;

	// bare paper through the style shader
	private Texture2D keyTexture;

	private int drawnVersion = -1;

	private Vector2 drawnCentre;

	private float drawnScale;

	private int drawnLayer = -1;

	// layout xz of the sheet's middle, and how far you've moved it from yourself
	private Vector2 centre;

	private Vector2 pan;

	// metres a pixel
	private static readonly float[] scales = { 1.5f, 3f, 6f };

	private int scaleAt;

	private const float panPerSecond = 500f;

	private const float redrawMoved = 12f;

	private static readonly Color pencil = new Color(0.22f, 0.19f, 0.16f, 0.9f);

	private static readonly Color pencilSoft = new Color(0.3f, 0.26f, 0.22f, 0.45f);

	private static readonly Color voidInk = new Color(0.1f, 0.09f, 0.08f, 0.75f);

	private static readonly Color shelterInk = new Color(0.16f, 0.32f, 0.2f, 0.95f);

	private static readonly Color waterInk = new Color(0.14f, 0.3f, 0.62f, 0.95f);

	private Camera overlay;

	private Camera overlayOf;

	private Transform sheet;

	private Texture2D arrow;

	private Texture2D dot;

	private Texture2D tower;

	private Texture2D house;

	private Texture2D[] rings;

	private static readonly Color youColor = new Color(1f, 0.82f, 0.2f);

	private static readonly Color partnerColor = new Color(0.35f, 0.6f, 0.9f);

	private static readonly Color downColor = new Color(0.85f, 0.15f, 0.1f);

	private static readonly Color ketColor = new Color(0.85f, 0.5f, 0.2f);

	private static readonly Color kanazawaRed = new Color(0.72f, 0.12f, 0.1f, 0.9f);

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Create()
	{
		GameObject obj = new GameObject("MapPaper");
		DontDestroyOnLoad(obj);
		instance = obj.AddComponent<MapPaper>();
	}

	public static void Open(PlayerMapController map)
	{
		if (instance == null)
		{
			return;
		}
		instance.owner = map;
		instance.pan = Vector2.zero;
		Holding = true;
		PlayerCameraController camera = map.GetComponent<PlayerCameraController>();
		if (camera != null)
		{
			camera.HoldMap(true);
		}
	}

	public static void Close()
	{
		if (instance == null || !Holding)
		{
			return;
		}
		Holding = false;
		PlayerCameraController camera = instance.owner != null ? instance.owner.GetComponent<PlayerCameraController>() : null;
		if (camera != null)
		{
			camera.HoldMap(false);
		}
	}

	private void LateUpdate()
	{
		if (Holding && (owner == null || !owner.enabled || !owner.isMapEnabled || !InputOn()))
		{
			// got into the ket, swapped girls, died...
			if (owner != null && owner.isMapEnabled)
			{
				owner.CloseMap();
			}
			Close();
		}
		lift = Mathf.MoveTowards(lift, Holding ? 1f : 0f, Time.unscaledDeltaTime / liftTime);
		if (lift <= 0f)
		{
			if (sheet != null)
			{
				sheet.gameObject.SetActive(false);
			}
			return;
		}
		if (!Ready())
		{
			return;
		}
		Steer();
		Vector3 here = owner.transform.position + SaveScene.Offset();
		centre = new Vector2(here.x, here.z) + pan;
		DrawSheet();
		FaceView();
		Compose();
		// slides up from below, sized to fill most of the width
		overlay.fieldOfView = overlayOf.fieldOfView;
		float halfTan = Mathf.Tan(overlay.fieldOfView * 0.5f * Mathf.Deg2Rad);
		float distance = sheetWidth / (0.78f * 2f * halfTan * overlay.aspect);
		float visible = 2f * distance * halfTan;
		float eased = 1f - (1f - lift) * (1f - lift);
		sheet.gameObject.SetActive(true);
		sheet.localPosition = new Vector3(0f, Mathf.Lerp(-visible, -0.06f * visible, eased), distance);
		sheet.localRotation = Quaternion.Euler(Mathf.Lerp(60f, 16f, eased), 0f, Mathf.Lerp(-6f, -1.5f, eased));
	}

	private void Steer()
	{
		if (!Holding)
		{
			return;
		}
		Vector2 move = Vector2.zero;
		Keyboard keys = Keyboard.current;
		if (keys != null)
		{
			move.x += (keys.rightArrowKey.isPressed ? 1f : 0f) - (keys.leftArrowKey.isPressed ? 1f : 0f);
			move.y += (keys.upArrowKey.isPressed ? 1f : 0f) - (keys.downArrowKey.isPressed ? 1f : 0f);
		}
		Gamepad pad = Gamepad.current;
		if (pad != null)
		{
			// the d-pad's up closes the map
			move += pad.rightStick.ReadValue();
			if (pad.rightShoulder.wasPressedThisFrame)
			{
				scaleAt = Mathf.Min(scaleAt + 1, scales.Length - 1);
			}
			if (pad.leftShoulder.wasPressedThisFrame)
			{
				scaleAt = Mathf.Max(scaleAt - 1, 0);
			}
		}
		pan += move * panPerSecond * (scales[scaleAt] / scales[0]) * Time.unscaledDeltaTime;
		Mouse mouse = Mouse.current;
		if (mouse != null)
		{
			float wheel = mouse.scroll.ReadValue().y;
			if (wheel < 0f)
			{
				scaleAt = Mathf.Min(scaleAt + 1, scales.Length - 1);
			}
			else if (wheel > 0f)
			{
				scaleAt = Mathf.Max(scaleAt - 1, 0);
			}
		}
	}

	// the pencil part, only when something's changed
	private void DrawSheet()
	{
		WorldGenerator world = GameSession.Get<WorldGenerator>();
		int layer = (world != null) ? world.Layer : 0;
		float scale = scales[scaleAt];
		if (MapSheet.Version == drawnVersion && layer == drawnLayer && scale == drawnScale && (centre - drawnCentre).sqrMagnitude < redrawMoved * redrawMoved)
		{
			return;
		}
		drawnVersion = MapSheet.Version;
		drawnLayer = layer;
		drawnScale = scale;
		drawnCentre = centre;
		Graphics.Blit(keyTexture, styled, style);
		RenderTexture previous = RenderTexture.active;
		RenderTexture.active = styled;
		GL.PushMatrix();
		GL.LoadPixelMatrix(0f, width, 0f, height);
		draw.mainTexture = Texture2D.whiteTexture;
		draw.SetPass(0);
		GL.Begin(GL.QUADS);
		float halfX = width * 0.5f * scale + 140f;
		float halfZ = height * 0.5f * scale + 140f;
		float tileSize = (float)Gen2.TileSize;
		foreach (MapSheet.Tile tile in MapSheet.Tiles)
		{
			float tx = (tile.X + 0.5f) * tileSize;
			float tz = (tile.Z + 0.5f) * tileSize;
			if (Mathf.Abs(tx - drawnCentre.x) > halfX || Mathf.Abs(tz - drawnCentre.y) > halfZ)
			{
				continue;
			}
			GL.Color(voidInk);
			foreach (Vector2 cell in tile.Voids)
			{
				Rect(Sheet(cell), MapSheet.VoidCell * 0.5f / scale, MapSheet.VoidCell * 0.5f / scale, Vector2.right);
			}
			GL.Color(pencilSoft);
			for (int i = 0; i < tile.Lots.Count; i++)
			{
				Vector4 lot = tile.Lots[i];
				Rect(Sheet(new Vector2(lot.x, lot.y)), tile.LotHalves[i].x * 0.82f / scale, tile.LotHalves[i].y * 0.82f / scale, new Vector2(lot.z, lot.w));
			}
			GL.Color(pencil);
			for (int i = 0; i < tile.Roads.Count; i++)
			{
				Vector4 road = tile.Roads[i];
				Line(Sheet(new Vector2(road.x, road.y)), Sheet(new Vector2(road.z, road.w)), Mathf.Max(1.2f, tile.RoadWidths[i] * 0.6f / scale));
			}
		}
		GL.End();
		GL.PopMatrix();
		RenderTexture.active = previous;
	}

	private static void Rect(Vector2 middle, float halfU, float halfV, Vector2 axis)
	{
		Vector2 u = axis * halfU;
		Vector2 v = new Vector2(-axis.y, axis.x) * halfV;
		GL.Vertex3(middle.x - u.x - v.x, middle.y - u.y - v.y, 0f);
		GL.Vertex3(middle.x - u.x + v.x, middle.y - u.y + v.y, 0f);
		GL.Vertex3(middle.x + u.x + v.x, middle.y + u.y + v.y, 0f);
		GL.Vertex3(middle.x + u.x - v.x, middle.y + u.y - v.y, 0f);
	}

	private static void Line(Vector2 a, Vector2 b, float thick)
	{
		Vector2 side = new Vector2(-(b - a).y, (b - a).x).normalized * thick * 0.5f;
		GL.Vertex3(a.x - side.x, a.y - side.y, 0f);
		GL.Vertex3(a.x + side.x, a.y + side.y, 0f);
		GL.Vertex3(b.x + side.x, b.y + side.y, 0f);
		GL.Vertex3(b.x - side.x, b.y - side.y, 0f);
	}

	// layout xz to the sheet as it was last drawn
	private Vector2 Sheet(Vector2 layout)
	{
		return new Vector2(width * 0.5f + (layout.x - drawnCentre.x) / drawnScale, height * 0.5f + (layout.y - drawnCentre.y) / drawnScale);
	}

	// on foot she turns with the camera
	private void FaceView()
	{
		PlayerMovement movement = owner.GetComponent<PlayerMovement>();
		CharacterController controller = owner.GetComponent<CharacterController>();
		if (!Holding || movement == null || !movement.enabled || controller == null || !controller.enabled || movement.Body == null)
		{
			return;
		}
		movement.Body.rotation = Quaternion.Euler(0f, overlayOf.transform.eulerAngles.y, 0f);
	}

	private bool InputOn()
	{
		PlayerInputInitialize input = owner.GetComponent<PlayerInputInitialize>();
		return input == null || input.actions.Player.enabled || input.actions.Vehicle.enabled;
	}

	private bool Ready()
	{
		if (style == null)
		{
			style = new Material(Resources.Load<Shader>("MapPaperStyle"));
			draw = new Material(Resources.Load<Shader>("MapPaperDraw"));
			sheetMaterial = new Material(draw);
			styled = new RenderTexture(width, height, 0);
			paper = new RenderTexture(width, height, 0);
			// chunky like everything else
			styled.filterMode = FilterMode.Point;
			paper.filterMode = FilterMode.Point;
			MakeTextures();
			keyTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			keyTexture.SetPixel(0, 0, KeyColor);
			keyTexture.Apply();
		}
		Camera main = Camera.main;
		if (main == null)
		{
			return false;
		}
		if (overlayOf != main)
		{
			Attach(main);
		}
		return owner != null;
	}

	private void Attach(Camera main)
	{
		if (overlay != null)
		{
			Destroy(overlay.gameObject);
		}
		overlayOf = main;
		GameObject obj = new GameObject("MapPaper Camera");
		obj.transform.SetParent(main.transform, worldPositionStays: false);
		overlay = obj.AddComponent<Camera>();
		overlay.cullingMask = 1 << sheetLayer;
		overlay.nearClipPlane = 0.05f;
		overlay.farClipPlane = 5f;
		overlay.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
		main.GetUniversalAdditionalCameraData().cameraStack.Add(overlay);
		foreach (Camera other in FindObjectsOfType<Camera>(includeInactive: true))
		{
			if (other != overlay)
			{
				other.cullingMask &= ~(1 << sheetLayer);
			}
		}
		GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
		Destroy(quad.GetComponent<Collider>());
		quad.name = "MapPaper Sheet";
		quad.layer = sheetLayer;
		quad.transform.SetParent(obj.transform, worldPositionStays: false);
		quad.transform.localScale = new Vector3(sheetWidth, sheetWidth * height / width, 1f);
		Mesh mesh = quad.GetComponent<MeshFilter>().mesh;
		Color[] white = new Color[mesh.vertexCount];
		for (int i = 0; i < white.Length; i++)
		{
			white[i] = Color.white;
		}
		mesh.colors = white;
		sheetMaterial.mainTexture = paper;
		quad.GetComponent<MeshRenderer>().sharedMaterial = sheetMaterial;
		sheet = quad.transform;
	}

	private void Compose()
	{
		Graphics.Blit(styled, paper);
		RenderTexture previous = RenderTexture.active;
		RenderTexture.active = paper;
		GL.PushMatrix();
		GL.LoadPixelMatrix(0f, width, 0f, height);
		Transform self = owner.transform;
		Vector2 you = ToPaper(self.position);
		float youSize = height * 0.06f;
		Marks();
		GameObject vehicle = GameObject.FindWithTag("Vehicle");
		if (vehicle != null)
		{
			Apart(vehicle.transform.position, you, youSize, ketColor, height * 0.035f);
		}
		Transform other = Partner(self);
		if (other != null)
		{
			bool down = CoopDowned.Instance != null && CoopDowned.Instance.RemoteDown;
			Apart(other.position, you, down ? 0f : youSize, down ? downColor : partnerColor, height * 0.035f);
		}
		// walking turns the model, not the parent
		PlayerMovement movement = owner.GetComponent<PlayerMovement>();
		Vector3 facing = movement != null && movement.Body != null ? movement.Body.forward : self.forward;
		you.x = Mathf.Clamp(you.x, youSize, width - youSize);
		you.y = Mathf.Clamp(you.y, youSize, height - youSize);
		Stamp(arrow, you, youSize, Mathf.Atan2(facing.z, facing.x) - Mathf.PI * 0.5f, youColor);
		GL.PopMatrix();
		RenderTexture.active = previous;
	}

	// what's been found: only on tiles that have been seen, so there's nothing here you haven't been near
	private void Marks()
	{
		float size = height * 0.075f;
		foreach (MapSheet.Tile tile in MapSheet.Tiles)
		{
			foreach (MapSheet.Mark mark in tile.Marks)
			{
				Vector2 at = PaperOf(mark.At);
				if (!Inside(at))
				{
					continue;
				}
				switch (mark.Kind)
				{
				case MapSheet.MarkKind.Fuel:
					Stamp(rings[0], at, size, 0.3f, kanazawaRed);
					break;
				case MapSheet.MarkKind.Water:
					Stamp(rings[1], at, size, 1.1f, waterInk);
					break;
				case MapSheet.MarkKind.Shelter:
					Stamp(house, at, size * 0.8f, 0f, shelterInk);
					break;
				case MapSheet.MarkKind.TowerUp:
					Stamp(tower, at, height * 0.1f, 0f, kanazawaRed);
					break;
				default:
					Stamp(tower, at, height * 0.1f, 0f, pencil);
					break;
				}
			}
		}
	}

	// skip when it's right on top of your arrow
	private void Apart(Vector3 world, Vector2 you, float youSize, Color color, float size)
	{
		Vector2 at = ToPaper(world);
		if (Inside(at) && Vector2.Distance(at, you) > youSize * 1.3f)
		{
			Stamp(dot, at, size, 0f, color);
		}
	}

	private static bool Inside(Vector2 at)
	{
		return at.x >= 0f && at.x <= width && at.y >= 0f && at.y <= height;
	}

	private Vector2 ToPaper(Vector3 world)
	{
		Vector3 layout = world + SaveScene.Offset();
		return PaperOf(new Vector2(layout.x, layout.z));
	}

	private Vector2 PaperOf(Vector2 layout)
	{
		return new Vector2(width * 0.5f + (layout.x - centre.x) / scales[scaleAt], height * 0.5f + (layout.y - centre.y) / scales[scaleAt]);
	}

	private void Stamp(Texture2D texture, Vector2 at, float size, float angle, Color color)
	{
		draw.mainTexture = texture;
		draw.SetPass(0);
		float c = Mathf.Cos(angle) * size * 0.5f;
		float s = Mathf.Sin(angle) * size * 0.5f;
		GL.Begin(GL.QUADS);
		GL.Color(color);
		GL.TexCoord2(0f, 0f);
		GL.Vertex3(at.x - c + s, at.y - s - c, 0f);
		GL.TexCoord2(0f, 1f);
		GL.Vertex3(at.x - c - s, at.y - s + c, 0f);
		GL.TexCoord2(1f, 1f);
		GL.Vertex3(at.x + c - s, at.y + s + c, 0f);
		GL.TexCoord2(1f, 0f);
		GL.Vertex3(at.x + c + s, at.y + s - c, 0f);
		GL.End();
	}

	private static Transform Partner(Transform self)
	{
		if (CoopPlayers.Instance != null)
		{
			return CoopPlayers.Instance.Remote;
		}
		foreach (PlayerMapController controller in FindObjectsOfType<PlayerMapController>(includeInactive: true))
		{
			if (controller.transform != self && controller.gameObject.activeInHierarchy)
			{
				return controller.transform;
			}
		}
		return null;
	}

	private void MakeTextures()
	{
		const int size = 48;
		arrow = Blank(size);
		dot = Blank(size);
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float u = (x + 0.5f) / size;
				float v = (y + 0.5f) / size;
				// pointing up, outlined
				float inside = ArrowInside(u, v);
				bool outline = inside <= 0f && (ArrowInside(u + 0.06f, v) > 0f || ArrowInside(u - 0.06f, v) > 0f || ArrowInside(u, v + 0.06f) > 0f || ArrowInside(u, v - 0.06f) > 0f);
				arrow.SetPixel(x, y, inside > 0f ? Color.white : outline ? new Color(0.12f, 0.09f, 0.07f, 1f) : Color.clear);
				float distance = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
				dot.SetPixel(x, y, distance < 0.3f ? Color.white : distance < 0.42f ? new Color(0.12f, 0.09f, 0.07f, 1f) : Color.clear);
			}
		}
		// little lattice tower
		tower = Blank(size);
		Pencil(tower, new Vector2(0.33f, 0.06f), new Vector2(0.45f, 0.8f), 0.035f);
		Pencil(tower, new Vector2(0.67f, 0.06f), new Vector2(0.55f, 0.8f), 0.035f);
		for (int k = 0; k < 4; k++)
		{
			float y0 = 0.08f + k * 0.18f;
			float y1 = y0 + 0.18f;
			Pencil(tower, new Vector2(0.34f + y0 * 0.15f, y0), new Vector2(0.66f - y1 * 0.15f, y1), 0.025f);
		}
		Pencil(tower, new Vector2(0.36f, 0.8f), new Vector2(0.64f, 0.8f), 0.035f);
		Pencil(tower, new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.96f), 0.03f);
		tower.Apply();
		// a roof and a door: somewhere to spend the night
		house = Blank(size);
		Pencil(house, new Vector2(0.2f, 0.15f), new Vector2(0.2f, 0.55f), 0.04f);
		Pencil(house, new Vector2(0.8f, 0.15f), new Vector2(0.8f, 0.55f), 0.04f);
		Pencil(house, new Vector2(0.2f, 0.15f), new Vector2(0.8f, 0.15f), 0.04f);
		Pencil(house, new Vector2(0.1f, 0.5f), new Vector2(0.5f, 0.88f), 0.04f);
		Pencil(house, new Vector2(0.9f, 0.5f), new Vector2(0.5f, 0.88f), 0.04f);
		Pencil(house, new Vector2(0.5f, 0.15f), new Vector2(0.5f, 0.38f), 0.05f);
		house.Apply();
		arrow.Apply();
		dot.Apply();
		// felt tip loops, all a bit different
		System.Random hand = new System.Random(7);
		rings = new Texture2D[5];
		for (int r = 0; r < rings.Length; r++)
		{
			rings[r] = Blank(size);
			float turns = 0.95f + (float)hand.NextDouble() * 0.3f;
			float start = (float)hand.NextDouble() * Mathf.PI * 2f;
			float radius = 0.3f + (float)hand.NextDouble() * 0.08f;
			float squash = 0.78f + (float)hand.NextDouble() * 0.22f;
			float wobble = 0.015f + (float)hand.NextDouble() * 0.035f;
			float wobbles = 2f + hand.Next(3);
			float drift = (float)hand.NextDouble() * 0.07f;
			float pressure = 0.028f + (float)hand.NextDouble() * 0.018f;
			for (float t = 0f; t < turns; t += 0.002f)
			{
				float angle = t * Mathf.PI * 2f + start;
				float reach = radius + wobble * Mathf.Sin(angle * wobbles + start) + drift * t;
				float pen = pressure + 0.012f * Mathf.Sin(t * 9f + start);
				Vector2 centre = new Vector2(0.5f + Mathf.Cos(angle) * reach, 0.5f + Mathf.Sin(angle) * reach * squash);
				for (int y = 0; y < size; y++)
				{
					for (int x = 0; x < size; x++)
					{
						if (Vector2.Distance(new Vector2((x + 0.5f) / size, (y + 0.5f) / size), centre) < pen)
						{
							rings[r].SetPixel(x, y, Color.white);
						}
					}
				}
			}
			rings[r].Apply();
		}
	}

	private static void Pencil(Texture2D texture, Vector2 from, Vector2 to, float pen)
	{
		int size = texture.width;
		for (float t = 0f; t <= 1f; t += 0.01f)
		{
			Vector2 centre = Vector2.Lerp(from, to, t);
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					if (Vector2.Distance(new Vector2((x + 0.5f) / size, (y + 0.5f) / size), centre) < pen)
					{
						texture.SetPixel(x, y, Color.white);
					}
				}
			}
		}
	}

	private static float ArrowInside(float u, float v)
	{
		return v > 0.12f && Mathf.Abs(u - 0.5f) < (0.92f - v) * 0.42f ? 1f : 0f;
	}

	private static Texture2D Blank(int size)
	{
		Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
		texture.filterMode = FilterMode.Point;
		texture.wrapMode = TextureWrapMode.Clamp;
		Color[] clear = new Color[size * size];
		texture.SetPixels(clear);
		return texture;
	}
}
