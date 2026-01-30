using Meta.XR;
using Meta.XR.BuildingBlocks.AIBlocks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Toggle = UnityEngine.UI.Toggle;

public class ImageAnalysisController : MonoBehaviour
{
    [SerializeField] private LlmAgent llmAgent;
    [SerializeField] private PassthroughCameraAccess passthroughCameraAccess;
    
    [Header("UI Bindings")]
    [SerializeField] private RawImage liveImage;
    [SerializeField] private RawImage capturedImage;
    [SerializeField] private Toggle captureButton;
    [SerializeField] private QuestTMPKeyboard promptKeyboardText;
    [SerializeField] private RectTransform llmResponseScrollView;
    
    private TextMeshProUGUI capturedText;
    private TextMeshProUGUI llmResponseText;
    private RenderTexture renderTexture;
    private Texture2D capturedFrame;
    private bool capturingInProgress;
    
    private bool CapturingInProgress
    {
        get => capturingInProgress;
        set
        {
            capturingInProgress = value;
            liveImage.gameObject.SetActive(!value);
            capturedImage.gameObject.SetActive(value);
            capturedText.text = value ? "Clear Capture" : "Capture";
            llmResponseScrollView.gameObject.SetActive(value);
        }
    }

    private void Awake()
    {
        if (passthroughCameraAccess == null)
        {
            Debug.LogError("[PassthroughCameraAddOns] PassthroughCameraAccess component not found!");
            return;
        }

        capturedText = captureButton.GetComponentInChildren<TextMeshProUGUI>();
        llmResponseText = llmResponseScrollView.GetComponentInChildren<TextMeshProUGUI>();
        llmResponseText.text = string.Empty;
        CapturingInProgress = false;
        renderTexture = new RenderTexture(1024, 1024, 0);
        renderTexture.Create();

        if (passthroughCameraAccess.TargetMaterial != null)
        {
            Graphics.Blit(null, renderTexture, passthroughCameraAccess.TargetMaterial);
            liveImage.texture = renderTexture;
        }

        captureButton.onValueChanged.AddListener((_) =>
        {
            if (CapturingInProgress)
            {
                CapturingInProgress = false;
            }
            else
            {
                CapturingInProgress = true;
                CaptureFrame();   
            }
        });
        
        llmAgent.onResponseReceived.AddListener(response =>
        {
            llmResponseText.text = response;
            Debug.Log("Response received: " + response);
        });
    }
    
    void Update()
    {
        if (passthroughCameraAccess.TargetMaterial != null && !CapturingInProgress)
        {
            Graphics.Blit(null, renderTexture, passthroughCameraAccess.TargetMaterial);
        }
    }

    private void CaptureFrame()
    {
        if (passthroughCameraAccess == null)
        {
            Debug.LogError("[PassthroughCameraAddOns] PassthroughCameraAccess is null. Cannot capture frame.");
            return;
        }

        var sourceTexture = passthroughCameraAccess.GetTexture();
        if (sourceTexture == null)
        {
            Debug.LogWarning("[PassthroughCameraAddOns] Passthrough camera texture is not available yet.");
            return;
        }

        // Create or resize the Texture2D if needed
        if (capturedFrame == null || 
            capturedFrame.width != sourceTexture.width || 
            capturedFrame.height != sourceTexture.height)
        {
            if (capturedFrame != null)
            {
                Destroy(capturedFrame);
            }
            capturedFrame = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
        }

        // Copy the texture data using a temporary RenderTexture
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture tempRT = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.ARGB32);
        
        Graphics.Blit(sourceTexture, tempRT);
        RenderTexture.active = tempRT;
        
        capturedFrame.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
        capturedFrame.Apply();
        
        RenderTexture.active = currentRT;
        RenderTexture.ReleaseTemporary(tempRT);

        if (capturedFrame != null)
        {
            Debug.Log($"[PassthroughCameraAddOns] Frame captured: {capturedFrame.width}x{capturedFrame.height}");
            capturedImage.texture = capturedFrame;
            _ = llmAgent.SendPromptAsync(promptKeyboardText.KeyboardText, capturedFrame);
        }
    }
    
    private void OnDestroy()
    {
        if (capturedFrame != null)
        {
            Destroy(capturedFrame);
            capturedFrame = null;
        }
    }
}