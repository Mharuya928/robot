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
        public string description;
        public PropertyType type;

        [Tooltip("Enumの場合の選択肢")]
        public string enumOptions;

        public enum PropertyType
        {
            String,
            Enum,
            Boolean,
            Array // ▼▼▼ 追加: 配列タイプ ▼▼▼
        }
    }
}