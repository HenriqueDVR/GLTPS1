using UnityEditor;

// -executeMethod ProfileBuild.Build: development player so profiler markers can be read
public static class ProfileBuild
{
	public static void Build()
	{
		BuildPlayerOptions options = new BuildPlayerOptions
		{
			scenes = new[] { "Assets/Scenes/MenuScene.unity", "Assets/Scenes/SampleScene.unity" },
			locationPathName = "../GLTPS1_build_profile/GLTPS1.exe",
			target = BuildTarget.StandaloneWindows64,
			options = BuildOptions.Development
		};
		BuildPipeline.BuildPlayer(options);
	}
}
