using UnityEngine;
using UnityEngine.UI;

//TODO: THIS CAN BE DELETED (Logic Moved to PassthroughCameraAddOns.cs
public class MaterialToRawImage : MonoBehaviour
{
    [SerializeField] private Material material;
    [SerializeField] private RawImage rawImage;

    RenderTexture renderTexture;

    void Start()
    {
        renderTexture = new RenderTexture(1024, 1024, 0);
        renderTexture.Create();

        Graphics.Blit(null, renderTexture, material);
        rawImage.texture = renderTexture;
    }

    void Update()
    {
        Graphics.Blit(null, renderTexture, material);
    }
}