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

        Debug.Log($"Sending Request (FreeForm: {isFreeForm})");

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
                // 必須キーとして追加
                requiredKeys.Add($"\"{prop.name}\"");

                string typeDef = "";
                if (prop.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Enum)
                {
                    // Enumの場合は選択肢を展開 (カンマ区切り文字列を配列に変換)
                    string[] opts = prop.enumOptions.Split(',');
                    // 各要素をダブルクォートで囲む
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
                    // String
                    typeDef = $@"{{ ""type"": ""string"", ""description"": ""{prop.description}"" }}";
                }
                
                props.Add($"\"{prop.name}\": {typeDef}");
            }
        }

        // プロパティを結合
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

        // アクティブなモジュール順に表示を作る
        foreach (var module in config.activeModules)
        {
            if (module == null) continue;

            sb.AppendLine($"<b>[{module.moduleName}]</b>");
            
            foreach (var prop in module.properties)
            {
                // 正規表現で値を探す: "key" : "value" または "key": value
                // (簡易的なパーサですが、Ollamaの構造化出力なら概ね動作します)
                string pattern = $"\"{prop.name}\"\\s*:\\s*\"?(.*?)\"?\\s*(,|}})";
                Match match = Regex.Match(jsonResponse, pattern);

                if (match.Success)
                {
                    // 値を取得
                    string val = match.Groups[1].Value.Trim();
                    // 末尾の引用符などが残っていたら削除
                    val = val.Trim('"');

                    // 色付け (Enumで危険度などを強調したい場合の例)
                    string displayVal = val;
                    if (val.ToLower() == "high" || val.ToLower() == "danger" || val.ToLower() == "true") 
                        displayVal = $"<color=red>{val}</color>";
                    else if (val.ToLower() == "safe" || val.ToLower() == "false")
                        displayVal = $"<color=green>{val}</color>";
                    else
                        displayVal = $"<color=yellow>{val}</color>";

                    sb.AppendLine($"- {prop.name}: {displayVal}");
                }
                else
                {
                    sb.AppendLine($"- {prop.name}: <color=grey>(Not found)</color>");
                }
            }
            sb.AppendLine(); // モジュール間の空行
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