#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MetaDyn.Editor
{
    internal sealed class MetaDynGltfShaderBuildPreprocessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string GltfShaderPrefix = "Packages/com.unity.cloud.gltfast/Runtime/Shader/";

        private static readonly string[] GltfShaderPaths =
        {
            GltfShaderPrefix + "glTF-pbrMetallicRoughness.shadergraph",
            GltfShaderPrefix + "glTF-pbrSpecularGlossiness.shadergraph",
            GltfShaderPrefix + "glTF-unlit.shadergraph",
            GltfShaderPrefix + "URP/glTF-pbrMetallicRoughness-Clearcoat.shadergraph"
        };

        private static Object[] _originalPreloadedAssets;
        private static bool _restorePreloadedAssets;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL)
                return;

            _originalPreloadedAssets = PlayerSettings.GetPreloadedAssets();
            var preloadedAssets = new List<Object>(_originalPreloadedAssets ?? Array.Empty<Object>());
            int added = 0;

            foreach (string shaderPath in GltfShaderPaths)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (shader == null)
                {
                    Debug.LogWarning($"[MetaDyn] glTFast shader not found for WebGL preload: {shaderPath}");
                    continue;
                }

                if (preloadedAssets.Any(asset => asset == shader))
                    continue;

                preloadedAssets.Add(shader);
                added++;
            }

            if (added == 0)
                return;

            PlayerSettings.SetPreloadedAssets(preloadedAssets.ToArray());
            _restorePreloadedAssets = true;
            Debug.Log($"[MetaDyn] Preloaded {added} glTFast shader(s) for WebGL runtime GLB avatar imports.");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!_restorePreloadedAssets)
                return;

            PlayerSettings.SetPreloadedAssets(_originalPreloadedAssets ?? Array.Empty<Object>());
            _restorePreloadedAssets = false;
            _originalPreloadedAssets = null;
        }
    }
}
#endif
