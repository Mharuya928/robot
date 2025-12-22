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
    // AI通信のメイン処理 (Multi-Message対応版)
    // =================================================================
    private IEnumerator SendRequestToOllama()
    {
        if (isProcessing) yield break;
        isProcessing = true;
        if (VLMText != null) VLMText.text = "VLM: Processing...";

        // モジュールログ
        StringBuilder moduleLog = new StringBuilder();
        if (config.activeModules != null) foreach (var m in config.activeModules) if (m) moduleLog.AppendLine(m.moduleName);
        Debug.Log($"Active Modules:\n{moduleLog}");

        if (carController != null) carController.SetRaycastLineVisibility(false);
        yield return null;

        // 1. 画像の撮影と収集
        List<Texture2D> capturedTextures = new List<Texture2D>();

        // 順番記録用ログ
        StringBuilder orderLog = new StringBuilder();
        orderLog.AppendLine("【Image Order Check】(AIへの送信順)");

        // カメラ撮影（順番は変えないこと！）
        switch (config.viewMode)
        {
            case VLMConfig.ViewMode.FPS:
                if (frontCamera) { capturedTextures.Add(CaptureCameraView(frontCamera)); orderLog.AppendLine("[0] Front (前方)"); }
                break;
            case VLMConfig.ViewMode.MultiView:
                if (frontCamera) { capturedTextures.Add(CaptureCameraView(frontCamera)); orderLog.AppendLine("[0] Front (前方)"); }
                if (topCamera) { capturedTextures.Add(CaptureCameraView(topCamera)); orderLog.AppendLine("[1] Top (俯瞰)"); }
                break;
            case VLMConfig.ViewMode.SurroundView:
                if (frontCamera)
                {
                    Texture2D t = CaptureCameraView(frontCamera);
                    AddColorIndicator(t, FRONT); // 前方 = 赤
                    capturedTextures.Add(t);
                    orderLog.AppendLine("[0] Front (Red/赤)");
                }
                if (backCamera)
                {
                    Texture2D t = CaptureCameraView(backCamera);
                    AddColorIndicator(t, BACK); // 後方 = 青
                    capturedTextures.Add(t);
                    orderLog.AppendLine("[1] Back (Blue/青)");
                }
                if (leftCamera)
                {
                    Texture2D t = CaptureCameraView(leftCamera);
                    AddColorIndicator(t, LEFT); // 左 = 緑
                    capturedTextures.Add(t);
                    orderLog.AppendLine("[2] Left (Green/緑)");
                }
                if (rightCamera)
                {
                    Texture2D t = CaptureCameraView(rightCamera);
                    AddColorIndicator(t, RIGHT); // 右 = 黄
                    capturedTextures.Add(t);
                    orderLog.AppendLine("[3] Right (Yellow/黄)");
                }
                break;
        }
        Debug.Log(orderLog.ToString());

        if (capturedTextures.Count == 0)
        {
            Debug.LogError("撮影失敗");
            isProcessing = false;
            yield break;
        }

        if (carController != null) carController.SetRaycastLineVisibility(true);

        // 2. Base64変換
        List<string> base64Images = new List<string>();
        for (int i = 0; i < capturedTextures.Count; i++)
        {
            byte[] bytes = capturedTextures[i].EncodeToJPG();
            SaveImageToFile(bytes, i);
            base64Images.Add(System.Convert.ToBase64String(bytes));
            Destroy(capturedTextures[i]);
        }

        // =========================================================
        // 3. JSONメッセージの構築 (ここが最大の変更点)
        // =========================================================

        StringBuilder messagesJson = new StringBuilder();
        messagesJson.Append("["); // 配列開始

        // (A) 画像メッセージを順番に追加
        for (int i = 0; i < base64Images.Count; i++)
        {
            // インデックスに応じた「意味（ラベル）」を取得
            string label = GetViewLabel(config.viewMode, i);
            string imageBase64 = base64Images[i];

            // 1メッセージを作る { "role": "user", "content": "ラベル", "images": ["Base64"] }
            // 画像データ部分だけ巨大なので、ここは文字列操作で慎重に結合
            messagesJson.Append($@"{{ ""role"": ""user"", ""content"": ""{label}"", ""images"": [""{imageBase64}""] }},");
        }

        // (B) 最後にプロンプト（指示）だけのメッセージを追加
        // プロンプト内の改行やダブルクォートをエスケープ
        string safePrompt = config.CurrentPrompt.Replace("\"", "\\\"").Replace("\n", "\\n");

        // 最後の指示メッセージ（画像なし）
        messagesJson.Append($@"{{ ""role"": ""user"", ""content"": ""{safePrompt}"" }}");

        messagesJson.Append("]"); // 配列終了

        // =========================================================
        // 4. リクエストボディの作成
        // =========================================================

        OllamaOptions options = new OllamaOptions { num_predict = config.maxTokens, temperature = config.temperature, num_ctx = config.contextSize };
        string optionsJson = JsonUtility.ToJson(options);

        string jsonBody = "";
        bool isFreeForm = (config.activeModules == null || config.activeModules.Count == 0);

        if (isFreeForm)
        {
            // Formatなし
            jsonBody = $@"{{ ""model"": ""{config.ModelName}"", ""stream"": false, ""options"": {optionsJson}, ""messages"": {messagesJson.ToString()} }}";
        }
        else
        {
            // Format (GBNF) あり
            string schemaJson = BuildDynamicSchemaJson(config.activeModules);
            jsonBody = $@"{{ ""model"": ""{config.ModelName}"", ""stream"": false, ""options"": {optionsJson}, ""messages"": {messagesJson.ToString()}, ""format"": {schemaJson} }}";
        }

        // ログ出力（画像データは省略して表示）
        // ログ出力（画像データをカメラ名に置き換えて表示）
        if (!string.IsNullOrEmpty(jsonBody))
        {
            string debugJson = jsonBody;

            // リストのインデックスを使って、各画像を対応するカメラ名のタグに置換する
            for (int i = 0; i < base64Images.Count; i++)
            {
                string camLabel = "Unknown";

                // 現在のViewModeとインデックス(i)からカメラ名を判定
                if (config.viewMode == VLMConfig.ViewMode.SurroundView)
                {
                    switch (i)
                    {
                        case 0: camLabel = "Front(前方)"; break;
                        case 1: camLabel = "Back(後方)"; break;
                        case 2: camLabel = "Left(左側)"; break;
                        case 3: camLabel = "Right(右側)"; break;
                    }
                }
                else if (config.viewMode == VLMConfig.ViewMode.MultiView)
                {
                    switch (i)
                    {
                        case 0: camLabel = "Front(前方)"; break;
                        case 1: camLabel = "Top(俯瞰)"; break;
                    }
                }
                else // FPS
                {
                    camLabel = "Front(前方)";
                }

                // Base64文字列を、分かりやすいタグに置換
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

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + request.error);
                if (VLMText) VLMText.text = "Error: " + request.error;
            }
            else
            {
                string rawJson = request.downloadHandler.text;

                // トークンチェック
                OllamaResponse responseData = JsonUtility.FromJson<OllamaResponse>(rawJson);
                int used = responseData.prompt_eval_count;
                int limit = config.contextSize;
                string tokenLog = $"【Token Usage】 Used: {used} / Limit: {limit}";
                if (used >= limit) Debug.LogError($"{tokenLog} ⚠️不足!"); else Debug.Log($"{tokenLog} ✅OK");

                string contentJson = ExtractContent(rawJson);
                Debug.Log("AI Response: " + contentJson);
                if (isFreeForm && VLMText) VLMText.text = contentJson;
                else DisplayDynamicResult(contentJson);
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

    // 再帰的にスキーマとJSONを照らし合わせて表示を作る（インデックス削除版）
    private void FormatSchemaRecursive(StringBuilder sb, VLMSchemaModule module, string jsonContext, int indentLevel)
    {
        string indent = new string(' ', indentLevel * 2);

        foreach (var prop in module.properties)
        {
            string rawValue = ExtractJsonValue(jsonContext, prop.name);

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
                    // ▼▼▼ 変更: [0]: を削除し、区切り線に変更 ▼▼▼

                    // 2つ目以降のアイテムの場合、区切り線を入れる
                    if (i > 0)
                    {
                        sb.AppendLine($"{indent}  ---");
                    }

                    if (prop.schemaReference != null)
                    {
                        // インデントを +2 から +1 に減らしてスッキリさせる
                        FormatSchemaRecursive(sb, prop.schemaReference, items[i], indentLevel + 1);
                    }
                    else
                    {
                        sb.AppendLine($"{indent}  {FormatValueColor(items[i])}");
                    }
                    // ▲▲▲ 変更ここまで ▲▲▲
                }
            }
            else
            {
                string displayVal = FormatValueColor(rawValue);
                sb.AppendLine($"{indent}- {prop.name}: {displayVal}");
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