using System.Collections.Generic;
using System.Linq;
using Meta.XR;
using Meta.XR.BuildingBlocks.AIBlocks;
using Meta.XR.EnvironmentDepth;
using UnityEngine;

[RequireComponent(typeof(ObjectDetectionAgent), typeof(DepthTextureAccess), typeof(EnvironmentDepthManager))]
public class ObjectDetectionVisualizerV2 : MonoBehaviour
{
    [SerializeField] private GameObject boundingBoxPrefab;
    [SerializeField] private bool showBoundingBoxes = true;

    /// <summary>
    /// Global toggle for all currently spawned and future bounding‑box renderers.
    /// </summary>
    public bool ShowBoundingBoxes
    {
        get => showBoundingBoxes;
        set
        {
            if (showBoundingBoxes == value) return;
            showBoundingBoxes = value;
            // Toggle every live quad / label renderer.
            foreach (var g in _live)
            {
                if (!g) continue;
                var rc = g.GetComponent<RendererCache>() ?? g.AddComponent<RendererCache>();
                foreach (var r in rc.Renderers)
                    r.enabled = value;
            }
        }
    }

#if UNITY_EDITOR
        // Ensures that ticking / unticking the checkbox in the Inspector during Play‑mode
        // actually runs the setter above.
        private void OnValidate() => ShowBoundingBoxes = showBoundingBoxes;
#endif

    /// <summary>
    /// Convenience wrapper for runtime UI (e.g. Toggle.onValueChanged).
    /// </summary>
    public void SetShowBoundingBoxes(bool value) => ShowBoundingBoxes = value;

    private ObjectDetectionAgent _agent;
    private readonly List<GameObject> _live = new();
    private readonly Queue<GameObject> _pool = new();

    private PassthroughCameraAccess _cam;
    private DepthTextureAccess _depth;
    private int _eyeIdx;

    private struct FrameData
    {
        public Pose Pose;
        public PassthroughCameraAccess.CameraIntrinsics CameraIntrinsics;
        public float[] Depth;
        public Matrix4x4[] ViewProjectionMatrix;
    }

    private FrameData _frame;

    private void Awake()
    {
        _agent = GetComponent<ObjectDetectionAgent>();
        _cam = FindAnyObjectByType<PassthroughCameraAccess>();
        _depth = GetComponent<DepthTextureAccess>();
        _eyeIdx = _cam.CameraPosition == PassthroughCameraAccess.CameraPositionType.Left ? 0 : 1;
    }
    
    private void OnEnable()
    {
        _agent.OnBoxesUpdated += HandleBatch;
        _depth.OnDepthTextureUpdateCPU += OnDepth;
    }

    private void OnDisable()
    {
        _agent.OnBoxesUpdated -= HandleBatch;
        _depth.OnDepthTextureUpdateCPU -= OnDepth;
    }

    private void OnDepth(DepthTextureAccess.DepthFrameData d)
    {
        _frame.Pose = _cam.GetCameraPose();
        _frame.CameraIntrinsics = _cam.Intrinsics;
        _frame.Depth = d.DepthTexturePixels.ToArray();
        _frame.ViewProjectionMatrix = d.ViewProjectionMatrix.ToArray();
    }

    private void HandleBatch(List<BoxData> batch)
    {
        Debug.Log($"[ObjectDetectionVisualizer] HandleBatch called with {batch.Count} detections");

        // recycle previous quads / labels
        foreach (var g in _live)
        {
            g.SetActive(false);
            _pool.Enqueue(g);
        }
        _live.Clear();
        
        if (boundingBoxPrefab == null)
        {
            Debug.LogError("[ObjectDetectionVisualizer] boundingBoxPrefab is null! Cannot create bounding boxes.");
            return;
        }

        int projected = 0;
        foreach (var b in batch)
        {
            var xmin = b.position.x;
            var ymin = b.position.y;
            var xmax = b.scale.x;
            var ymax = b.scale.y;

            Debug.Log($"[ObjectDetectionVisualizer] Processing detection: {b.label} at ({xmin},{ymin},{xmax},{ymax})");

            if (!TryProject(xmin, ymin, xmax, ymax, out var pos, out var rot, out var scl))
            {
                Debug.Log($"[ObjectDetectionVisualizer] TryProject failed for detection {b.label}");
                continue;
            }

            projected++;
            Debug.Log($"[ObjectDetectionVisualizer] Projected {b.label} to world pos: {pos}, scale: {scl}");

            // quad
            var quad = _pool.Count > 0 ? _pool.Dequeue() : Instantiate(boundingBoxPrefab);
            quad.SetActive(true);

            // Renderer cache (created once per pooled object)
            var rc = quad.GetComponent<RendererCache>() ?? quad.AddComponent<RendererCache>();
            foreach (var r in rc.Renderers) r.enabled = showBoundingBoxes;

            quad.transform.SetPositionAndRotation(pos, rot);
            quad.transform.localScale = scl;
            _live.Add(quad);

            // label
            var lbl = _pool.Count > 0 ? _pool.Dequeue() : new GameObject("Label");
            lbl.SetActive(true);
            if (lbl.TryGetComponent<Renderer>(out var lr))
                lr.enabled = showBoundingBoxes;

            var tm = lbl.GetComponent<TextMesh>() ?? lbl.AddComponent<TextMesh>();
            tm.text = b.label;
            tm.fontSize = 24;
            tm.characterSize = .02f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;

            lbl.transform.SetPositionAndRotation(pos + Vector3.up * .02f, rot);
            _live.Add(lbl);
        }
        Debug.Log($"[ObjectDetectionVisualizer] Successfully projected {projected}/{batch.Count} detections. Created {_live.Count} GameObjects.");
    }
    
     public bool TryProject(float xmin, float ymin, float xmax, float ymax,
            out Vector3 world, out Quaternion rot, out Vector3 scale)
    {
        world = default;
        rot = default;
        scale = default;

        var cameraTexture = _cam.GetTexture();
        if (cameraTexture == null)
        {
            return false;
        }

        var px = (xmin + xmax) * 0.5f;
        var py = (ymin + ymax) * 0.5f;

        var normalizedCenterX = px / cameraTexture.width;
        var normalizedCenterY = py / cameraTexture.height;

        var ray = _cam.ViewportPointToRay(new Vector2(normalizedCenterX, 1.0f - normalizedCenterY), _frame.Pose);

        var world1M = ray.origin + ray.direction;
        var clip = _frame.ViewProjectionMatrix[_eyeIdx] * new Vector4(world1M.x, world1M.y, world1M.z, 1f);
        if (clip.w <= 0)
        {
            return false;
        }

        var uv = (new Vector2(clip.x, clip.y) / clip.w) * 0.5f + Vector2.one * 0.5f;

        if (_depth == null)
        {
            return false;
        }

        var texSize = DepthTextureAccess.TextureSize;
        var sx = Mathf.Clamp((int)(uv.x * texSize), 0, texSize - 1);
        var sy = Mathf.Clamp((int)(uv.y * texSize), 0, texSize - 1);
        var idx = _eyeIdx * texSize * texSize + sy * texSize + sx;
        var d = _frame.Depth[idx];

        if (d <= 0 || d > 20 || float.IsInfinity(d))
        {
            return false;
        }

        world = ray.origin + ray.direction * d;
        rot = Quaternion.LookRotation(world - _frame.Pose.position);

        var normalizedWidth = (xmax - xmin) / cameraTexture.width;
        var normalizedHeight = (ymax - ymin) / cameraTexture.height;

        var rayLeft = _cam.ViewportPointToRay(new Vector2(normalizedCenterX - normalizedWidth * 0.5f, 1.0f - normalizedCenterY), _frame.Pose);
        var rayRight = _cam.ViewportPointToRay(new Vector2(normalizedCenterX + normalizedWidth * 0.5f, 1.0f - normalizedCenterY), _frame.Pose);
        var rayTop = _cam.ViewportPointToRay(new Vector2(normalizedCenterX, 1.0f - (normalizedCenterY - normalizedHeight * 0.5f)), _frame.Pose);
        var rayBottom = _cam.ViewportPointToRay(new Vector2(normalizedCenterX, 1.0f - (normalizedCenterY + normalizedHeight * 0.5f)), _frame.Pose);

        var worldLeft = rayLeft.origin + rayLeft.direction * d;
        var worldRight = rayRight.origin + rayRight.direction * d;
        var worldTop = rayTop.origin + rayTop.direction * d;
        var worldBottom = rayBottom.origin + rayBottom.direction * d;

        var w = Vector3.Distance(worldLeft, worldRight);
        var h = Vector3.Distance(worldTop, worldBottom);
        scale = new Vector3(w, h, 1f);

        return true;
    }

    /// <summary>
    /// Tiny per-object renderer cache to avoid GetComponentsInChildren each frame.
    /// </summary>
    private sealed class RendererCache : MonoBehaviour
    {
        public Renderer[] Renderers;

        private void Awake()
        {
            Renderers = GetComponentsInChildren<Renderer>(true);
        }
    }
}
