using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewSchemaModule", menuName = "VLM/Schema Module")]
public class VLMSchemaModule : ScriptableObject
{
    [Header("Module Info")]
    public string moduleName; // 表示用タイトル

    [Header("Properties")]
    public List<SchemaPropertyDefinition> properties = new List<SchemaPropertyDefinition>();

    [System.Serializable]
    public class SchemaPropertyDefinition
    {
        public string name;         // JSONのキー (例: "risk_level")
        public string description;  // AIへの説明 (例: "The risk level of the scene")
        public PropertyType type;   // 型

        [Tooltip("Enumの場合の選択肢 (カンマ区切りで入力)")]
        public string enumOptions;  // 例: "high,medium,low"

        public enum PropertyType
        {
            String,
            Enum,
            Boolean
        }
    }
}