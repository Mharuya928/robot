using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System; // Action用
using System.Diagnostics; // Stopwatch用
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;

// ▼▼▼ この1行を追加してください！ ▼▼▼
using Debug = UnityEngine.Debug;
// ▲▲▲ 追加ここまで ▲▲▲

public class VLMClient : MonoBehaviour
{
    // =================================================================
    // 1. 設定・依存関係
    // =================================================================
    // ▼▼▼ 変更: 切り替え機能を追加 ▼▼▼
    [Header("Config Selection")]
    [Tooltip("使用するコンフィグを選択してください")]
    public ConfigType selectConfig = ConfigType.ConfigA;

    [Tooltip("Aパターンの設定ファイル")]
    public VLMConfig configA;

    [Tooltip("Bパターンの設定ファイル")]
    public VLMConfig configB;

    [Header("Active Config (Auto Assigned)")]
    [Tooltip("現在アクティブになっている設定（自動でセットされます）")]
    public VLMConfig config; // ← 既存の変数はそのまま残し、中身を差し替えます

    public enum ConfigType
    {
        ConfigA,
        ConfigB
    }

    // エディタ上で値を変更した瞬間に反映させる
    void OnValidate()
    {
        ApplyConfigSelection();
    }

    // ゲーム開始時にも確実に適用する
    void Awake()
    {
        ApplyConfigSelection();
    }

    private void ApplyConfigSelection()
    {
        if (selectConfig == ConfigType.ConfigA)
        {
            config = configA;
        }
        else
        {
            config = configB;
        }
    }
    // ▲▲▲ 変更ここまで ▲▲▲

    [Header("Debug")]
    [Tooltip("チェックを入れるとOllamaに通信せず、ダミー応答で実験を高速進行させます")]
    public bool debugMockMode = false; // ← これを追加

    [Header("Dependencies")]
    public CarController carController;

    // =================================================================
    // 実験用設定・UI
    // =================================================================
    [Header("Experiment Settings")]
    [Tooltip("実験開始ボタン")]
    public Button startExperimentButton;
    [Tooltip("実験進捗表示用テキスト")]
    public TMP_Text experimentStatusText;

    private string logFilePath; // ログ保存先

    // 計測結果受け渡し用構造体
    public struct InferenceMetrics
    {
        public long t_start_ms;
        public long t_end_ms;
        public long latency_ms;
        public int out_chars;
        public int out_bytes;
        public string responseContent;
        public bool isSuccess;
    }

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

    static readonly Color32 FRONT = new Color32(255, 0, 0, 255); // 赤
    static readonly Color32 BACK = new Color32(0, 0, 255, 255); // 青
    static readonly Color32 LEFT = new Color32(0, 255, 0, 255); // 緑
    static readonly Color32 RIGHT = new Color32(255, 255, 0, 255); // 黄


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

        // ▼▼▼ 追加: 実験用初期化 ▼▼▼
        if (startExperimentButton != null)
        {
            startExperimentButton.onClick.AddListener(() => StartCoroutine(RunBaselineExperiment()));
        }

        // ログ保存パスの決定 (Assets/Testlogs/inference_log.csv)
        string folder = Path.Combine(Application.dataPath, "Testlogs");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        logFilePath = Path.Combine(folder, "inference_log.csv");
        // ▲▲▲ 追加ここまで ▲▲▲

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
    // ★★★ 卒論評価用実験コルーチン (画像固定版) ★★★
    // =================================================================
    private IEnumerator RunBaselineExperiment()
    {
        if (isProcessing) { Debug.LogWarning("実験不可: 他の処理中"); yield break; }
        
        bool originalAutoWarning = enableAutoWarning;
        enableAutoWarning = false;

        Debug.Log($"【Experiment Start】Saving to: {logFilePath}");
        if (experimentStatusText) experimentStatusText.text = "Initializing...";

        // ▼▼▼ 追加: ここで1回だけ撮影してキャッシュする！ ▼▼▼
        if (experimentStatusText) experimentStatusText.text = "Capturing base images...";
        yield return null; // 1フレ待ってUI更新
        List<string> fixedImages = CaptureAndEncodeAllImages();
        Debug.Log("Base images captured and cached.");
        // ▲▲▲ 追加ここまで ▲▲▲

        // CSVヘッダ (変更なし)
        if (!File.Exists(logFilePath)) File.WriteAllText(logFilePath, "cond,i,t_start_ms,t_end_ms,latency_ms,out_chars,out_bytes,parse_ok\n", Encoding.UTF8);
        else File.AppendAllText(logFilePath, "\n", Encoding.UTF8);

        // ウォームアップ (変更: キャッシュ画像を渡す)
        if (experimentStatusText) experimentStatusText.text = "Warming up...";
        for (int w = 0; w < 5; w++)
        {
            selectConfig = ConfigType.ConfigA; ApplyConfigSelection();
            yield return StartCoroutine(SendRequestToOllama(null, fixedImages)); // ★画像を渡す

            selectConfig = ConfigType.ConfigB; ApplyConfigSelection();
            yield return StartCoroutine(SendRequestToOllama(null, fixedImages)); // ★画像を渡す
        }

        // 本番計測 (変更: キャッシュ画像を渡す)
        int totalIterations = 100;
        for (int i = 1; i <= totalIterations; i++)
        {
            if (experimentStatusText) experimentStatusText.text = $"Progress: {i}/{totalIterations}";

            // A条件
            selectConfig = ConfigType.ConfigA; ApplyConfigSelection();
            InferenceMetrics metricsA = new InferenceMetrics();
            bool doneA = false;
            yield return StartCoroutine(SendRequestToOllama((m) => { metricsA = m; doneA = true; }, fixedImages)); // ★画像を渡す
            while (!doneA) yield return null;
            
            int parseOk = CheckJsonParse(metricsA.responseContent);
            File.AppendAllText(logFilePath, $"A,{i},{metricsA.t_start_ms},{metricsA.t_end_ms},{metricsA.latency_ms},{metricsA.out_chars},{metricsA.out_bytes},{parseOk}\n", Encoding.UTF8);
            yield return new WaitForSeconds(0.1f);

            // B条件
            selectConfig = ConfigType.ConfigB; ApplyConfigSelection();
            InferenceMetrics metricsB = new InferenceMetrics();
            bool doneB = false;
            yield return StartCoroutine(SendRequestToOllama((m) => { metricsB = m; doneB = true; }, fixedImages)); // ★画像を渡す
            while (!doneB) yield return null;

            File.AppendAllText(logFilePath, $"B,{i},{metricsB.t_start_ms},{metricsB.t_end_ms},{metricsB.latency_ms},{metricsB.out_chars},{metricsB.out_bytes},\n", Encoding.UTF8);
            yield return new WaitForSeconds(0.1f);
        }

        if (experimentStatusText) experimentStatusText.text = "Complete!";
        Debug.Log($"【Experiment Complete】Log saved at: {logFilePath}");
        enableAutoWarning = originalAutoWarning;
        selectConfig = ConfigType.ConfigA; ApplyConfigSelection();
    }

    // 最小限のJSONパース判定
    private int CheckJsonParse(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        text = text.Trim();
        // 簡易判定: {}で囲まれているか
        if (text.StartsWith("{") && text.EndsWith("}")) return 1;
        return 0;
    }

    // 画像撮影～Base64変換を一括で行うヘルパー
    private List<string> CaptureAndEncodeAllImages()
    {
        List<Texture2D> capturedTextures = new List<Texture2D>();
        List<string> base64Results = new List<string>();

        // 固定4方向 (SurroundView)
        if (frontCamera) { Texture2D t = CaptureCameraView(frontCamera); AddColorIndicator(t, FRONT); capturedTextures.Add(t); }
        if (backCamera)  { Texture2D t = CaptureCameraView(backCamera);  AddColorIndicator(t, BACK);  capturedTextures.Add(t); }
        if (leftCamera)  { Texture2D t = CaptureCameraView(leftCamera);  AddColorIndicator(t, LEFT);  capturedTextures.Add(t); }
        if (rightCamera) { Texture2D t = CaptureCameraView(rightCamera); AddColorIndicator(t, RIGHT); capturedTextures.Add(t); }

        for (int i = 0; i < capturedTextures.Count; i++)
        {
            byte[] bytes = capturedTextures[i].EncodeToJPG();
            base64Results.Add(Convert.ToBase64String(bytes));
            Destroy(capturedTextures[i]);
        }
        return base64Results;
    }

    // =================================================================
    // AI通信のメイン処理 (キャッシュ対応版)
    // =================================================================
    // ▼▼▼ 変更: 第2引数に cachedImages を追加 ▼▼▼
    private IEnumerator SendRequestToOllama(System.Action<InferenceMetrics> onComplete = null, List<string> cachedImages = null)
    {
        if (isProcessing) yield break;
        isProcessing = true;
        if (VLMText != null) VLMText.text = "VLM: Processing...";

        bool isExperimentMode = (onComplete != null);

        // ★計測開始
        Stopwatch sw = new Stopwatch();
        sw.Start();
        long t_start = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);

        // モジュールログ (通常時のみ)
        if (!isExperimentMode)
        {
            StringBuilder moduleLog = new StringBuilder();
            if (config.activeModules != null) foreach (var m in config.activeModules) if (m) moduleLog.AppendLine(m.moduleName);
            Debug.Log($"Active Modules:\n{moduleLog}");
        }

        if (carController != null) carController.SetRaycastLineVisibility(false);
        yield return null;

        // =========================================================
        // 1. 画像収集 (キャッシュ判定ロジックへ変更)
        // =========================================================
        List<string> base64Images = new List<string>();
        List<string> imageLabels = new List<string>();

        // ▼▼▼ 変更: キャッシュがあれば使い、なければ撮影する分岐 ▼▼▼
        if (cachedImages != null && cachedImages.Count > 0)
        {
            // A. キャッシュを使用 (実験モード・高速化)
            base64Images = cachedImages;
            
            // ラベルは固定 (SurroundView順)
            imageLabels.Add("これは車両前方。以後この対応を保持。");
            imageLabels.Add("これは車両後方。以後この対応を保持。");
            imageLabels.Add("これは車両左側。以後この対応を保持。");
            imageLabels.Add("これは車両右側。以後この対応を保持。");
        }
        else
        {
            // B. 新規撮影 (通常モード or キャッシュなし)
            List<Texture2D> capturedTextures = new List<Texture2D>();

            if (isExperimentMode)
            {
                // 実験モードだがキャッシュがない場合（念のため）
                if (frontCamera) { Texture2D t = CaptureCameraView(frontCamera); AddColorIndicator(t, FRONT); capturedTextures.Add(t); imageLabels.Add("これは車両前方。以後この対応を保持。"); }
                if (backCamera)  { Texture2D t = CaptureCameraView(backCamera);  AddColorIndicator(t, BACK);  capturedTextures.Add(t); imageLabels.Add("これは車両後方。以後この対応を保持。"); }
                if (leftCamera)  { Texture2D t = CaptureCameraView(leftCamera);  AddColorIndicator(t, LEFT);  capturedTextures.Add(t); imageLabels.Add("これは車両左側。以後この対応を保持。"); }
                if (rightCamera) { Texture2D t = CaptureCameraView(rightCamera); AddColorIndicator(t, RIGHT); capturedTextures.Add(t); imageLabels.Add("これは車両右側。以後この対応を保持。"); }
            }
            else
            {
                // 通常プレイ: Config設定に従う
                switch (config.viewMode)
                {
                    case VLMConfig.ViewMode.FPS:
                        if (frontCamera) { capturedTextures.Add(CaptureCameraView(frontCamera)); imageLabels.Add("これは車両前方の映像です。"); }
                        break;
                    case VLMConfig.ViewMode.MultiView:
                        if (frontCamera) { capturedTextures.Add(CaptureCameraView(frontCamera)); imageLabels.Add("これは車両前方。"); }
                        if (topCamera)   { capturedTextures.Add(CaptureCameraView(topCamera));   imageLabels.Add("これは車両俯瞰(上から)。"); }
                        break;
                    case VLMConfig.ViewMode.SurroundView:
                        if (frontCamera) { Texture2D t = CaptureCameraView(frontCamera); AddColorIndicator(t, FRONT); capturedTextures.Add(t); imageLabels.Add("これは車両前方。以後この対応を保持。"); }
                        if (backCamera)  { Texture2D t = CaptureCameraView(backCamera);  AddColorIndicator(t, BACK);  capturedTextures.Add(t); imageLabels.Add("これは車両後方。以後この対応を保持。"); }
                        if (leftCamera)  { Texture2D t = CaptureCameraView(leftCamera);  AddColorIndicator(t, LEFT);  capturedTextures.Add(t); imageLabels.Add("これは車両左側。以後この対応を保持。"); }
                        if (rightCamera) { Texture2D t = CaptureCameraView(rightCamera); AddColorIndicator(t, RIGHT); capturedTextures.Add(t); imageLabels.Add("これは車両右側。以後この対応を保持。"); }
                        break;
                }
            }

            // 撮影した画像を変換＆保存
            for (int i = 0; i < capturedTextures.Count; i++)
            {
                byte[] bytes = capturedTextures[i].EncodeToJPG();
                SaveImageToFile(bytes, i); // 通常時は保存
                base64Images.Add(Convert.ToBase64String(bytes));
                Destroy(capturedTextures[i]);
            }
        }
        // ▲▲▲ 変更ここまで ▲▲▲

        if (carController != null) carController.SetRaycastLineVisibility(true);

        // =========================================================
        // 3. メッセージ構築 (以降は変更なし)
        // =========================================================
        StringBuilder messagesJson = new StringBuilder();
        messagesJson.Append("[");

        for (int i = 0; i < base64Images.Count; i++)
        {
            string label = (i < imageLabels.Count) ? imageLabels[i] : "画像情報";
            messagesJson.Append($@"{{ ""role"": ""user"", ""content"": ""{label}"", ""images"": [""{base64Images[i]}""] }},");
        }
        
        string safePrompt = config.CurrentPrompt.Replace("\"", "\\\"").Replace("\n", "\\n");
        messagesJson.Append($@"{{ ""role"": ""user"", ""content"": ""{safePrompt}"" }}");
        messagesJson.Append("]");

        // =========================================================
        // 4. リクエスト作成 (以降は変更なし)
        // =========================================================
        OllamaOptions options;
        if (isExperimentMode) options = new OllamaOptions { num_predict = 1000, temperature = 0.5f, num_ctx = 4096 };
        else options = new OllamaOptions { num_predict = config.maxTokens, temperature = config.temperature, num_ctx = config.contextSize };
        
        string optionsJson = JsonUtility.ToJson(options);
        string jsonBody = "";
        bool isFreeForm = (config.activeModules == null || config.activeModules.Count == 0);

        if (isFreeForm) jsonBody = $@"{{ ""model"": ""{config.ModelName}"", ""stream"": false, ""options"": {optionsJson}, ""messages"": {messagesJson.ToString()} }}";
        else
        {
            string schemaJson = BuildDynamicSchemaJson(config.activeModules);
            jsonBody = $@"{{ ""model"": ""{config.ModelName}"", ""stream"": false, ""options"": {optionsJson}, ""messages"": {messagesJson.ToString()}, ""format"": {schemaJson} }}";
        }

        // =========================================================
        // ★★★ デバッグ用モック（通信スキップ） ★★★
        // =========================================================
        if (debugMockMode)
        {
            // 少しだけ待機（処理っぽく見せるため）
            yield return new WaitForSeconds(0.05f);

            sw.Stop();
            long t_end = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);

            // ダミーの応答内容 (A条件ならJSON、B条件なら適当なテキスト)
            string mockContent = "";
            bool isJsonMode = (config.activeModules != null && config.activeModules.Count > 0);

            if (isJsonMode)
            {
                // JSON形式のダミー
                mockContent = "{ \"障害物情報\": { \"障害物一覧\": [] }, \"左右比較\": \"なし\", \"推奨行動\": \"停止\" }";
            }
            else
            {
                // 自由記述のダミー
                mockContent = "これはダミーの応答です。障害物はありません。";
            }

            // ログ出力
            Debug.Log($"[Mock] Skipped Network Request. Latency: {sw.ElapsedMilliseconds}ms");
            
            // UI表示
            if (isFreeForm && VLMText) VLMText.text = mockContent;
            else DisplayDynamicResult(mockContent);

            // 実験用コールバックを呼んで終了
            if (isExperimentMode && onComplete != null)
            {
                InferenceMetrics metrics = new InferenceMetrics
                {
                    t_start_ms = t_start,
                    t_end_ms = t_end,
                    latency_ms = sw.ElapsedMilliseconds,
                    out_chars = mockContent.Length,
                    out_bytes = Encoding.UTF8.GetByteCount(mockContent),
                    responseContent = mockContent,
                    isSuccess = true
                };
                onComplete(metrics);
            }
            
            isProcessing = false;
            yield break; // ★ここで強制終了（通信に行かせない）
        }

        if (!string.IsNullOrEmpty(jsonBody))
        {
            string debugJson = jsonBody;
            
            // Base64の長い文字列を、短いタグ <IMAGE: ...> に置換して見やすくする
            for (int i = 0; i < base64Images.Count; i++)
            {
                string camLabel = "Unknown";
                
                // 画像枚数からカメラを推測 (実験モードは4枚固定)
                if (base64Images.Count == 4)
                {
                    switch (i)
                    {
                        case 0: camLabel = "Front(前方)"; break;
                        case 1: camLabel = "Back(後方)"; break;
                        case 2: camLabel = "Left(左側)"; break;
                        case 3: camLabel = "Right(右側)"; break;
                    }
                }
                else if (base64Images.Count == 2)
                {
                    switch (i) { case 0: camLabel = "Front(前方)"; break; case 1: camLabel = "Top(俯瞰)"; break; }
                }
                else
                {
                    camLabel = "Front(前方)";
                }

                debugJson = debugJson.Replace(base64Images[i], $"<IMAGE: {camLabel}>");
            }

            Debug.Log($"【Request Debug】Config: {config.name}\nSending JSON: {debugJson}");
        }

        // 5. 送信
        using (UnityWebRequest request = new UnityWebRequest(ollamaUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            sw.Stop();
            long t_end = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
            
            string contentResult = "";

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + request.error);
                if (VLMText) VLMText.text = "Error: " + request.error;
            }
            else
            {
                string rawJson = request.downloadHandler.text;
                contentResult = ExtractContent(rawJson);
                
                if (!isExperimentMode)
                {
                    OllamaResponse responseData = JsonUtility.FromJson<OllamaResponse>(rawJson);
                    int used = responseData.prompt_eval_count;
                    int limit = (isExperimentMode) ? 4096 : config.contextSize;
                    Debug.Log($"【Token Usage】 Used: {used} / Limit: {limit}");
                }
                Debug.Log("AI Response: " + contentResult);
                
                if (isFreeForm && VLMText) VLMText.text = contentResult;
                else DisplayDynamicResult(contentResult);
            }

            if (isExperimentMode && onComplete != null)
            {
                InferenceMetrics metrics = new InferenceMetrics
                {
                    t_start_ms = t_start,
                    t_end_ms = t_end,
                    latency_ms = sw.ElapsedMilliseconds,
                    out_chars = contentResult.Length,
                    out_bytes = Encoding.UTF8.GetByteCount(contentResult),
                    responseContent = contentResult,
                    isSuccess = (request.result == UnityWebRequest.Result.Success)
                };
                onComplete(metrics);
            }
        }
        isProcessing = false;
    }

    // =================================================================
    // ヘルパー: カメラごとのラベル生成
    // =================================================================
    private string GetViewLabel(VLMConfig.ViewMode mode, int index)
    {
        // SurroundView (4枚) の場合
        if (mode == VLMConfig.ViewMode.SurroundView)
        {
            switch (index)
            {
                case 0: return "これは車両前方。以後この対応を保持。";
                case 1: return "これは車両後方。以後この対応を保持。";
                case 2: return "これは車両左側。以後この対応を保持。";
                case 3: return "これは車両右側。以後この対応を保持。";
            }
        }
        // MultiView (2枚) の場合
        else if (mode == VLMConfig.ViewMode.MultiView)
        {
            switch (index)
            {
                case 0: return "これは車両前方。";
                case 1: return "これは車両俯瞰(上から)。";
            }
        }
        // FPS (1枚) の場合
        else
        {
            return "これは車両前方の映像です。";
        }

        return "画像情報";
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
            foreach (var p in m.properties)
            {
                // ▼▼▼ 修正: Optionalでなければ必須リストに追加 ▼▼▼
                if (!p.isOptional)
                {
                    req.Add($"\"{p.name}\"");
                }
                // ▲▲▲ 修正ここまで ▲▲▲
            }
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
        foreach (var p in module.properties)
        {
            // ▼▼▼ 修正: Optionalでなければ必須リストに追加 ▼▼▼
            if (!p.isOptional)
            {
                req.Add($"\"{p.name}\"");
            }
            // ▲▲▲ 修正ここまで ▲▲▲
        }
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

    // 画像の上端に色帯を追加するヘルパー
    private void AddColorIndicator(Texture2D tex, Color color, int thickness = 20)
    {
        Color[] colors = new Color[tex.width * thickness];
        for (int i = 0; i < colors.Length; i++) colors[i] = color;

        // 上端に線を引く (tex.height - thickness から tex.height まで)
        tex.SetPixels(0, tex.height - thickness, tex.width, thickness, colors);
        tex.Apply();
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

    // =================================================================
    // ヘルパー: 結果の表示（装飾なし版）
    // =================================================================
    private void DisplayDynamicResult(string jsonResponse)
    {
        StringBuilder sb = new StringBuilder();

        foreach (var module in config.activeModules)
        {
            if (module == null) continue;

            sb.AppendLine($"[{module.moduleName}]");
            FormatSchemaRecursive(sb, module, jsonResponse, 0);
            sb.AppendLine();
        }

        // 文字列として確定させる
        string finalResult = sb.ToString();

        // ▼▼▼ 追加: 整形後の結果をコンソールにも出す ▼▼▼
        Debug.Log("【AI Formatted Output】\n" + finalResult);
        // ▲▲▲ 追加ここまで ▲▲▲

        if (VLMText != null) VLMText.text = finalResult;
    }

    // 再帰的にスキーマとJSONを照らし合わせて表示を作る（None非表示版）
    private void FormatSchemaRecursive(StringBuilder sb, VLMSchemaModule module, string jsonContext, int indentLevel)
    {
        string indent = new string(' ', indentLevel * 2);

        foreach (var prop in module.properties)
        {
            string rawValue = ExtractJsonValue(jsonContext, prop.name);

            // 値が見つからない、または "null" の場合はスキップ
            // (必須項目で見つからない場合のみ表示したいならここを調整ですが、今回はNone非表示優先)
            if (string.IsNullOrEmpty(rawValue) || rawValue == "null")
            {
                if (!prop.isOptional)
                {
                    sb.AppendLine($"{indent}- {prop.name}: (Not found)");
                }
                continue;
            }

            if (prop.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Object)
            {
                sb.AppendLine($"{indent}- {prop.name}:");
                if (prop.schemaReference != null)
                {
                    FormatSchemaRecursive(sb, prop.schemaReference, rawValue, indentLevel + 1);
                }
            }
            else if (prop.type == VLMSchemaModule.SchemaPropertyDefinition.PropertyType.Array)
            {
                sb.AppendLine($"{indent}- {prop.name}:");

                string arrayContent = rawValue.Trim();
                if (arrayContent.StartsWith("[") && arrayContent.EndsWith("]"))
                {
                    arrayContent = arrayContent.Substring(1, arrayContent.Length - 2);
                }

                List<string> items = SplitArrayItems(arrayContent);

                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.AppendLine($"{indent}  ---");

                    if (prop.schemaReference != null)
                    {
                        FormatSchemaRecursive(sb, prop.schemaReference, items[i], indentLevel + 1);
                    }
                    else
                    {
                        // 配列の中身が None の場合は表示するか悩みますが、
                        // 通常は空文字が入ることは少ないためそのまま表示します
                        string val = FormatValueColor(items[i]);
                        if (val != "None")
                        {
                            sb.AppendLine($"{indent}  {val}");
                        }
                    }
                }
            }
            else
            {
                // --- String / Enum / Boolean型 ---
                string displayVal = FormatValueColor(rawValue);

                // ▼▼▼ 修正: 値が "None" なら行ごと表示しない ▼▼▼
                if (displayVal != "None")
                {
                    sb.AppendLine($"{indent}- {prop.name}: {displayVal}");
                }
                // ▲▲▲ 修正ここまで ▲▲▲
            }
        }
    }

    // 値を整形するヘルパー (色付けなし版)
    private string FormatValueColor(string val)
    {
        // クォートやカンマ、改行などを除去して綺麗にする
        val = val.Trim(' ', '"', ',', '\n', '\r');

        // 空なら "None" を返す
        if (string.IsNullOrWhiteSpace(val)) return "None";

        // 色タグを付けずにそのまま返す
        return val;
    }

    // =================================================================
    // JSON解析用パーサー (修正版)
    // =================================================================
    private string ExtractJsonValue(string json, string key)
    {
        // キーを探す ("key": )
        string pattern = $"\"{key}\"\\s*:\\s*";
        Match match = Regex.Match(json, pattern);
        if (!match.Success) return null;

        int startIndex = match.Index + match.Length;
        int depth = 0;
        bool inQuote = false;
        StringBuilder result = new StringBuilder();

        for (int i = startIndex; i < json.Length; i++)
        {
            char c = json[i];

            // 引用符の中なら無視して進む
            if (c == '\"' && (i == 0 || json[i - 1] != '\\'))
            {
                inQuote = !inQuote;
                result.Append(c);
                continue;
            }

            if (!inQuote)
            {
                if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    // ▼▼▼ 修正: 構造を閉じるカッコだった場合、これを含めてから終了する ▼▼▼
                    if (depth == 0)
                    {
                        result.Append(c);
                        break;
                    }
                    // ▲▲▲ 修正ここまで ▲▲▲
                }

                // 深さが0（構造の外）で、区切り文字（カンマや親の閉じカッコ）が来たら終了
                if (depth == 0 && (c == ',' || c == '}' || c == ']'))
                {
                    break;
                }

                // 異常系: 親の閉じカッコで行き過ぎた場合
                if (depth < 0) break;
            }

            result.Append(c);
        }

        return result.ToString().Trim();
    }

    // 配列の中身 "{...}, {...}" を個別の要素に分割する
    private List<string> SplitArrayItems(string arrayContent)
    {
        List<string> items = new List<string>();
        int depth = 0;
        bool inQuote = false;
        StringBuilder currentItem = new StringBuilder();

        for (int i = 0; i < arrayContent.Length; i++)
        {
            char c = arrayContent[i];

            if (c == '\"' && (i == 0 || arrayContent[i - 1] != '\\')) inQuote = !inQuote;

            if (!inQuote)
            {
                if (c == '{' || c == '[') depth++;
                if (c == '}' || c == ']') depth--;

                // 深さ0のカンマは区切り文字
                if (depth == 0 && c == ',')
                {
                    if (currentItem.Length > 0) items.Add(currentItem.ToString().Trim());
                    currentItem.Clear();
                    continue;
                }
            }
            currentItem.Append(c);
        }
        if (currentItem.Length > 0) items.Add(currentItem.ToString().Trim());

        return items;
    }

    private string ExtractContent(string fullJson)
    {
        try
        {
            string content = JsonUtility.FromJson<OllamaResponse>(fullJson).message.content;
            if (!string.IsNullOrEmpty(content)) content = content.Replace("**", "");
            return content;
        }
        catch { return fullJson; }
    }

    [System.Serializable]
    public class OllamaResponse
    {
        public ResponseMessage message;
        public int prompt_eval_count;
        public int eval_count;
    }
    [System.Serializable] public class ResponseMessage { public string content; }
    [System.Serializable] public class OllamaOptions { public int num_predict; public float temperature; public int num_ctx; }
}