using System.Collections.Generic;
using Meta.XR.BuildingBlocks.AIBlocks;
using UnityEngine;

public class ObjectDetectionMetrics : MonoBehaviour
{
    [SerializeField] private ObjectDetectionAgent objectDetectionAgent;
    
    void Start()
    {
        if (objectDetectionAgent == null)
        {
            Debug.LogError("ObjectDetectionAgent component needs to be assigned");
            return;
        }
        objectDetectionAgent.OnBoxesUpdated += ObjectDetectionAgentOnOnBoxesUpdated;
    }

    private void ObjectDetectionAgentOnOnBoxesUpdated(List<BoxData> boxDataList)
    {
        float detectionStartTime = Time.realtimeSinceStartup;
        int index = 0;
        foreach (var boxData in boxDataList)
        {
            float detectionTime = Time.realtimeSinceStartup;
            float elapsedTime = detectionTime - detectionStartTime;
            Debug.Log($"Object Detected[{index++}] Time={detectionTime:F3}s Elapsed={elapsedTime:F3}s" +
                      $"(Label={boxData.label} Bounds Pos={boxData.position} Rot={boxData.rotation})");
        }
    }

    private void OnDestroy()
    {
        objectDetectionAgent.OnBoxesUpdated -= ObjectDetectionAgentOnOnBoxesUpdated;
    }
}
