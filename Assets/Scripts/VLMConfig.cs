using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewVLMConfig", menuName = "VLM/Config")]
public class VLMConfig : ScriptableObject
{
    // ▼▼▼ 1. カメラモードの定義 ▼▼▼
    public enum ViewMode
    {
        FPS,        // 一人称 (Front Camera)
        TPS,        // 三人称 (Back/Top Camera)
        MultiView   // 結合 (Front + Top)
    }

    public enum ModelType
    {
        Qwen2_5_VL_7B,
        Qwen2_5_VL_3B,
        Qwen3_VL_8B_Instruct,
        Qwen3_VL_30B_A3B_Instruct,
        Qwen3_VL_8B,
        Qwen3_VL_4B
    }

    [Header("Model Selection")]
    public ModelType selectedModel = ModelType.Qwen2_5_VL_7B;

    // ▼▼▼ 2. ビューモード選択 ▼▼▼
    [Header("Camera & View Settings")]
    [Tooltip("カメラの視点モードを選択してください")]
    public ViewMode viewMode = ViewMode.FPS;

    // ▼▼▼ 3. モードごとのプロンプト設定 ▼▼▼
    [Header("Prompts per Mode")]
    [TextArea(3, 10)] [Tooltip("FPSモード用プロンプト")]
    public string promptFPS = "Describe this image.\n...";
    
    [TextArea(3, 10)] [Tooltip("TPSモード用プロンプト")]
    public string promptTPS = "Describe this image.\n...";

    [TextArea(3, 10)] [Tooltip("Multi-Viewモード用プロンプト")]
    public string promptMulti = "Describe this image.\n...";

    // 以前の単一プロンプトは、現在選択中のモードに応じて返すように変更
    public string CurrentPrompt
    {
        get
        {
            switch (viewMode)
            {
                case ViewMode.FPS: return promptFPS;
                case ViewMode.TPS: return promptTPS;
                case ViewMode.MultiView: return promptMulti;
                default: return promptFPS;
            }
        }
    }

    public string ModelName
    {
        get
        {
            switch (selectedModel)
            {
                case ModelType.Qwen3_VL_4B:   return "qwen3-vl:4b";
                case ModelType.Qwen3_VL_8B:   return "qwen3-vl:8b";
                case ModelType.Qwen3_VL_8B_Instruct: return "qwen3-vl:8b-instruct";
                case ModelType.Qwen3_VL_30B_A3B_Instruct: return "qwen3-vl:30b-a3b-instruct";
                case ModelType.Qwen2_5_VL_7B: return "qwen2.5vl:7b";
                case ModelType.Qwen2_5_VL_3B: return "qwen2.5vl:3b";
                default:                      return "qwen3-vl:8b";
            }
        }
    }

    [Header("Generation Settings")]
    public int contextSize = 4096;
    public int maxTokens = 300;
    [Range(0.0f, 1.0f)] 
    public float temperature = 0.0f; 

    public List<VLMSchemaModule> activeModules = new List<VLMSchemaModule>();
}