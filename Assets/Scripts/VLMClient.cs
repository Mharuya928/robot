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

    // ▼▼▼ 修正: カメラを役割ごとに明確に指定 ▼▼▼
    [Header("Camera Setup")]
    [Tooltip("一人称視点 (FPS) および マルチビューの上半分で使用")]
    public Camera frontCamera;

    [Tooltip("三人称視点 (TPS) で使用")]
    public Camera tpsCamera;

    [Tooltip("マルチビューの下半分 (俯瞰) で使用")]
    public Camera topCamera;
    // ▲▲▲ 修正ここまで ▲▲▲

    // [Header("Camera Selection")]
    // [Tooltip("ここに入力した番号（Element番号）のカメラが使われます")]
    // public int selectedCameraIndex = 0;

    [SerializeField] private TMP_Text VLMText;

    [Header("Ollama Connection")]
    public string ollamaUrl = "http://localhost:11434/api/chat";

    [Header("Capture Settings")]
    [Tooltip("VLMに送る画像の幅。小さいほど高速です。(推奨: 640 or 512)")]
    public int captureWidth = 512;
    
    [Tooltip("VLMに送る画像の高さ。(推奨: 360 or 512)")]
    public int captureHeight = 512;
    
    [Header("Input")]
    [Tooltip("VLM（写真撮影）を起動するキー")]
    public KeyCode vlmActivationKey = KeyCode.Tab;

    // [Header("Multi-View Settings")]
    // [Tooltip("オンにすると、Capture Camerasの Element 0（上半分）と Element 1（下半分）を縦に結合して送ります")]
    // public bool useMultiView = false;

    [Header("Image Save Settings")]
    public string saveFolderName = "Images";

    private bool isProcessing = false;

    void Start()
    {
        // 必須設定のチェック
        if (config == null) Debug.LogError("VLM Config が設定されていません！ Projectウィンドウで作成してセットしてください。");

        if (VLMText != null)
        {
            string modelName = config != null ? config.ModelName : "Unknown";
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

        // --- 1. 画像撮影 ---
        string base64Image = null;
        if (carController != null) carController.SetRaycastLineVisibility(false);
        yield return null; 

        Texture2D photo = null;

        // ▼▼▼ 修正: ConfigのViewModeに従ってカメラを選ぶ ▼▼▼
        switch (config.viewMode)
        {
            case VLMConfig.ViewMode.FPS:
                // FPSモード: FrontCameraを使用
                if (frontCamera != null)
                {
                    photo = CaptureCameraView(frontCamera);
                }
                else Debug.LogError("FPSモードですが、Front Cameraが設定されていません。");
                break;

            case VLMConfig.ViewMode.TPS:
                // TPSモード: TPSCameraを使用
                if (tpsCamera != null)
                {
                    photo = CaptureCameraView(tpsCamera);
                }
                else Debug.LogError("TPSモードですが、TPS Cameraが設定されていません。");
                break;

            case VLMConfig.ViewMode.MultiView:
                // MultiViewモード: Front + Top を結合
                if (frontCamera != null && topCamera != null)
                {
                    photo = CaptureCombinedView(frontCamera, topCamera);
                }
                else Debug.LogError("MultiViewモードですが、Front Camera または Top Camera が設定されていません。");
                break;
        }
        // ▲▲▲ 修正ここまで ▲▲▲
        if (photo == null)
        {
             Debug.LogError("撮影に失敗しました (Photo is null)");
             isProcessing = false;
             yield break;
        }

        if (carController != null) carController.SetRaycastLineVisibility(true);
        
        byte[] bytes = photo.EncodeToJPG();

        // 画像保存
        SaveImageToFile(bytes);

        base64Image = System.Convert.ToBase64String(bytes);
        Destroy(photo);
        // ---------------------------

        // ▼▼▼ プロンプト取得 (Config側でモードに応じて切り替わる) ▼▼▼
        string currentPromptText = config.CurrentPrompt;
        string safePrompt = currentPromptText.Replace("\"", "\\\"").Replace("\n", "\\n");

        // ▼▼▼ 追加: オプションのJSON文字列を作成 ▼▼▼
        OllamaOptions options = new OllamaOptions
        {
            num_predict = config.maxTokens,   // Configの値をセット
            temperature = config.temperature,  // Configの値をセット
            num_ctx = config.contextSize      // これを送らないと画像で溢れます
        };
        string optionsJson = JsonUtility.ToJson(options);
        // ▲▲▲ 追加ここまで ▲▲▲

        // ▼▼▼ 修正: モジュールがあるかないかで JSON の作り方を変える ▼▼▼
        
        string jsonBody = "";
        bool isFreeForm = (config.activeModules == null || config.activeModules.Count == 0);

        if (isFreeForm)
        {
            // パターンA: モジュールなし (Free Form) -> "format" を含めない
            // ▼▼▼ 修正: options を追加 ▼▼▼
            jsonBody = $@"
            {{
                ""model"": ""{config.ModelName}"",
                ""stream"": false,
                ""options"": {optionsJson},
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
            
            // ▼▼▼ 修正: options を追加 ▼▼▼
            jsonBody = $@"
            {{
                ""model"": ""{config.ModelName}"",
                ""stream"": false,
                ""options"": {optionsJson},
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

        // ▼▼▼ 追加: 送信JSONのデバッグ表示 (画像データは省略して表示) ▼▼▼
        if (!string.IsNullOrEmpty(jsonBody))
        {
            // ログ用にコピーを作成
            string debugJson = jsonBody;

            // 長すぎるBase64画像データを "<IMAGE_DATA>" に置換して見やすくする
            if (!string.IsNullOrEmpty(base64Image))
            {
                debugJson = debugJson.Replace(base64Image, "<IMAGE_DATA_OMITTED>");
            }

            // ▼▼▼ 修正: カメラモードもログに含める ▼▼▼
            Debug.Log($"【Current Camera Mode】: {config.viewMode}");
            Debug.Log($"【Request Debug】Sending JSON:{debugJson}");
            // ▲▲▲ 修正ここまで ▲▲▲

            // 置換処理（Replace）を行わず、そのまま表示します
            // Debug.Log($"【Request Debug】FULL JSON (Warning: Huge Data){jsonBody}");
        }
        // ▲▲▲ 追加ここまで ▲▲▲

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
                Debug.Log("RAW JSON: " + rawJson); // ★この行を追加！
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
        // ▼▼▼ 修正: 指定した固定解像度を使用する ▼▼▼
        int width = captureWidth;
        int height = captureHeight;

        // RenderTextureを作成 (指定サイズで)
        RenderTexture renderTexture = new RenderTexture(width, height, 24);
        camera.targetTexture = renderTexture;
        
        // レンダリング
        camera.Render();
        
        RenderTexture.active = renderTexture;
        
        // Texture2Dも同じサイズで作る
        Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
        
        // 読み込み範囲も (0, 0, width, height) にする
        screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenshot.Apply();
        
        // 後始末
        camera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(renderTexture);
        
        return screenshot;
    }

    // ▼▼▼ 修正: 引数でカメラを受け取るように変更 ▼▼▼
    // 上半分=cam1(Front), 下半分=cam2(Top)
    private Texture2D CaptureCombinedView(Camera cam1, Camera cam2)
    {
        int w = captureWidth;
        int h = captureHeight;
        int totalW = w;
        int totalH = h * 2;

        Texture2D combinedTex = new Texture2D(totalW, totalH, TextureFormat.RGB24, false);

        // 1. 上半分 (Front Camera)
        if (cam1 != null)
        {
            Texture2D tex1 = CaptureCameraView(cam1);
            combinedTex.SetPixels(0, h, w, h, tex1.GetPixels());
            Destroy(tex1);
        }

        // 2. 下半分 (Top Camera)
        if (cam2 != null)
        {
            Texture2D tex2 = CaptureCameraView(cam2);
            combinedTex.SetPixels(0, 0, w, h, tex2.GetPixels());
            Destroy(tex2);
        }

        // 区切り線
        int borderThickness = 6;
        Color borderColor = Color.green;
        Color[] borderColors = new Color[w * borderThickness];
        for (int i = 0; i < borderColors.Length; i++) borderColors[i] = borderColor;
        int borderY = h - (borderThickness / 2);
        if (borderY < 0) borderY = 0;
        combinedTex.SetPixels(0, borderY, w, borderThickness, borderColors);

        combinedTex.Apply();
        return combinedTex;
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

    // ▼▼▼ 追加: オプション送信用クラス ▼▼▼
    [System.Serializable]
    public class OllamaOptions
    {
        public int num_predict; // 最大トークン数
        public float temperature; // 創造性
        public int num_ctx;     // コンテキスト長 (画像のメモリ確保に必須)
    }
}