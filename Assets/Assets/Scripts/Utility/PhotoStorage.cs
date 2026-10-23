using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

// photos saved as pngs next to the save data
public static class PhotoStorage
{
	public static string Folder => Path.Combine(Application.persistentDataPath, "Photos");

	public static void Save(Texture2D photo)
	{
		byte[] png = photo.EncodeToPNG();
		string path = Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".png");
		Task.Run(delegate
		{
			try
			{
				Directory.CreateDirectory(Folder);
				File.WriteAllBytes(path, png);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		});
	}

	// names are timestamps so sorted = oldest first
	public static List<string> GetPhotoPaths()
	{
		if (!Directory.Exists(Folder))
		{
			return new List<string>();
		}
		return Directory.GetFiles(Folder, "*.png").OrderBy((string path) => Path.GetFileName(path), StringComparer.Ordinal).ToList();
	}

	public static Texture2D Load(string path)
	{
		Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: false);
		if (!texture2D.LoadImage(File.ReadAllBytes(path)))
		{
			UnityEngine.Object.Destroy(texture2D);
			return null;
		}
		MakeDisplayOnly(texture2D);
		return texture2D;
	}

	// call after Save
	public static void MakeDisplayOnly(Texture2D photo)
	{
		photo.Compress(highQuality: true);
		photo.Apply(updateMipmaps: false, makeNoLongerReadable: true);
	}
}
