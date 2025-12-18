using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq; // List操作用

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
    // AI通信のメイン処理 (修正版)
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

        // 線を消す
        if (carController != null) carController.SetRaycastLineVisibility(false);
        yield return null; 

        // ▼▼▼ 修正: 複数画像をリストで取得 ▼▼▼
        List<Texture2D> capturedTextures = new List<Texture2D>();

        switch (config.viewMode)
        {
            case VLMConfig.ViewMode.FPS:
                if (frontCamera) capturedTextures.Add(CaptureCameraView(frontCamera));
                break;

            case VLMConfig.ViewMode.MultiView:
                // 順番: 1.前方, 2.上方
                if (frontCamera) capturedTextures.Add(CaptureCameraView(frontCamera));
                if (topCamera) capturedTextures.Add(CaptureCameraView(topCamera));
                break;

            case VLMConfig.ViewMode.SurroundView:
                // 順番: 1.前方, 2.後方, 3.左, 4.右
                // ※プロンプトでこの順番をAIに伝える必要があります
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

        // 線を戻す
        if (carController != null) carController.SetRaycastLineVisibility(true);

        // ▼▼▼ 修正: 全画像をBase64リストに変換 ▼▼▼
        List<string> base64Images = new List<string>();
        
        for (int i = 0; i < capturedTextures.Count; i++)
        {
            byte[] bytes = capturedTextures[i].EncodeToJPG();
            // 保存（デバッグ用: ファイル名にインデックスをつける）
            SaveImageToFile(bytes, i); 
            base64Images.Add(System.Convert.ToBase64String(bytes));
            Destroy(capturedTextures[i]); // メモリ解放
        }

        // --- JSON構築 ---
        string safePrompt = config.CurrentPrompt.Replace("\"", "\\\"").Replace("\n", "\\n");
        OllamaOptions options = new OllamaOptions { num_predict = config.maxTokens, temperature = config.temperature, num_ctx = config.contextSize };
        string optionsJson = JsonUtility.ToJson(options);

        // 画像配列のJSON文字列を作成 ("img1", "img2", "img3")
        string imagesJsonArray = "";
        if (base64Images.Count > 0)
        {
            // 各Base64文字列をダブルクォートで囲み、カンマで結合
            imagesJsonArray = "\"" + string.Join("\",\"", base64Images) + "\"";
        }

        string jsonBody = "";
        bool isFreeForm = (config.activeModules == null || config.activeModules.Count == 0);
        
        // メッセージ部分の構築 (images配列を埋め込む)
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

        // ログ出力
        Debug.Log($"【Request Debug】Sending {base64Images.Count} images. Config: {config.name}");

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
                string contentJson = ExtractContent(rawJson);
                Debug.Log("AI Response: " + contentJson);
                if (isFreeForm && VLMText) VLMText.text = contentJson;
                else DisplayDynamicResult(contentJson);
            }
        }
        isProcessing = false;
    }

    // =================================================================
    // ヘルパー関数
    // =================================================================

    // 画像保存 (インデックス付きに対応)
    private void SaveImageToFile(byte[] bytes, int index)
    {
        #if UNITY_EDITOR
        string folderPath = Path.Combine(Application.dataPath, saveFolderName);
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
        // ファイル名に連番をつける (capture_DATE_0.jpg, capture_DATE_1.jpg...)
        string fileName = $"capture_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{index}.jpg";
        File.WriteAllBytes(Path.Combine(folderPath, fileName), bytes);
        #endif
    }

    // 単一カメラ撮影 (これだけ残せばOK)
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

    // NOTE: 以前の CaptureCombinedView, CaptureSurroundView は不要になったため削除しました。

    // --- JSON Schema構築などは変更なし ---
    private string BuildDynamicSchemaJson(List<VLMSchemaModule> modules)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(@"{ ""type"": ""object"", ""properties"": {");
        List<string> req = new List<string>();
        List<string> props = new List<string>();
        foreach (var m in modules)
        {
            if (m == null) continue;
            foreach (var p in m.properties)
            {
                req.Add($"\"{p.name}\"");
                string t = "";
                if (p.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Array) t = $@"{{ ""type"": ""array"", ""items"": {{ ""type"": ""string"" }}, ""description"": ""{p.description}"" }}";
                else if (p.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Enum) {
                    string[] opts = p.enumOptions.Split(','); for (int i = 0; i < opts.Length; i++) opts[i] = opts[i].Trim();
                    string enumStr = string.Join("\",\"", opts);
                    t = $@"{{ ""type"": ""string"", ""enum"": [""{enumStr}""], ""description"": ""{p.description}"" }}";
                }
                else if (p.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Boolean) t = $@"{{ ""type"": ""boolean"", ""description"": ""{p.description}"" }}";
                else t = $@"{{ ""type"": ""string"", ""description"": ""{p.description}"" }}";
                props.Add($"\"{p.name}\": {t}");
            }
        }
        sb.Append(string.Join(",", props));
        sb.Append(@"}, ""required"": [");
        sb.Append(string.Join(",", req));
        sb.Append("] }");
        return sb.ToString();
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

    [System.Serializable] public class OllamaResponse { public ResponseMessage message; }
    [System.Serializable] public class ResponseMessage { public string content; }
    [System.Serializable] public class OllamaOptions { public int num_predict; public float temperature; public int num_ctx; }
}