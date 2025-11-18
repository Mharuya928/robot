using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewVLMConfig", menuName = "VLM/Config")]
public class VLMConfig : ScriptableObject
{
    public string modelName = "qwen2.5vl:7b";
    [TextArea(3, 10)] public string prompt = "Analyze this scene.";

    // ▼▼▼ 修正: 単にモジュールをドラッグ＆ドロップするリストにする ▼▼▼
    public List<VLMSchemaModule> activeModules = new List<VLMSchemaModule>();
}