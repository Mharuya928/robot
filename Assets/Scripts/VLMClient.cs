using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

public class VLMClient : MonoBehaviour
{
    // =================================================================
    // 1. 設定・依存関係
    // =================================================================
    [Header("Config")]
    [Tooltip("Projectウィンドウで作成した設定ファイル(VLMConfig)をセットしてください")]
    public VLMConfig config;

    [Header("Dependencies")]
    [Tooltip("撮影時にレイキャストの線を消すために制御するCarController")]
    public CarController carController;

    // ▼▼▼ 追加: TimeManagerへの参照 ▼▼▼
    [Tooltip("タイマーを開始させるためにセットしてください")]
    public TimeManager timeManager;
    // ▲▲▲ 追加ここまで ▲▲▲

    // =================================================================
    // 2. カメラ設定 (各視点で使用するカメラを割り当て)
    // =================================================================
    [Header("Camera Setup")]
    [Tooltip("FPSモード / MultiView(上) / Surround(左上) で使用するカメラ")]
    public Camera frontCamera;

    [Tooltip("Surround(右上) で使用する後方カメラ")]
    public Camera backCamera; 

    [Tooltip("MultiView(下) で使用する俯瞰カメラ")]
    public Camera topCamera;

    [Tooltip("Surround(左下) で使用する左側面カメラ")]
    public Camera leftCamera;

    [Tooltip("Surround(右下) で使用する右側面カメラ")]
    public Camera rightCamera;

    [SerializeField] private TMP_Text VLMText;

    // =================================================================
    // 3. 通信・撮影設定
    // =================================================================
    [Header("Ollama Connection")]
    public string ollamaUrl = "http://localhost:11434/api/chat";

    [Header("Capture Settings")]
    [Tooltip("VLMに送る画像の幅。小さいほど高速です。(推奨: 512)")]
    public int captureWidth = 512;
    
    [Tooltip("VLMに送る画像の高さ。(推奨: 512)")]
    public int captureHeight = 512;
    
    [Header("Input")]
    [Tooltip("手動でVLM（写真撮影）を起動するキー")]
    public KeyCode vlmActivationKey = KeyCode.Tab;

    // =================================================================
    // 4. コ・パイロット (自動警告) 設定 ★追加機能★
    // =================================================================
    [Header("Co-pilot / Auto Warning Settings")]
    [Tooltip("チェックを入れると、障害物が近づいた時に自動でAIに問い合わせます")]
    public bool enableAutoWarning = true; 

    [Tooltip("障害物がこの距離(m)以内に入ったら自動発動します")]
    public float autoWarningDistance = 2.0f; 
    
    [Tooltip("一度警告したら、次の警告まで何秒待つか（連続発動の防止）")]
    public float warningCooldown = 5.0f; 

    // 内部変数
    private float lastWarningTime = -100f; // 前回の警告時刻
    private bool isProcessing = false;     // 現在AIと通信中かどうか
    [Header("Image Save Settings")]
    public string saveFolderName = "Images";

    // =================================================================
    // 初期化 (Start)
    // =================================================================
    void Start()
    {
        // 必須設定のチェック
        if (config == null) 
            Debug.LogError("VLM Config が設定されていません！ Projectウィンドウで作成してセットしてください。");

        // UI表示の更新
        if (VLMText != null)
        {
            string modelName = config != null ? config.ModelName : "Unknown";
            VLMText.text = $"VLM: Ready ({modelName})";
        }

        // カメラ設定の警告（開発者が気づきやすいように）
        if (frontCamera == null) Debug.LogWarning("Front Camera 未設定");
        if (backCamera == null) Debug.LogWarning("Back Camera 未設定");
        if (leftCamera == null) Debug.LogWarning("Left Camera 未設定");
        if (rightCamera == null) Debug.LogWarning("Right Camera 未設定");

        Debug.Log("VLM Client Initialized.");
    }

    // =================================================================
    // メインループ (Update)
    // =================================================================
    void Update()
    {
        // ---------------------------------------------------------
        // A. 手動トリガー (キー入力)
        // ---------------------------------------------------------
        if (Input.GetKeyDown(vlmActivationKey) && !isProcessing && config != null)
        {
            // ▼▼▼ 追加: キーを押したらタイマー開始！ ▼▼▼
            if (timeManager != null)
            {
                timeManager.StartTimer();
            }
            // ▲▲▲ 追加ここまで ▲▲▲
            StartCoroutine(SendRequestToOllama());
        }

        // ---------------------------------------------------------
        // B. 自動安全トリガー (センサー連携)
        // ---------------------------------------------------------
        // 条件: 機能がON && AIが暇 && 設定がある
        if (enableAutoWarning && !isProcessing && config != null)
        {
            RaycastHit hit;

            // ▼▼▼ 修正: CarControllerに合わせて、少し高い位置(0.5m)から発射する ▼▼▼
            Vector3 rayOrigin = transform.position + new Vector3(0, 0.5f, 0);

            // 車の正面(transform.forward)に向かって見えない線を飛ばす
            if (Physics.Raycast(rayOrigin, transform.forward, out hit, autoWarningDistance))
            {
                // 壁などにぶつかった、かつ クールダウン時間が経過している場合
                if (Time.time - lastWarningTime > warningCooldown)
                {
                    Debug.LogWarning($"【Auto】障害物接近！({hit.distance:F1}m) AIに危険性を確認させます");
                    lastWarningTime = Time.time; // タイマー更新
                    
                    // AIへのリクエスト開始
                    StartCoroutine(SendRequestToOllama());
                }
            }
        }
    }

    // =================================================================
    // AI通信のメイン処理 (コルーチン)
    // =================================================================
    private IEnumerator SendRequestToOllama()
    {
        if (isProcessing) yield break; // 二重実行防止
        isProcessing = true;

        if (VLMText != null) VLMText.text = "VLM: Processing...";

        // --- 1. 使用モジュールのログ出力 ---
        StringBuilder moduleLog = new StringBuilder();
        moduleLog.AppendLine("【Active Modules (使用中のモジュール)】");
        if (config.activeModules != null && config.activeModules.Count > 0)
        {
            foreach (var module in config.activeModules)
                if (module != null) moduleLog.AppendLine($"- {module.moduleName}");
        }
        else
        {
            moduleLog.AppendLine("- None (Free Form Mode / 自由会話モード)");
        }
        Debug.Log(moduleLog.ToString());

        // --- 2. 画像撮影処理 ---
        string base64Image = null;
        
        // 撮影の瞬間だけ赤い線を消す
        if (carController != null) carController.SetRaycastLineVisibility(false);
        yield return null; // 1フレーム待つ

        Texture2D photo = null;

        // ConfigのViewModeに従って、適切なカメラの組み合わせで撮影する
        switch (config.viewMode)
        {
            case VLMConfig.ViewMode.FPS:
                if (frontCamera != null) photo = CaptureCameraView(frontCamera);
                else Debug.LogError("FPSモードですが、Front Cameraが設定されていません。");
                break;

            case VLMConfig.ViewMode.MultiView:
                // 前方 + 俯瞰 の縦結合
                if (frontCamera != null && topCamera != null) 
                    photo = CaptureCombinedView(frontCamera, topCamera);
                else Debug.LogError("MultiViewモードエラー: カメラ不足");
                break;

            case VLMConfig.ViewMode.SurroundView:
                // 前後左右の4枚結合
                if (frontCamera != null && backCamera != null && leftCamera != null && rightCamera != null)
                    photo = CaptureSurroundView(frontCamera, backCamera, leftCamera, rightCamera);
                else Debug.LogError("SurroundViewエラー: カメラ不足(4台必要)");
                break;
        }

        if (photo == null)
        {
             Debug.LogError("撮影に失敗しました (Photo is null)");
             isProcessing = false;
             yield break;
        }

        // 線を再表示
        if (carController != null) carController.SetRaycastLineVisibility(true);
        
        // 画像をバイト配列に変換 -> 保存 -> Base64化
        byte[] bytes = photo.EncodeToJPG();
        SaveImageToFile(bytes);
        base64Image = System.Convert.ToBase64String(bytes);
        Destroy(photo); // メモリ解放
        
        // --- 3. プロンプトとJSONの準備 ---

        // プロンプトのエスケープ処理
        string currentPromptText = config.CurrentPrompt;
        string safePrompt = currentPromptText.Replace("\"", "\\\"").Replace("\n", "\\n");

        // Ollamaオプション作成 (トークン数やコンテキストサイズ)
        OllamaOptions options = new OllamaOptions
        {
            num_predict = config.maxTokens,
            temperature = config.temperature,
            num_ctx = config.contextSize
        };
        string optionsJson = JsonUtility.ToJson(options);

        // 送信JSONの構築
        string jsonBody = "";
        bool isFreeForm = (config.activeModules == null || config.activeModules.Count == 0);

        if (isFreeForm)
        {
            // 自由記述モード
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
            // スキーマ(構造化)モード
            string schemaJson = BuildDynamicSchemaJson(config.activeModules);
            
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

        // --- 4. デバッグログ出力 ---
        if (!string.IsNullOrEmpty(jsonBody))
        {
            string debugJson = jsonBody;
            // ログが埋め尽くされないよう、画像データ部分は隠す
            if (!string.IsNullOrEmpty(base64Image))
            {
                debugJson = debugJson.Replace(base64Image, "<IMAGE_DATA_OMITTED>");
            }
            Debug.Log($"【Current Camera Mode】: {config.viewMode}");
            Debug.Log($"【Request Debug】Sending JSON:{debugJson}");
        }

        // --- 5. HTTP通信処理 ---
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
                if (VLMText != null) VLMText.text = "Error: " + request.error;
            }
            else
            {
                string rawJson = request.downloadHandler.text;
                Debug.Log("RAW JSON: " + rawJson);
                
                // 応答から本文を抽出
                string contentJson = ExtractContent(rawJson);
                Debug.Log("AI Response: " + contentJson);

                if (isFreeForm)
                {
                    if (VLMText != null) VLMText.text = contentJson;
                }
                else
                {
                    DisplayDynamicResult(contentJson);
                }
            }
        }

        isProcessing = false;
    }

    // =================================================================
    // ヘルパー: JSON Schemaの動的生成
    // =================================================================
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
                
                // 配列型の定義
                if (prop.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Array)
                {
                    typeDef = $@"{{ ""type"": ""array"", ""items"": {{ ""type"": ""string"" }}, ""description"": ""{prop.description}"" }}";
                }
                // Enum型の定義
                else if (prop.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Enum)
                {
                    string[] opts = prop.enumOptions.Split(',');
                    for(int i=0; i<opts.Length; i++) opts[i] = opts[i].Trim(); 
                    string enumStr = string.Join("\",\"", opts); 
                    typeDef = $@"{{ ""type"": ""string"", ""enum"": [""{enumStr}""], ""description"": ""{prop.description}"" }}";
                }
                // Boolean型の定義
                else if (prop.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Boolean)
                {
                    typeDef = $@"{{ ""type"": ""boolean"", ""description"": ""{prop.description}"" }}";
                }
                // String型の定義
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

    // =================================================================
    // ヘルパー: 結果の表示 (色付け・整形)
    // =================================================================
    private void DisplayDynamicResult(string jsonResponse)
    {
        StringBuilder sb = new StringBuilder();

        foreach (var module in config.activeModules)
        {
            if (module == null) continue;

            sb.AppendLine($"<b>[{module.moduleName}]</b>");
            
            foreach (var prop in module.properties)
            {
                // JSONからキーに対応する値を正規表現で抜く
                string pattern = $"\"{prop.name}\"\\s*:\\s*(\\[.*?\\]|\".*?\")";
                Match match = Regex.Match(jsonResponse, pattern, RegexOptions.Singleline);

                if (match.Success)
                {
                    string val = match.Groups[1].Value.Trim();

                    // 配列やクォートの除去
                    if (val.StartsWith("[")) val = val.Replace("[", "").Replace("]", "").Replace("\"", "");
                    else val = val.Trim('"');
                    
                    val = val.Replace("**", ""); // 強調記号の削除

                    if (string.IsNullOrWhiteSpace(val)) val = "None";

                    // 色付けロジック
                    string displayVal = val;
                    string lowerVal = val.ToLower();
                    if (lowerVal.Contains("high") || lowerVal.Contains("danger") || lowerVal == "true" || lowerVal.Contains("critical")) 
                        displayVal = $"<color=red>{val}</color>";
                    else if (lowerVal.Contains("safe") || lowerVal == "false" || lowerVal.Contains("clear") || lowerVal == "none")
                        displayVal = $"<color=green>{val}</color>";
                    else if (lowerVal.Contains("caution") || lowerVal.Contains("warning") || lowerVal.Contains("medium"))
                        displayVal = $"<color=yellow>{val}</color>";
                    
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

    // =================================================================
    // ヘルパー: 本文抽出とクリーニング
    // =================================================================
    private string ExtractContent(string fullJson)
    {
        try
        {
            string content = JsonUtility.FromJson<OllamaResponse>(fullJson).message.content;
            // アスタリスク(**)を一括削除して綺麗にする
            if (!string.IsNullOrEmpty(content))
            {
                content = content.Replace("**", "");
            }
            return content;
        }
        catch
        {
            return fullJson;
        }
    }

    // =================================================================
    // ヘルパー: 画像撮影・加工
    // =================================================================

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

    // 単一カメラ撮影
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

    // 2枚結合 (MultiView: 上下)
    private Texture2D CaptureCombinedView(Camera cam1, Camera cam2)
    {
        int w = captureWidth;
        int h = captureHeight;
        int totalW = w;
        int totalH = h * 2;
        Texture2D combinedTex = new Texture2D(totalW, totalH, TextureFormat.RGB24, false);

        if (cam1 != null)
        {
            Texture2D tex1 = CaptureCameraView(cam1);
            combinedTex.SetPixels(0, h, w, h, tex1.GetPixels());
            Destroy(tex1);
        }
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

    // 4枚結合 (SurroundView: 田の字)
    private Texture2D CaptureSurroundView(Camera front, Camera back, Camera left, Camera right)
    {
        int w = captureWidth; 
        int h = captureHeight;
        int totalW = w * 2;
        int totalH = h * 2;
        Texture2D combined = new Texture2D(totalW, totalH, TextureFormat.RGB24, false);

        // [Front (左上)] [Back  (右上)]
        // [Left  (左下)] [Right (右下)]

        Texture2D tFront = CaptureCameraView(front);
        combined.SetPixels(0, h, w, h, tFront.GetPixels());
        Destroy(tFront);

        Texture2D tBack = CaptureCameraView(back);
        combined.SetPixels(w, h, w, h, tBack.GetPixels());
        Destroy(tBack);

        Texture2D tLeft = CaptureCameraView(left);
        combined.SetPixels(0, 0, w, h, tLeft.GetPixels());
        Destroy(tLeft);

        Texture2D tRight = CaptureCameraView(right);
        combined.SetPixels(w, 0, w, h, tRight.GetPixels());
        Destroy(tRight);

        // 区切り線 (十字)
        int thickness = 6;
        Color lineColor = Color.green;
        
        // 横線
        Color[] hLine = new Color[totalW * thickness];
        for(int i=0; i<hLine.Length; i++) hLine[i] = lineColor;
        combined.SetPixels(0, h - (thickness/2), totalW, thickness, hLine);

        // 縦線
        Color[] vLine = new Color[thickness * totalH];
        for(int i=0; i<vLine.Length; i++) vLine[i] = lineColor;
        combined.SetPixels(w - (thickness/2), 0, thickness, totalH, vLine);

        combined.Apply();
        return combined;
    }

    // =================================================================
    // 内部クラス (JSONシリアライズ用)
    // =================================================================
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
    [System.Serializable]
    public class OllamaOptions
    {
        public int num_predict;
        public float temperature;
        public int num_ctx;
    }
}