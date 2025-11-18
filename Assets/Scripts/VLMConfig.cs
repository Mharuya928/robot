using UnityEngine;

[CreateAssetMenu(fileName = "NewVLMConfig", menuName = "VLM Config")]
public class VLMConfig : ScriptableObject
{
    [Header("Model Settings")]
    public string modelName = "qwen2.5vl:7b";

    [Header("Prompt Settings")]
    [TextArea(3, 10)]
    public string prompt = "Analyze this picture. List all noteworthy objects you see.";

    [Header("Schema Settings")]
    public SchemaType schemaType = SchemaType.ObjectDetection;

    public enum SchemaType
    {
        ObjectDetection,
        FreeForm
    }
}