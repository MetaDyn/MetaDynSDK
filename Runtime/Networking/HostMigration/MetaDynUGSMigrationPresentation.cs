using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MetaDyn.Networking.HostMigration
{
    /// <summary>
    /// Keeps a frozen local visual and camera anchor alive while UGS restarts NGO.
    /// The proxy contains no networking, input, colliders, or gameplay scripts.
    /// </summary>
    internal sealed class MetaDynUGSMigrationPresentation
    {
        private readonly List<Mesh> _bakedMeshes = new List<Mesh>();
        private GameObject _root;
        private Transform _followTarget;

        public bool IsActive => _root != null;
        public bool HasVisuals => _root != null && _root.transform.childCount > 0;

        public void Capture(MetaDynUGSPlayerController player)
        {
            if (player == null) return;

            Release();

            _root = new GameObject("MetaDynMigrationPresentation");
            _root.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
            Object.DontDestroyOnLoad(_root);

            Renderer[] renderers = player.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer source = renderers[i];
                if (source == null || !source.enabled || !source.gameObject.activeInHierarchy)
                    continue;

                if (source is SkinnedMeshRenderer skinnedMeshRenderer)
                {
                    CaptureSkinnedRenderer(skinnedMeshRenderer);
                }
                else if (source is MeshRenderer meshRenderer)
                {
                    CaptureMeshRenderer(meshRenderer);
                }
            }

            CameraFollow cameraFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cameraFollow != null)
            {
                cameraFollow.target = _root.transform;
                cameraFollow.inputSource = null;
                cameraFollow.isTargetMoving = false;
            }

            Debug.Log($"[UGS MIGRATION] Preserved local avatar presentation with {_root.transform.childCount} renderer snapshots.");
        }

        public void AttachTo(Transform replacementPlayer)
        {
            if (_root == null || replacementPlayer == null) return;

            _followTarget = replacementPlayer;
            _root.transform.SetPositionAndRotation(replacementPlayer.position, replacementPlayer.rotation);
        }

        public void UpdateFollowTarget()
        {
            if (_root == null || _followTarget == null) return;

            _root.transform.SetPositionAndRotation(_followTarget.position, _followTarget.rotation);
        }

        public void Release()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            _followTarget = null;

            for (int i = 0; i < _bakedMeshes.Count; i++)
            {
                if (_bakedMeshes[i] != null)
                    Object.Destroy(_bakedMeshes[i]);
            }

            _bakedMeshes.Clear();
        }

        private void CaptureSkinnedRenderer(SkinnedMeshRenderer source)
        {
            var bakedMesh = new Mesh
            {
                name = $"{source.name}_MigrationBake"
            };
            source.BakeMesh(bakedMesh, true);
            _bakedMeshes.Add(bakedMesh);

            GameObject visual = CreateVisualObject(source);
            var meshFilter = visual.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = bakedMesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            CopyRendererSettings(source, renderer);

            visual.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            visual.transform.localScale = Vector3.one;
        }

        private void CaptureMeshRenderer(MeshRenderer source)
        {
            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null) return;

            GameObject visual = CreateVisualObject(source);
            var meshFilter = visual.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = sourceFilter.sharedMesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            CopyRendererSettings(source, renderer);

            visual.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            visual.transform.localScale = source.transform.lossyScale;
        }

        private GameObject CreateVisualObject(Renderer source)
        {
            var visual = new GameObject($"{source.name}_MigrationVisual");
            visual.layer = source.gameObject.layer;
            visual.transform.SetParent(_root.transform, true);
            return visual;
        }

        private static void CopyRendererSettings(Renderer source, MeshRenderer destination)
        {
            destination.sharedMaterials = source.sharedMaterials;
            destination.shadowCastingMode = source.shadowCastingMode;
            destination.receiveShadows = source.receiveShadows;
            destination.lightProbeUsage = LightProbeUsage.Off;
            destination.reflectionProbeUsage = source.reflectionProbeUsage;
            destination.sortingLayerID = source.sortingLayerID;
            destination.sortingOrder = source.sortingOrder;

            var propertyBlock = new MaterialPropertyBlock();
            source.GetPropertyBlock(propertyBlock);
            destination.SetPropertyBlock(propertyBlock);
        }
    }
}
