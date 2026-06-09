using System.IO;
using UnityEditor;
using UnityEngine;

namespace MetaDyn.Editor
{
    public static class GLTFastShaderMaterialsCreator
    {
        private const string k_OutputFolder = "Assets/Resources/GLTFastShaders";

        // GUIDs stabili del package com.unity.cloud.gltfast (verificati su 6.x)
        private static readonly (string guid, string matName)[] k_Shaders =
        {
            ("b9d29dfa1474148e792ac720cbd45122", "gltfast_metallic"),
            ("c87047c884d9843f5b0f4cce282aa760", "gltfast_unlit"),
            ("9a07dad0f3c4e43ff8312e3b5fa42300", "gltfast_specular"),
        };

        [MenuItem("Tools/MetaDyn/Create GLTFast Shader Materials (WebGL Fix)")]
        public static void CreateMaterials()
        {
            if (!AssetDatabase.IsValidFolder(k_OutputFolder))
            {
                Directory.CreateDirectory(k_OutputFolder);
                AssetDatabase.Refresh();
            }

            int created = 0;
            foreach (var (guid, matName) in k_Shaders)
            {
                string shaderPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(shaderPath))
                {
                    Debug.LogWarning($"[GLTFastShaderMaterialsCreator] Shader GUID {guid} non trovato — skip.");
                    continue;
                }

                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (shader == null)
                {
                    Debug.LogWarning($"[GLTFastShaderMaterialsCreator] Impossibile caricare shader da {shaderPath} — skip.");
                    continue;
                }

                string matPath = $"{k_OutputFolder}/{matName}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null)
                {
                    Debug.Log($"[GLTFastShaderMaterialsCreator] Già esistente: {matPath}");
                    continue;
                }

                AssetDatabase.CreateAsset(new Material(shader), matPath);
                created++;
                Debug.Log($"[GLTFastShaderMaterialsCreator] Creato: {matPath}");
            }

            if (created > 0)
                AssetDatabase.SaveAssets();

            Debug.Log($"[GLTFastShaderMaterialsCreator] Completato — {created} materiale/i creato/i in {k_OutputFolder}.");
        }
    }
}
