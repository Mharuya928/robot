using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewSchemaModule", menuName = "VLM/Schema Module")]
public class VLMSchemaModule : ScriptableObject
{
    [Header("Module Info")]
    public string moduleName = "Navigation";

    [Header("Properties")]
    public List<SchemaPropertyDefinition> properties = new List<SchemaPropertyDefinition>();

    [System.Serializable]
    public class SchemaPropertyDefinition
    {
        public string name;
        public PropertyType type;

        [Tooltip("Enumの場合の選択肢 (カンマ区切り)")]
        public string enumOptions;

        // ▼▼▼ 追加: 構造を定義するための参照スロット ▼▼▼
        [Tooltip("TypeがObjectまたはArrayの場合、中身の構造を定義した別のSchemaModuleをここにセットしてください")]
        public VLMSchemaModule schemaReference; 
        // ▲▲▲ 追加ここまで ▲▲▲

        // ▼▼▼ 追加: 任意項目にするためのフラグ ▼▼▼
        [Tooltip("チェックを入れると、この項目は「必須(required)」から除外されます")]
        public bool isOptional = false; 
        // ▲▲▲ 追加ここまで ▲▲▲

        public enum PropertyType
        {
            String,
            Enum,
            Boolean,
            Array,  // 既存
            Object  // ▼▼▼ 追加: オブジェクト型 ▼▼▼
        }
    }
}