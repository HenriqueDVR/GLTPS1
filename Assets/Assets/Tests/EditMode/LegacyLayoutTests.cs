using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// freezes the legacy generator (genVersion 1). old saves and co-op peers rebuild the world from the seed,
// so if one of these fails the change broke them. don't update a hash unless that's really what you want
public class LegacyLayoutTests
{
	private const string scenePath = "Assets/Scenes/SampleScene.unity";

	// seed, node count, hash of WorldLayout.Describe(). the last 5 came from real player logs
	private static readonly object[] golden =
	{
		new object[] { 1uL, 157, 0xdb584e8du },
		new object[] { 2uL, 107, 0xaa5a951fu },
		new object[] { 3uL, 122, 0x458cd7cfu },
		new object[] { 7uL, 148, 0x913c7de7u },
		new object[] { 42uL, 106, 0xbbd5466du },
		new object[] { 123uL, 127, 0x790573f8u },
		new object[] { 1000uL, 127, 0x5696a7dbu },
		new object[] { 4242uL, 182, 0x76b4a12eu },
		new object[] { 65535uL, 198, 0x77c8eff2u },
		new object[] { 99999uL, 111, 0x794700bbu },
		new object[] { 123456uL, 197, 0x0b4cfbb2u },
		new object[] { 777777uL, 279, 0xdbdcff09u },
		new object[] { 1000003uL, 65, 0x340358e3u },
		new object[] { 2147483646uL, 196, 0xa6ce1823u },
		new object[] { 9876543210uL, 151, 0x958089feu },
		new object[] { ulong.MaxValue, 138, 0xe238aec6u },
		new object[] { 672606546uL, 164, 0x7998e050u },
		new object[] { 1076690268uL, 219, 0x44a69a9au },
		new object[] { 150680302uL, 121, 0x1ed4ee3au },
		new object[] { 1383592446uL, 91, 0xaf43ac0bu },
		new object[] { 1833990471uL, 163, 0x5a70f2b5u }
	};

	private Scene scene;

	private bool openedScene;

	private Node start;

	private NodeData data;

	[OneTimeSetUp]
	public void OpenScene()
	{
		scene = SceneManager.GetSceneByPath(scenePath);
		if (!scene.isLoaded)
		{
			scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			openedScene = true;
		}
		start = WorldGenerator.FindStartNode();
		data = Object.FindObjectOfType<NodeData>();
		Assert.IsNotNull(start, "no start node in " + scenePath);
		Assert.IsNotNull(data, "no NodeData in " + scenePath);
	}

	[OneTimeTearDown]
	public void CloseScene()
	{
		if (openedScene)
		{
			EditorSceneManager.CloseScene(scene, true);
		}
	}

	private WorldLayout Build(ulong seed)
	{
		return WorldGenerator.BuildLayout(start, data, new Pose(start.transform.position, start.transform.rotation), seed);
	}

	[TestCaseSource(nameof(golden))]
	public void LayoutMatchesGoldenHash(ulong seed, int nodes, uint hash)
	{
		WorldLayout layout = Build(seed);
		Assert.AreEqual(nodes, layout.Nodes.Count, "node count, seed " + seed);
		Assert.AreEqual(hash.ToString("x8"), WorldLayout.Hash(layout.Describe()).ToString("x8"), "layout hash, seed " + seed);
	}

	// nothing carries over between builds
	[Test]
	public void BuildOrderDoesntMatter()
	{
		string first = Build(42uL).Describe();
		Build(7uL);
		Build(ulong.MaxValue);
		Assert.AreEqual(first, Build(42uL).Describe());
	}
}
