using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewVLMConfig", menuName = "VLM/Config")]
public class VLMConfig : ScriptableObject
{
    // ▼▼▼ 1. Customを削除したリスト ▼▼▼
    public enum ModelType
    {
        Qwen2_5_VL_7B,      // qwen2.5vl:7b
        Qwen2_5_VL_3B,      // qwen2.5vl:3b
        Qwen3_VL_8B,        // qwen3-vl:8b
        Qwen3_VL_4B         // qwen3-vl:4b
    }

    [Header("Model Selection")]
    [Tooltip("使用するモデルを選択してください")]
    public ModelType selectedModel = ModelType.Qwen2_5_VL_7B;

    // customModelName 変数は削除しました

    // ▼▼▼ 2. 選択されたモデル名を返すプロパティ ▼▼▼
    public string ModelName
    {
        get
        {
            switch (selectedModel)
            {
                case ModelType.Qwen3_VL_4B:   return "qwen3-vl:4b";
                case ModelType.Qwen3_VL_8B:   return "qwen3-vl:8b";
                case ModelType.Qwen2_5_VL_7B: return "qwen2.5vl:7b";
                case ModelType.Qwen2_5_VL_3B: return "qwen2.5vl:3b";
                default:                      return "qwen3-vl:8b";
            }
        }
    }

    // --- 以下、既存の設定項目 ---

    [TextArea(3, 10)] 
    public string prompt = "Analyze this scene.";

    [Header("Generation Settings")]
    [Tooltip("コンテキスト長（記憶容量）。画像を送るなら 4096 以上推奨。")]
    public int contextSize = 4096;

    [Tooltip("最大トークン数（出力の上限）。")]
    public int maxTokens = 300;

    [Tooltip("創造性 (0.0 = 論理的・固い, 1.0 = 創造的・ランダム)")]
    [Range(0.0f, 1.0f)] 
    public float temperature = 0.0f; 

    public List<VLMSchemaModule> activeModules = new List<VLMSchemaModule>();
}