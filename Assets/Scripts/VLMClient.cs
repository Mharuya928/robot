using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System.Text;

public class VLMClient : MonoBehaviour
{
    [Header("Config")]
    [Tooltip("ここに作成した設定ファイル(VLMConfig)をセットする")]
    public VLMConfig config; 

    [Header("Dependencies")]
    [Tooltip("撮影時に線を消すために制御するCarController")]
    public CarController carController; 
    public Camera carCamera;
    public Canvas canvas;
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
        // 必須コンポーネントのチェック
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
        if(Input.GetKeyDown(vlmActivationKey) && !isProcessing && config != null)
        {
            OnPhoto();
        }
    }

    // ========== UIイベントハンドラ ==========

    private void OnPhoto()
    {
        // 設定ファイル(config)内のプロンプトを使用してリクエスト開始
        StartCoroutine(SendRequestToOllama(config.prompt));
    }
    
    // ========== VLMへのリクエスト送信 ==========
    
    private IEnumerator SendRequestToOllama(string prompt)
    {
        if (isProcessing) yield break;
        isProcessing = true;
        
        if (VLMText != null) VLMText.text = "VLM: Processing...";

        // --- 1. 画像撮影シーケンス ---
        string base64Image = null;

        // レイキャストの線（LineRenderer）を一時的に消す
        if (carController != null) carController.SetRaycastLineVisibility(false);

        yield return null; // 1フレーム待機して描画更新を待つ

        // 撮影
        Texture2D photo = CaptureCameraView(carCamera);

        // 線を戻す
        if (carController != null) carController.SetRaycastLineVisibility(true);

        // 画像の保存とエンコード
        byte[] bytes = photo.EncodeToJPG();
        SaveImageToFile(bytes);
        base64Image = System.Convert.ToBase64String(bytes);
        Destroy(photo);
        // ---------------------------

        // メッセージの作成
        var message = new Message
        {
            role = "user",
            content = prompt,
            images = new string[] { base64Image } 
        };

        string jsonBody = "";

        // --- 2. 設定(Config)に応じたリクエスト作成 ---
        
        if (config.schemaType == VLMConfig.SchemaType.FreeForm)
        {
            // パターンA: スキーマなし (通常のチャット形式)
            OllamaRequest requestData = new OllamaRequest
            {
                model = config.modelName,
                stream = false,
                messages = new Message[] { message }
            };
            jsonBody = JsonUtility.ToJson(requestData);
        }
        else
        {
            // パターンB: JSONスキーマあり (構造化出力)
            // 現在は "ObjectDetection" 用のスキーマを生成
            PhotoFormatSchema schemaFormat = null;

            if (config.schemaType == VLMConfig.SchemaType.ObjectDetection)
            {
                schemaFormat = new PhotoFormatSchema
                {
                    type = "object",
                    properties = new PhotoFormatProperties { 
                        detected_objects = new SchemaPropertyArray { 
                            type = "array", 
                            items = new SchemaPropertyBase { type = "string" } 
                        } 
                    },
                    required = new string[] { "detected_objects" }
                };
            }
            // ※ 必要に応じてここに他のスキーマタイプの定義を追加可能

            OllamaPhotoSchemaRequest requestData = new OllamaPhotoSchemaRequest
            {
                model = config.modelName,
                stream = false,
                messages = new Message[] { message },
                format = schemaFormat
            };
            jsonBody = JsonUtility.ToJson(requestData);
        }
        
        Debug.Log($"Sending JSON ({config.schemaType}): " + jsonBody);
        
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
                Debug.LogError("Error: " + request.error);
                if (VLMText != null) VLMText.text = "VLM Error: " + request.error;
            }
            else
            {
                string responseMessage = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text).message.content;
                Debug.Log("Raw Response: " + responseMessage);

                // --- 4. 応答のパース ---
                try
                {
                    if (config.schemaType == VLMConfig.SchemaType.ObjectDetection)
                    {
                        // JSONとしてパースして表示
                        AIPhotoResponse photoResponse = JsonUtility.FromJson<AIPhotoResponse>(responseMessage);
                        string formatted = $"Objects: {string.Join(", ", photoResponse.detected_objects)}";
                        if (VLMText != null) VLMText.text = formatted;
                    }
                    else
                    {
                        // そのまま表示 (FreeForm)
                        if (VLMText != null) VLMText.text = responseMessage;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("JSON Parse Error: " + e.Message);
                    // パース失敗時は生の応答を表示しておく
                    if (VLMText != null) VLMText.text = "VLM (Raw): " + responseMessage;
                }
            }
        }
        
        isProcessing = false;
    }

    // ========== ヘルパー関数 ==========

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

    // --- データ定義クラス (JSON用) ---
    [System.Serializable] public class OllamaRequest { public string model; public bool stream; public Message[] messages; }
    [System.Serializable] public class Message { public string role; public string content; public string[] images; }
    [System.Serializable] public class OllamaResponse { public ResponseMessage message; }
    [System.Serializable] public class ResponseMessage { public string role; public string content; }
    
    // スキーマ付きリクエスト用
    [System.Serializable] public class OllamaPhotoSchemaRequest { public string model; public bool stream; public Message[] messages; public PhotoFormatSchema format; }
    [System.Serializable] public class PhotoFormatSchema { public string type = "object"; public PhotoFormatProperties properties; public string[] required; }
    [System.Serializable] public class PhotoFormatProperties { public SchemaPropertyArray detected_objects; }
    [System.Serializable] public class SchemaPropertyBase { public string type; public string description; }
    [System.Serializable] public class SchemaPropertyArray : SchemaPropertyBase { public SchemaPropertyBase items; }
    
    // 応答パース用
    [System.Serializable] public class AIPhotoResponse { public string[] detected_objects; }
}