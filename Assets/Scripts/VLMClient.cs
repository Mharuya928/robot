using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions; // 正規表現を使用
using System.IO;

public class VLMClient : MonoBehaviour
{
    [Header("Config")]
    [Tooltip("作成した設定ファイル(VLMConfig)をセットしてください")]
    public VLMConfig config;

    [Header("Dependencies")]
    [Tooltip("撮影時に線を消すために制御するCarController")]
    public CarController carController;
    public Camera carCamera;
    [SerializeField] private TMP_Text VLMText;

    [Header("Ollama Connection")]
    public string ollamaUrl = "http://localhost:11434/api/chat";

    [Header("Input")]
    [Tooltip("VLM（写真撮影）を起動するキー")]
    public KeyCode vlmActivationKey = KeyCode.Tab;

    [Header("Image Save Settings")]
    public string saveFolderName = "Images";

    private bool isProcessing = false;

    void Start()
    {
        // 必須設定のチェック
        if (config == null) Debug.LogError("VLM Config が設定されていません！ Projectウィンドウで作成してセットしてください。");
        if (carCamera == null) Debug.LogError("Target Camera が設定されていません");

        if (VLMText != null)
        {
            string modelName = config != null ? config.modelName : "Unknown";
            VLMText.text = $"VLM: Ready ({modelName})";
        }

        Debug.Log("VLM Client Initialized.");
    }

    void Update()
    {
        // キー入力で撮影開始
        if (Input.GetKeyDown(vlmActivationKey) && !isProcessing && config != null)
        {
            StartCoroutine(SendRequestToOllama());
        }
    }

    // ========== メイン処理 ==========

private IEnumerator SendRequestToOllama()
    {
        if (isProcessing) yield break;
        isProcessing = true;

        if (VLMText != null) VLMText.text = "VLM: Processing...";

        // ▼▼▼ 追加: 使用するモジュール一覧をログに出力 ▼▼▼
        StringBuilder moduleLog = new StringBuilder();
        moduleLog.AppendLine("【Active Modules (使用中のモジュール)】");

        if (config.activeModules != null && config.activeModules.Count > 0)
        {
            foreach (var module in config.activeModules)
            {
                if (module != null)
                {
                    moduleLog.AppendLine($"- {module.moduleName}");
                }
            }
        }
        else
        {
            moduleLog.AppendLine("- None (Free Form Mode / 自由会話モード)");
        }
        Debug.Log(moduleLog.ToString());
        // ▲▲▲ 追加ここまで ▲▲▲

        // --- 1. 画像撮影 (変更なし) ---
        string base64Image = null;
        if (carController != null) carController.SetRaycastLineVisibility(false);
        yield return null; 
        Texture2D photo = CaptureCameraView(carCamera);
        if (carController != null) carController.SetRaycastLineVisibility(true);
        byte[] bytes = photo.EncodeToJPG();
        base64Image = System.Convert.ToBase64String(bytes);
        Destroy(photo);
        // ---------------------------

        // エスケープ処理
        string safePrompt = config.prompt.Replace("\"", "\\\"").Replace("\n", "\\n");

        // ▼▼▼ 修正: モジュールがあるかないかで JSON の作り方を変える ▼▼▼
        
        string jsonBody = "";
        bool isFreeForm = (config.activeModules == null || config.activeModules.Count == 0);

        if (isFreeForm)
        {
            // パターンA: モジュールなし (Free Form) -> "format" を含めない
            jsonBody = $@"
            {{
                ""model"": ""{config.modelName}"",
                ""stream"": false,
                ""messages"": [
                    {{
                        ""role"": ""user"",
                        ""content"": ""{safePrompt}"",
                        ""images"": [""{base64Image}""]
                    }}
                ]
            }}";
        }
        else
        {
            // パターンB: モジュールあり (Schema Mode) -> "format" を含める
            string schemaJson = BuildDynamicSchemaJson(config.activeModules);
            
            jsonBody = $@"
            {{
                ""model"": ""{config.modelName}"",
                ""stream"": false,
                ""messages"": [
                    {{
                        ""role"": ""user"",
                        ""content"": ""{safePrompt}"",
                        ""images"": [""{base64Image}""]
                    }}
                ],
                ""format"": {schemaJson}
            }}";
        }

        // ▼▼▼ 修正: ここで送信するJSON全文をログに出力する ▼▼▼
        // base64Imageは非常に長くてログが見づらくなるため、"<IMAGE_DATA>"などに置き換えて表示すると便利です
        // string logBody = jsonBody.Replace(base64Image, "<BASE64_IMAGE_DATA>");
        // Debug.Log($"【Sending JSON Request】\n{logBody}");

        // --- 3. 通信処理 ---
        using (UnityWebRequest request = new UnityWebRequest(ollamaUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // エラー処理 (省略)
                Debug.LogError("Error: " + request.error);
                if (VLMText != null) VLMText.text = "Error: " + request.error;
            }
            else
            {
                string rawJson = request.downloadHandler.text;
                string contentJson = ExtractContent(rawJson);
                Debug.Log("AI Response: " + contentJson);

                // ▼▼▼ 修正: 表示処理の分岐 ▼▼▼
                if (isFreeForm)
                {
                    // Free Form ならそのままテキストを表示
                    if (VLMText != null) VLMText.text = contentJson;
                }
                else
                {
                    // Schema Mode ならパースして表示
                    DisplayDynamicResult(contentJson);
                }
            }
        }

        isProcessing = false;
    }

    // ========== 🛠️ 動的ロジック ==========

/// <summary>
    /// Configに登録されたモジュールから、JSON Schema文字列を動的に生成する
    /// </summary>
    private string BuildDynamicSchemaJson(List<VLMSchemaModule> modules)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(@"{ ""type"": ""object"", ""properties"": {");

        List<string> requiredKeys = new List<string>();
        List<string> props = new List<string>();

        foreach (var module in modules)
        {
            if (module == null) continue;

            foreach (var prop in module.properties)
            {
                requiredKeys.Add($"\"{prop.name}\"");

                string typeDef = "";
                
                // ▼▼▼ 修正: Arrayタイプの処理を追加 ▼▼▼
                if (prop.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Array)
                {
                    // 文字列の配列として定義する
                    typeDef = $@"{{ ""type"": ""array"", ""items"": {{ ""type"": ""string"" }}, ""description"": ""{prop.description}"" }}";
                }
                else if (prop.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Enum)
                {
                    string[] opts = prop.enumOptions.Split(',');
                    for(int i=0; i<opts.Length; i++) opts[i] = opts[i].Trim(); 
                    string enumStr = string.Join("\",\"", opts); 
                    typeDef = $@"{{ ""type"": ""string"", ""enum"": [""{enumStr}""], ""description"": ""{prop.description}"" }}";
                }
                else if (prop.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Boolean)
                {
                    typeDef = $@"{{ ""type"": ""boolean"", ""description"": ""{prop.description}"" }}";
                }
                else
                {
                    typeDef = $@"{{ ""type"": ""string"", ""description"": ""{prop.description}"" }}";
                }
                
                props.Add($"\"{prop.name}\": {typeDef}");
            }
        }

        sb.Append(string.Join(",", props));
        sb.Append(@"}, ""required"": [");
        sb.Append(string.Join(",", requiredKeys));
        sb.Append("] }");

        return sb.ToString();
    }

    /// <summary>
    /// AIからのJSON応答を正規表現で解析し、UIに綺麗に表示する
    /// </summary>
    private void DisplayDynamicResult(string jsonResponse)
    {
        StringBuilder sb = new StringBuilder();

        foreach (var module in config.activeModules)
        {
            if (module == null) continue;

            sb.AppendLine($"<b>[{module.moduleName}]</b>");
            
            foreach (var prop in module.properties)
            {
                // 配列 [...] も 文字列 "..." も両方拾える正規表現
                string pattern = $"\"{prop.name}\"\\s*:\\s*(\\[.*?\\]|\".*?\")";
                Match match = Regex.Match(jsonResponse, pattern, RegexOptions.Singleline);

                if (match.Success)
                {
                    string val = match.Groups[1].Value.Trim();

                    // ▼▼▼ 修正: 値の整形処理 (記号を消す) ▼▼▼
                    
                    if (val.StartsWith("[")) 
                    {
                        // 配列の場合: [ ] " をすべて削除して、カンマ区切りだけにする
                        // 例: ["cube", "sphere"]  ->  cube, sphere
                        val = val.Replace("[", "").Replace("]", "").Replace("\"", "");
                    }
                    else 
                    {
                        // 文字列の場合: 両端の " を削除
                        val = val.Trim('"');
                    }

                    // 値が空っぽなら "None" と表示するなどの調整
                    if (string.IsNullOrWhiteSpace(val)) val = "None";


                    // --- 色付けロジック (変更なし) ---
                    string displayVal = val;
                    string lowerVal = val.ToLower(); // 小文字で判定
                    if (lowerVal.Contains("high") || lowerVal.Contains("danger") || lowerVal == "true" || lowerVal.Contains("critical")) 
                    {
                        // 危険系 -> 赤
                        displayVal = $"<color=red>{val}</color>";
                    }
                    else if (lowerVal.Contains("safe") || lowerVal == "false" || lowerVal.Contains("clear") || lowerVal == "none")
                    {
                        // 安全系 -> 緑
                        displayVal = $"<color=green>{val}</color>";
                    }
                    else if (lowerVal.Contains("caution") || lowerVal.Contains("warning") || lowerVal.Contains("medium"))
                    {
                        // 注意系 -> 黄色
                        displayVal = $"<color=yellow>{val}</color>";
                    }
                    else
                    {
                        // その他（物体名など） -> 色を変えない（デフォルトの白）
                        displayVal = val;
                    }

                    sb.AppendLine($"- {prop.name}: {displayVal}");
                }
                else
                {
                    sb.AppendLine($"- {prop.name}: <color=grey>(Not found)</color>");
                }
            }
            sb.AppendLine(); 
        }

        if (VLMText != null) VLMText.text = sb.ToString();
    }

    // OllamaのレスポンスJSONから .message.content の中身だけ抜くヘルパー
    private string ExtractContent(string fullJson)
    {
        try
        {
            return JsonUtility.FromJson<OllamaResponse>(fullJson).message.content;
        }
        catch
        {
            return fullJson; // パース失敗時はそのまま返す
        }
    }

    // ========== ヘルパー関数 (画像処理など) ==========

    private void SaveImageToFile(byte[] bytes)
    {
        #if UNITY_EDITOR
        string folderPath = Path.Combine(Application.dataPath, saveFolderName);
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
        string fileName = $"capture_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.jpg";
        string filePath = Path.Combine(folderPath, fileName);
        File.WriteAllBytes(filePath, bytes);
        #endif
    }

    private Texture2D CaptureCameraView(Camera camera)
    {
        RenderTexture renderTexture = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 24);
        camera.targetTexture = renderTexture;
        camera.Render();
        RenderTexture.active = renderTexture;
        Texture2D screenshot = new Texture2D(camera.pixelWidth, camera.pixelHeight, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, camera.pixelWidth, camera.pixelHeight), 0, 0);
        screenshot.Apply();
        camera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(renderTexture);
        return screenshot;
    }

    // Unity JsonUtility用のラッパークラス (外側のレスポンス用)
    [System.Serializable]
    public class OllamaResponse
    {
        public ResponseMessage message;
    }
    [System.Serializable]
    public class ResponseMessage
    {
        public string content;
    }
}