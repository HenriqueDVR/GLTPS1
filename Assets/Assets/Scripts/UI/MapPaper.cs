using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// map as a sheet of paper held in first person. picture gets inked once, markers go on top.
// own overlay camera so it never clips into walls
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

	private Texture lastPicture;

	private Camera overlay;

	private Camera overlayOf;

	private Transform sheet;

	private Texture2D arrow;

	private Texture2D dot;

	private Texture2D tower;

	private Texture2D[] rings;

	private struct Station
	{
		public Transform Where;
		public int Ring;
		public float Size;
		public float Angle;
	}

	private readonly List<Station> fuelStations = new List<Station>();

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
		Texture picture = owner.MapImage.texture;
		if (picture == null)
		{
			return;
		}
		if (picture != lastPicture)
		{
			lastPicture = picture;
			Graphics.Blit(picture, styled, style);
			FindFuelStations();
		}
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
		return owner != null && owner.MapImage != null;
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
		Matrix4x4 render = owner.RenderedViewProjection;
		Transform self = owner.transform;
		Vector2 you = ToPaper(render, self.position);
		float youSize = height * 0.06f;
		Route(render);
		foreach (Station station in fuelStations)
		{
			if (station.Where == null)
			{
				continue;
			}
			Vector2 at = ToPaper(render, station.Where.position);
			if (Inside(at))
			{
				Stamp(rings[station.Ring], at, height * 0.07f * station.Size, station.Angle, kanazawaRed);
			}
		}
		GameObject vehicle = GameObject.FindWithTag("Vehicle");
		if (vehicle != null)
		{
			Apart(vehicle.transform.position, render, you, youSize, ketColor, height * 0.035f);
		}
		Transform other = Partner(self);
		if (other != null)
		{
			bool down = CoopDowned.Instance != null && CoopDowned.Instance.RemoteDown;
			Apart(other.position, render, you, down ? 0f : youSize, down ? downColor : partnerColor, height * 0.035f);
		}
		// walking turns the model, not the parent
		PlayerMovement movement = owner.GetComponent<PlayerMovement>();
		Vector3 facing = movement != null && movement.Body != null ? movement.Body.forward : self.forward;
		Vector2 ahead = ToPaper(render, self.position + facing * 5f) - you;
		you.x = Mathf.Clamp(you.x, youSize, width - youSize);
		you.y = Mathf.Clamp(you.y, youSize, height - youSize);
		Stamp(arrow, you, youSize, Mathf.Atan2(ahead.y, ahead.x) - Mathf.PI * 0.5f, youColor);
		GL.PopMatrix();
		RenderTexture.active = previous;
	}

	private readonly List<Vector3> route = new List<Vector3>();

	private float nextRoute;

	// route in red pencil, from the layout since the traced line needs the pillar spawned
	private void Route(Matrix4x4 render)
	{
		if (Time.unscaledTime >= nextRoute)
		{
			nextRoute = Time.unscaledTime + 0.5f;
			route.Clear();
			WorldGenerator generator = WorldGenerator.Active ? GameSession.Get<WorldGenerator>() : null;
			if (generator != null && generator.Layout != null)
			{
				route.AddRange(generator.RouteToPillar(owner.transform.position));
			}
			else
			{
				NodeData data = FindObjectOfType<NodeData>();
				LineRenderer line = data != null ? data.PathLine : null;
				for (int i = 0; line != null && i < line.positionCount; i++)
				{
					route.Add(line.useWorldSpace ? line.GetPosition(i) : line.transform.TransformPoint(line.GetPosition(i)));
				}
			}
		}
		if (route.Count < 2)
		{
			return;
		}
		draw.mainTexture = Texture2D.whiteTexture;
		draw.SetPass(0);
		GL.Begin(GL.QUADS);
		GL.Color(kanazawaRed);
		const float pen = 3.6f;
		const float step = 7f;
		for (int i = 0; i + 1 < route.Count; i++)
		{
			Vector2 from = ToPaper(render, route[i]);
			Vector2 along = ToPaper(render, route[i + 1]) - from;
			int pieces = Mathf.Max(1, Mathf.CeilToInt(along.magnitude / step));
			Vector2 side = new Vector2(-along.y, along.x).normalized;
			Vector2 last = from;
			for (int k = 1; k <= pieces; k++)
			{
				// a slightly shaky hand
				float shake = (Mathf.PerlinNoise(i * 3.1f + k * 0.37f, 0.5f) - 0.5f) * 2.4f;
				Vector2 next = from + along * ((float)k / pieces) + side * (k == pieces ? 0f : shake);
				Vector2 width = new Vector2(-(next - last).y, (next - last).x).normalized * pen * 0.5f;
				GL.Vertex3(last.x - width.x, last.y - width.y, 0f);
				GL.Vertex3(last.x + width.x, last.y + width.y, 0f);
				GL.Vertex3(next.x + width.x, next.y + width.y, 0f);
				GL.Vertex3(next.x - width.x, next.y - width.y, 0f);
				last = next;
			}
		}
		GL.End();
		TowerMark(render);
	}

	// tower sketch, or an arrow at the edge when it's off the sheet
	private void TowerMark(Matrix4x4 render)
	{
		float margin = height * 0.07f;
		Vector2 end = ToPaper(render, route[route.Count - 1]);
		if (InsideBy(end, margin))
		{
			Stamp(tower, end, height * 0.1f, 0f, kanazawaRed);
			return;
		}
		for (int i = 0; i + 1 < route.Count; i++)
		{
			Vector2 a = ToPaper(render, route[i]);
			Vector2 b = ToPaper(render, route[i + 1]);
			if (!InsideBy(a, margin) || InsideBy(b, margin))
			{
				continue;
			}
			// where the line leaves the sheet
			float t = 1f;
			t = b.x > width - margin ? Mathf.Min(t, (width - margin - a.x) / (b.x - a.x)) : t;
			t = b.x < margin ? Mathf.Min(t, (margin - a.x) / (b.x - a.x)) : t;
			t = b.y > height - margin ? Mathf.Min(t, (height - margin - a.y) / (b.y - a.y)) : t;
			t = b.y < margin ? Mathf.Min(t, (margin - a.y) / (b.y - a.y)) : t;
			Vector2 edge = a + (b - a) * t;
			Vector2 away = (b - a).normalized;
			Stamp(arrow, edge, height * 0.05f, Mathf.Atan2(away.y, away.x) - Mathf.PI * 0.5f, kanazawaRed);
			Stamp(tower, edge - away * height * 0.075f, height * 0.08f, 0f, kanazawaRed);
			return;
		}
	}

	private static bool InsideBy(Vector2 at, float margin)
	{
		return at.x >= margin && at.x <= width - margin && at.y >= margin && at.y <= height - margin;
	}

	// skip when it's right on top of your arrow
	private void Apart(Vector3 world, Matrix4x4 render, Vector2 you, float youSize, Color color, float size)
	{
		Vector2 at = ToPaper(render, world);
		if (Inside(at) && Vector2.Distance(at, you) > youSize * 1.3f)
		{
			Stamp(dot, at, size, 0f, color);
		}
	}

	private static bool Inside(Vector2 at)
	{
		return at.x >= 0f && at.x <= width && at.y >= 0f && at.y <= height;
	}

	private static Vector2 ToPaper(Matrix4x4 render, Vector3 world)
	{
		Vector4 clip = render * new Vector4(world.x, world.y, world.z, 1f);
		Vector2 ndc = clip.w != 0f ? new Vector2(clip.x / clip.w, clip.y / clip.w) : Vector2.zero;
		return new Vector2((ndc.x + 1f) * 0.5f * width, (ndc.y + 1f) * 0.5f * height);
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

	// one mark per station, dry pumps (no spray) left out
	private void FindFuelStations()
	{
		fuelStations.Clear();
		foreach (GameObject station in GameObject.FindGameObjectsWithTag("FuelingStation"))
		{
			if (station.GetComponentInChildren<ParticleSystem>() == null)
			{
				continue;
			}
			Vector3 position = station.transform.position;
			bool known = false;
			foreach (Station other in fuelStations)
			{
				if (other.Where != null && (other.Where.position - position).sqrMagnitude < 25f)
				{
					known = true;
					break;
				}
			}
			if (!known)
			{
				System.Random hand = new System.Random(station.GetInstanceID());
				fuelStations.Add(new Station
				{
					Where = station.transform,
					Ring = hand.Next(rings.Length),
					Size = 0.8f + (float)hand.NextDouble() * 0.45f,
					Angle = (float)hand.NextDouble() * Mathf.PI * 2f
				});
			}
		}
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
