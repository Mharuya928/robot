using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;

public class VLMClient : MonoBehaviour
{
    // =================================================================
    // 1. 設定・依存関係
    // =================================================================
    [Header("Config")]
    public VLMConfig config;

    [Header("Dependencies")]
    public CarController carController;

    // =================================================================
    // 2. カメラ設定
    // =================================================================
    [Header("Camera Setup")]
    public Camera frontCamera;
    public Camera backCamera; 
    public Camera topCamera;
    public Camera leftCamera;
    public Camera rightCamera;

    [SerializeField] private TMP_Text VLMText;

    [Header("UI Settings")]
    [SerializeField] private TMP_Text configText; 

    // =================================================================
    // 3. 通信・撮影設定
    // =================================================================
    [Header("Ollama Connection")]
    public string ollamaUrl = "http://localhost:11434/api/chat";

    [Header("Capture Settings")]
    [Tooltip("VLMに送る画像の幅 (推奨: 512)")]
    public int captureWidth = 512;
    
    [Tooltip("VLMに送る画像の高さ (推奨: 512)")]
    public int captureHeight = 512;
    
    [Header("Input")]
    public KeyCode vlmActivationKey = KeyCode.Tab;

    // =================================================================
    // 4. コ・パイロット設定
    // =================================================================
    [Header("Co-pilot / Auto Warning Settings")]
    public bool enableAutoWarning = true; 
    public float autoWarningDistance = 2.0f; 
    public float warningCooldown = 5.0f; 

    private float lastWarningTime = -100f; 
    private bool isProcessing = false;     
    
    [Header("Image Save Settings")]
    public string saveFolderName = "Images";

    // =================================================================
    // 初期化
    // =================================================================
    void Start()
    {
        if (config == null) Debug.LogError("VLM Config Error");
        if (configText != null) configText.text = $"Config: {config?.name}";
        if (VLMText != null) VLMText.text = $"VLM: Ready ({config?.ModelName})";
        Debug.Log("VLM Client Initialized.");
    }

    // =================================================================
    // メインループ
    // =================================================================
    void Update()
    {
        // A. 手動トリガー
        if (Input.GetKeyDown(vlmActivationKey) && !isProcessing && config != null)
        {
            StartCoroutine(SendRequestToOllama());
        }

        // B. 自動安全トリガー
        if (enableAutoWarning && !isProcessing && config != null)
        {
            RaycastHit hit;
            Vector3 rayOrigin = transform.position + new Vector3(0, 0.5f, 0);
            if (Physics.Raycast(rayOrigin, transform.forward, out hit, autoWarningDistance))
            {
                if (Time.time - lastWarningTime > warningCooldown)
                {
                    Debug.LogWarning($"【Auto】Obstacle detected! {hit.distance:F1}m");
                    lastWarningTime = Time.time;
                    StartCoroutine(SendRequestToOllama());
                }
            }
        }
    }

    // =================================================================
    // AI通信のメイン処理
    // =================================================================
    private IEnumerator SendRequestToOllama()
    {
        if (isProcessing) yield break;
        isProcessing = true;
        if (VLMText != null) VLMText.text = "VLM: Processing...";

        // モジュールログ
        StringBuilder moduleLog = new StringBuilder();
        if (config.activeModules != null) foreach(var m in config.activeModules) if(m) moduleLog.AppendLine(m.moduleName);
        Debug.Log($"Active Modules:\n{moduleLog}");

        if (carController != null) carController.SetRaycastLineVisibility(false);
        yield return null; 

        // 複数画像をリストで取得
        List<Texture2D> capturedTextures = new List<Texture2D>();

        switch (config.viewMode)
        {
            case VLMConfig.ViewMode.FPS:
                if (frontCamera) capturedTextures.Add(CaptureCameraView(frontCamera));
                break;

            case VLMConfig.ViewMode.MultiView:
                if (frontCamera) capturedTextures.Add(CaptureCameraView(frontCamera));
                if (topCamera) capturedTextures.Add(CaptureCameraView(topCamera));
                break;

            case VLMConfig.ViewMode.SurroundView:
                // 順番: 1.前方, 2.後方, 3.左, 4.右
                if (frontCamera) capturedTextures.Add(CaptureCameraView(frontCamera));
                if (backCamera) capturedTextures.Add(CaptureCameraView(backCamera));
                if (leftCamera) capturedTextures.Add(CaptureCameraView(leftCamera));
                if (rightCamera) capturedTextures.Add(CaptureCameraView(rightCamera));
                break;
        }

        if (capturedTextures.Count == 0)
        {
             Debug.LogError("撮影失敗: カメラが設定されていないか、画像が取得できませんでした。");
             isProcessing = false;
             yield break;
        }

        if (carController != null) carController.SetRaycastLineVisibility(true);

        // 全画像をBase64リストに変換
        List<string> base64Images = new List<string>();
        for (int i = 0; i < capturedTextures.Count; i++)
        {
            byte[] bytes = capturedTextures[i].EncodeToJPG();
            SaveImageToFile(bytes, i); 
            base64Images.Add(System.Convert.ToBase64String(bytes));
            Destroy(capturedTextures[i]);
        }

        // --- JSON構築 ---
        string safePrompt = config.CurrentPrompt.Replace("\"", "\\\"").Replace("\n", "\\n");
        OllamaOptions options = new OllamaOptions { num_predict = config.maxTokens, temperature = config.temperature, num_ctx = config.contextSize };
        string optionsJson = JsonUtility.ToJson(options);

        string imagesJsonArray = "";
        if (base64Images.Count > 0)
        {
            imagesJsonArray = "\"" + string.Join("\",\"", base64Images) + "\"";
        }

        string jsonBody = "";
        bool isFreeForm = (config.activeModules == null || config.activeModules.Count == 0);
        
        string messagesPart = $@"
        [
            {{
                ""role"": ""user"",
                ""content"": ""{safePrompt}"",
                ""images"": [{imagesJsonArray}] 
            }}
        ]";

        if (isFreeForm)
        {
            jsonBody = $@"{{ ""model"": ""{config.ModelName}"", ""stream"": false, ""options"": {optionsJson}, ""messages"": {messagesPart} }}";
        }
        else
        {
            string schemaJson = BuildDynamicSchemaJson(config.activeModules);
            jsonBody = $@"{{ ""model"": ""{config.ModelName}"", ""stream"": false, ""options"": {optionsJson}, ""messages"": {messagesPart}, ""format"": {schemaJson} }}";
        }

        // ログ出力 (画像データ省略)
        if (!string.IsNullOrEmpty(jsonBody))
        {
            string debugJson = jsonBody;
            if (base64Images != null)
            {
                foreach (var b64 in base64Images)
                {
                    if (!string.IsNullOrEmpty(b64)) debugJson = debugJson.Replace(b64, "<IMAGE_DATA_OMITTED>");
                }
            }
            Debug.Log($"【Request Debug】Config: {config.name}\nSending JSON: {debugJson}");
        }

        // --- 通信 ---
        using (UnityWebRequest request = new UnityWebRequest(ollamaUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + request.error);
                if (VLMText) VLMText.text = "Error: " + request.error;
            }
            else
            {
                string rawJson = request.downloadHandler.text;
                
                // トークン使用量チェック
                OllamaResponse responseData = JsonUtility.FromJson<OllamaResponse>(rawJson);
                int used = responseData.prompt_eval_count;
                int limit = config.contextSize;
                string tokenLog = $"【Token Usage】 Used: {used} / Limit: {limit}";
                if (used >= limit) Debug.LogError($"{tokenLog} ⚠️不足!");
                else Debug.Log($"{tokenLog} ✅OK");

                string contentJson = ExtractContent(rawJson);
                Debug.Log("AI Response: " + contentJson);
                if (isFreeForm && VLMText) VLMText.text = contentJson;
                else DisplayDynamicResult(contentJson);
            }
        }
        isProcessing = false;
    }

    // =================================================================
    // ヘルパー: JSON Schemaの動的生成 (Description削除)
    // =================================================================
// =================================================================
    // ヘルパー: JSON Schemaの動的生成 (再帰対応版)
    // =================================================================
    private string BuildDynamicSchemaJson(List<VLMSchemaModule> modules)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(@"{ ""type"": ""object"", ""properties"": {");

        List<string> props = new List<string>();
        List<string> req = new List<string>();

        foreach (var m in modules)
        {
            if (m == null) continue;
            // モジュールごとのプロパティ定義を取得して結合
            props.Add(GeneratePropertiesJson(m));
            
            // ルートレベルの必須項目リストを作成
            foreach (var p in m.properties) req.Add($"\"{p.name}\"");
        }

        sb.Append(string.Join(",", props));
        sb.Append(@"}, ""required"": [");
        sb.Append(string.Join(",", req));
        sb.Append(@"], ""additionalProperties"": false }"); // 厳密なスキーマにする

        return sb.ToString();
    }

    // プロパティリストのJSON生成 (再帰用)
    private string GeneratePropertiesJson(VLMSchemaModule module)
    {
        if (module == null) return "";
        List<string> propList = new List<string>();

        foreach (var p in module.properties)
        {
            string typeDef = "";

            switch (p.type)
            {
                case VLMSchemaModule.SchemaPropertyDefinition.PropertyType.String:
                    typeDef = @"{ ""type"": ""string"" }";
                    break;

                case VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Boolean:
                    typeDef = @"{ ""type"": ""boolean"" }";
                    break;

                case VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Enum:
                    string[] opts = p.enumOptions.Split(',');
                    for (int i = 0; i < opts.Length; i++) opts[i] = opts[i].Trim();
                    string enumStr = string.Join("\",\"", opts);
                    typeDef = $@"{{ ""type"": ""string"", ""enum"": [""{enumStr}""] }}";
                    break;

                case VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Array:
                    // 中身の定義がある場合は「オブジェクトの配列」にする
                    if (p.schemaReference != null)
                    {
                        string childProps = GeneratePropertiesJson(p.schemaReference);
                        string childReq = GetRequiredListJson(p.schemaReference);
                        typeDef = $@"{{ ""type"": ""array"", ""items"": {{ ""type"": ""object"", ""properties"": {{ {childProps} }}, ""required"": {childReq}, ""additionalProperties"": false }} }}";
                    }
                    else
                    {
                        // 参照がない場合は従来の「文字列配列」
                        typeDef = @"{ ""type"": ""array"", ""items"": { ""type"": ""string"" } }";
                    }
                    break;

                case VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Object:
                    // 中身の定義を使って「オブジェクト」を作る
                    if (p.schemaReference != null)
                    {
                        string childProps = GeneratePropertiesJson(p.schemaReference);
                        string childReq = GetRequiredListJson(p.schemaReference);
                        typeDef = $@"{{ ""type"": ""object"", ""properties"": {{ {childProps} }}, ""required"": {childReq}, ""additionalProperties"": false }}";
                    }
                    else
                    {
                        typeDef = @"{ ""type"": ""object"" }";
                    }
                    break;
            }
            propList.Add($"\"{p.name}\": {typeDef}");
        }
        return string.Join(",", propList);
    }

    // 必須項目リストの生成ヘルパー
    private string GetRequiredListJson(VLMSchemaModule module)
    {
        List<string> req = new List<string>();
        foreach (var p in module.properties) req.Add($"\"{p.name}\"");
        return "[" + string.Join(",", req) + "]";
    }

    // =================================================================
    // ヘルパー: その他
    // =================================================================

    private void SaveImageToFile(byte[] bytes, int index)
    {
        #if UNITY_EDITOR
        string folderPath = Path.Combine(Application.dataPath, saveFolderName);
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
        string fileName = $"capture_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{index}.jpg";
        File.WriteAllBytes(Path.Combine(folderPath, fileName), bytes);
        #endif
    }

    private Texture2D CaptureCameraView(Camera camera)
    {
        int width = captureWidth;
        int height = captureHeight;
        RenderTexture renderTexture = new RenderTexture(width, height, 24);
        camera.targetTexture = renderTexture;
        camera.Render();
        RenderTexture.active = renderTexture;
        Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenshot.Apply();
        camera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(renderTexture);
        return screenshot;
    }

    private void DisplayDynamicResult(string jsonResponse)
    {
        StringBuilder sb = new StringBuilder();
        foreach (var module in config.activeModules)
        {
            if (module == null) continue;
            sb.AppendLine($"<b>[{module.moduleName}]</b>");
            foreach (var prop in module.properties)
            {
                string pattern = $"\"{prop.name}\"\\s*:\\s*(\\[.*?\\]|\".*?\")";
                Match match = Regex.Match(jsonResponse, pattern, RegexOptions.Singleline);
                if (match.Success)
                {
                    string val = match.Groups[1].Value.Trim();
                    if (val.StartsWith("[")) val = val.Replace("[", "").Replace("]", "").Replace("\"", "");
                    else val = val.Trim('"');
                    val = val.Replace("**", ""); 
                    if (string.IsNullOrWhiteSpace(val)) val = "None";

                    string d = val; string l = val.ToLower();
                    if (l.Contains("high") || l.Contains("danger") || l == "true" || l.Contains("critical")) d = $"<color=red>{val}</color>";
                    else if (l.Contains("safe") || l == "false" || l.Contains("clear") || l == "none") d = $"<color=green>{val}</color>";
                    else if (l.Contains("caution") || l.Contains("warning")) d = $"<color=yellow>{val}</color>";
                    sb.AppendLine($"- {prop.name}: {d}");
                }
                else sb.AppendLine($"- {prop.name}: <color=grey>(Not found)</color>");
            }
            sb.AppendLine();
        }
        if (VLMText != null) VLMText.text = sb.ToString();
    }

    private string ExtractContent(string fullJson)
    {
        try {
            string content = JsonUtility.FromJson<OllamaResponse>(fullJson).message.content;
            if (!string.IsNullOrEmpty(content)) content = content.Replace("**", "");
            return content;
        } catch { return fullJson; }
    }

    [System.Serializable] public class OllamaResponse { 
        public ResponseMessage message; 
        public int prompt_eval_count;
        public int eval_count;
    }
    [System.Serializable] public class ResponseMessage { public string content; }
    [System.Serializable] public class OllamaOptions { public int num_predict; public float temperature; public int num_ctx; }
}