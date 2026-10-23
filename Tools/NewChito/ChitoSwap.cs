using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// one off: remaps chito's anims to the new model's joint positions and swaps the textures.
// needs the old model copied to ChitoOld_tmp.fbx next to the new one
public static class ChitoSwap
{
    const string Folder = "Assets/Assets/Models/Chito/";
    const string Clips = "Assets/Assets/Animations/Chito";

    public static void Run()
    {
        Dictionary<string, Vector3> oldRest = RestLocalPositions(Folder + "ChitoOld_tmp.fbx");
        Dictionary<string, Vector3> newRest = RestLocalPositions(Folder + "Chito.fbx");
        Dictionary<string, Vector3> moved = new Dictionary<string, Vector3>();
        foreach (KeyValuePair<string, Vector3> bone in newRest)
        {
            if (oldRest.TryGetValue(bone.Key, out Vector3 old) && (bone.Value - old).sqrMagnitude > 1e-12f)
            {
                moved[bone.Key] = bone.Value - old;
                Debug.Log("ChitoSwap moved " + bone.Key + " old " + old.ToString("F4") + " new " + bone.Value.ToString("F4"));
            }
        }

        int changedCurves = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { Clips }))
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform) || !binding.propertyName.StartsWith("m_LocalPosition.") || !moved.TryGetValue(binding.path, out Vector3 delta))
                {
                    continue;
                }
                float shift = binding.propertyName.EndsWith("x") ? delta.x : binding.propertyName.EndsWith("y") ? delta.y : delta.z;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                Keyframe[] keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value += shift;
                }
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                changedCurves++;
            }
            EditorUtility.SetDirty(clip);
        }
        Debug.Log("ChitoSwap shifted " + changedCurves + " position curves");

        SetTexture(Folder + "M_Chito.001.mat", Folder + "T_ChitoBody_new.png");
        SetTexture(Folder + "M_ChitoHead.001.mat", Folder + "T_ChitoFace_new.png");
        AssetDatabase.SaveAssets();
        AssetDatabase.DeleteAsset(Folder + "ChitoOld_tmp.fbx");
        Debug.Log("ChitoSwap done");
    }

    static Dictionary<string, Vector3> RestLocalPositions(string path)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        SkinnedMeshRenderer smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        Matrix4x4[] bindposes = smr.sharedMesh.bindposes;
        Dictionary<Transform, Matrix4x4> rest = new Dictionary<Transform, Matrix4x4>();
        for (int i = 0; i < smr.bones.Length; i++)
        {
            rest[smr.bones[i]] = bindposes[i].inverse;
        }
        Matrix4x4 rendererSpace = smr.transform.worldToLocalMatrix;
        Dictionary<string, Vector3> positions = new Dictionary<string, Vector3>();
        foreach (KeyValuePair<Transform, Matrix4x4> bone in rest)
        {
            Transform parent = bone.Key.parent;
            Matrix4x4 parentRest = rest.TryGetValue(parent, out Matrix4x4 p) ? p : rendererSpace * parent.localToWorldMatrix;
            positions[AnimationUtility.CalculateTransformPath(bone.Key, model.transform)] = (parentRest.inverse * bone.Value).GetColumn(3);
        }
        return positions;
    }

    static void SetTexture(string materialPath, string texturePath)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_MainTex", texture);
        material.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(material);
    }
}
