 using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;

// ▼▼▼ 削除: コマンド用のデータ構造 (OllamaFormatSchema, AICommandResponse など) ▼▼▼
// [System.Serializable]
// public class OllamaFormatSchema { ... }
// ... (関連クラスすべて) ...

// --- リクエスト/レスポンス (共通) ---
[System.Serializable]
public class OllamaRequest
{
    public string model;
    public bool stream;
    public Message[] messages;
}

// ▼▼▼ 削除: コマンドスキーマ用のリクエスト (OllamaSchemaRequest) ▼▼▼
// [System.Serializable]
// public class OllamaSchemaRequest { ... }

[System.Serializable]
public class Message { public string role; public string content; public string[] images; }
[System.Serializable]
public class OllamaResponse { public ResponseMessage message; }
[System.Serializable]
public class ResponseMessage { public string role; public string content; }

// ▼▼▼ 削除: コマンド用のレスポンス (AICommandResponse) ▼▼▼
// [System.Serializable]
// public class AICommandResponse { ... }

// --- 写真解析用のJSONスキーマ定義 (変更なし) ---

[System.Serializable]
public class OllamaPhotoSchemaRequest
{
    public string model;
    public bool stream;
    public Message[] messages;
    public PhotoFormatSchema format; 
}

[System.Serializable]
public class PhotoFormatSchema
{
    public string type = "object";
    public PhotoFormatProperties properties;
    public string[] required;
}

[System.Serializable]
public class PhotoFormatProperties
{
    public SchemaPropertyBase danger_detected;
    public SchemaPropertyEnum danger_type;
    public SchemaPropertyArray detected_objects;
}

[System.Serializable]
public class SchemaPropertyBase
{
    public string type;
    public string description;
}

[System.Serializable]
public class SchemaPropertyEnum : SchemaPropertyBase
{
    public string[] @enum;
}

[System.Serializable]
public class SchemaPropertyArray : SchemaPropertyBase
{
    public SchemaPropertyBase items;
}

// --- 写真解析のJSON応答をパースするためのクラス (変更なし) ---
[System.Serializable]
public class AIPhotoResponse
{
    public bool danger_detected;
    public string danger_type;
    public string[] detected_objects;
}


// --- メインのスクリプト ---
public class VLMCarController : MonoBehaviour
{
    // (中略: 車の物理制御、UI変数、Ollama設定などの変数は変更なし)
    [Header("Car Physics Settings")]
    public List<AxleInfo> axleInfos;
    public float maxMotorTorque;
    public float maxBrakeTorque;
    public float maxSteeringAngle;

    [Header("VLM and UI Settings")]
    public Canvas canvas;
    public Camera carCamera;
    
    [Header("Ollama Settings")]
    public string ollamaUrl = "http://localhost:11434/api/chat";
    public string modelName = "qwen2.5vl:8b";

    [Header("Image Save Settings")]
    public string saveFolderName = "Images";

    // ▼▼▼ 削除: inputField, sendButton, isInputFieldFocused ▼▼▼
    // private TMP_InputField inputField;
    // private Button sendButton;    
    private Button photoButton; // photoButtonのみ残す
    [SerializeField] private TMP_Text responseText;
    // private bool isInputFieldFocused = false;
    private bool isProcessing = false;
    
    // ▼▼▼ 削除: コマンド用スキーマ (_commandSchema) ▼▼▼
    // private OllamaFormatSchema _commandSchema;
    private PhotoFormatSchema _photoSchema; // 写真解析用のみ残す

    // ▼▼▼ 削除: AI操縦用の状態 (AiState, currentAiState) ▼▼▼
    // private enum AiState { ... }
    // private AiState currentAiState = AiState.Idle;

    
    void Start()
    {
        if (canvas != null)
        {
            // ▼▼▼ 削除: inputField, sendButton の取得ロジック ▼▼▼
            // inputField = canvas.GetComponentInChildren<TMP_InputField>();
            // Transform sendButtonTransform = canvas.transform.Find("sendButton");
            // if (sendButtonTransform != null) sendButton = sendButtonTransform.GetComponent<Button>();

            Transform photoButtonTransform = canvas.transform.Find("photoButton");
            if (photoButtonTransform != null) photoButton = photoButtonTransform.GetComponent<Button>();
        }
        if (carCamera == null) { Debug.LogError("Target Camera が設定されていません"); return; }
        if (photoButton == null) { Debug.LogError("photoButton (Button) が見つかりません"); return; }
        
        // ▼▼▼ 削除: inputField, sendButton のエラーチェックとリスナー設定 ▼▼▼
        // if (inputField == null) { ... }
        // if (sendButton == null) { ... }
        // inputField.onSelect.AddListener(...);
        // inputField.onDeselect.AddListener(...);
        // inputField.onSubmit.AddListener(...);
        // sendButton.onClick.AddListener(OnSend);

        photoButton.onClick.AddListener(OnPhoto); // OnPhotoのリスナーのみ残す
        
        // ▼▼▼ 削除: 車両制御用のJSONスキーマ (_commandSchema) の定義 ▼▼▼
        // _commandSchema = new OllamaFormatSchema { ... };

        // 写真解析用のJSONスキーマを定義 (変更なし)
        _photoSchema = new PhotoFormatSchema
        {
            type = "object",
            properties = new PhotoFormatProperties
            {
                danger_detected = new SchemaPropertyBase
                {
                    type = "boolean",
                    description = "Indicates if an immediate danger (e.g., pedestrian, obstacle) is detected."
                },
                danger_type = new SchemaPropertyEnum
                {
                    type = "string",
                    description = "Specifies the type of danger if one is detected.",
                    @enum = new string[] { "none", "obstacle", "pedestrian", "vehicle" }
                },
                detected_objects = new SchemaPropertyArray
                {
                    type = "array",
                    description = "A list of noteworthy objects recognized in the scene.",
                    items = new SchemaPropertyBase { type = "string" }
                }
            },
            required = new string[] { "danger_detected", "danger_type", "detected_objects" }
        };

        // ▼▼▼ 修正: currentAiStateに関するログを削除 ▼▼▼
        Debug.Log("VLM Car Controller Initialized.");
    }
    
    
    void FixedUpdate()
    {
        float manualMotor = Input.GetAxis("Vertical");
        float manualSteering = Input.GetAxis("Horizontal");
        bool manualBrake = Input.GetKey(KeyCode.Space);

        // ▼▼▼ 修正: isInputFieldFocused のチェックと else (AI操縦) ブロックを削除 ▼▼▼
        // 常に手動入力を車に適用
        Move(manualMotor, manualSteering, manualBrake);
        
        foreach (AxleInfo axleInfo in axleInfos)
        {
            ApplyLocalPositionToVisuals(axleInfo.leftWheel);
            ApplyLocalPositionToVisuals(axleInfo.rightWheel);
        }
    }

    // ========== UIイベントハンドラ ==========

    /// <summary>
    /// Photoボタンが押された時の処理 (変更なし)
    /// </summary>
    private void OnPhoto()
    {
        // OnPhoto メソッド内のプロンプトをこう変えます
        string fixedQuestion = "Analyze this third-person view. If you detect an object that is close enough to be considered a risk, set 'danger_detected' to true and 'danger_type' to 'obstacle'. If there are no immediate risks, set 'danger_detected' to false. Also, list all noteworthy objects in 'detected_objects'. Provide a structural JSON report.";        // ▼▼▼ 修正: 呼び出し先を新しい SendRequestToOllama のシグネチャに合わせる ▼▼▼
        StartCoroutine(SendRequestToOllama(fixedQuestion));
    }

    // ▼▼▼ 削除: OnSend メソッド ▼▼▼
    // private void OnSend() { ... }

    // ========== VLMへのリクエスト送信 (共通コルーチン) ==========
    
    /// <summary>
    /// ▼▼▼ 修正: シグネチャ (引数) を簡略化 ▼▼▼
    /// </summary>
    private IEnumerator SendRequestToOllama(string prompt)
    {
        if (isProcessing) yield break;
        isProcessing = true;
        SetUIInteractable(false);
        if (responseText != null) responseText.text = "AI is processing your request...";

        string base64Image = null;

        // ▼▼▼ 修正: includeImage=true を前提とし、画像キャプチャを常に行う ▼▼▼
        if (canvas != null) canvas.enabled = false;
        yield return null;
        Texture2D photo = CaptureCameraView(carCamera);
        if (canvas != null) canvas.enabled = true;
        byte[] bytes = photo.EncodeToJPG();
        SaveImageToFile(bytes);
        base64Image = System.Convert.ToBase64String(bytes);
        Destroy(photo);
        
        var message = new Message
        {
            role = "user",
            content = prompt,
            images = new string[] { base64Image } // 常に画像を含む
        };

        string jsonBody = "";
        
        // ▼▼▼ 修正: コマンドスキーマ(OnSend)のロジックを削除 ▼▼▼
        // 常に写真解析スキーマ (_photoSchema) を使用
        OllamaPhotoSchemaRequest requestData = new OllamaPhotoSchemaRequest
        {
            model = modelName,
            stream = false,
            messages = new Message[] { message },
            format = _photoSchema // 常に _photoSchema を使用
        };
        jsonBody = JsonUtility.ToJson(requestData);
        Debug.Log("Sending JSON (Photo Schema Mode): " + jsonBody);
        
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
                if (responseText != null) responseText.text = "エラー: " + request.error;
                // ▼▼▼ 削除: HandleLLMResponse("Stop") の呼び出し ▼▼▼
            }
            else
            {
                OllamaResponse response = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
                string responseMessage = response.message.content;
                Debug.Log("Raw Response: " + responseMessage);

                // ▼▼▼ 修正: コマンドスキーマ(OnSend)のロジックを削除 ▼▼▼
                // OnPhoto (JSON解析モード) の場合の処理
                try
                {
                    AIPhotoResponse photoResponse = JsonUtility.FromJson<AIPhotoResponse>(responseMessage);

                    // 応答を整形してUIに表示
                    string formattedResponse = $"<b>Danger:</b> {photoResponse.danger_detected}\n" +
                                             $"<b>Type:</b> {photoResponse.danger_type}\n" +
                                             $"<b>Objects:</b> {string.Join(", ", photoResponse.detected_objects)}";

                    Debug.Log("VLM photo analysis: " + formattedResponse);
                    if (responseText != null) responseText.text = formattedResponse;
                    // 写真解析なので車は動かさない
                }
                // try
                // {
                    
                //     // OnPhoto (JSON解析モード) の場合の処理
                //     string rawJsonResponse = responseMessage;
                    
                //     Debug.Log("VLM photo analysis (Raw JSON): " + rawJsonResponse);
                //     if (responseText != null) responseText.text = rawJsonResponse;

                //     // JSONとして有効かどうかのパース試行
                //     JsonUtility.FromJson<AIPhotoResponse>(responseMessage);
                // }
                catch (System.Exception e)
                {
                    Debug.LogError("AIのJSON応答の解析に失敗: " + e.Message + " | 応答: " + responseMessage);
                    if (responseText != null) responseText.text = "AI Response (Invalid JSON): " + responseMessage;
                }
            }
        }
        SetUIInteractable(true);
        isProcessing = false;
    }

    // ▼▼▼ 削除: HandleLLMResponse メソッド ▼▼▼
    // private void HandleLLMResponse(string command) { ... }
    
    // ▼▼▼ 削除: ExecuteAiState メソッド ▼▼▼
    // private void ExecuteAiState() { ... }

// ========== ヘルパー関数 ==========

    /// <summary>
    /// UI要素の有効/無効をまとめて切り替える
    /// </summary>
    private void SetUIInteractable(bool interactable)
    {
        photoButton.interactable = interactable;
        
        // ▼▼▼ 削除: sendButton, inputField 関連の処理 ▼▼▼
        // sendButton.interactable = interactable;
        // inputField.interactable = interactable;
        // if (interactable) { ... }
    }

    // (SaveImageToFile, CaptureCameraView, Move, ApplyLocalPositionToVisuals は変更なし)
    
    private void SaveImageToFile(byte[] bytes)
    {
        #if UNITY_EDITOR
        string folderPath = Path.Combine(Application.dataPath, saveFolderName); 
        
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
        string fileName = $"capture_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.jpg";
        string filePath = Path.Combine(folderPath, fileName);
        
        File.WriteAllBytes(filePath, bytes);
        
        UnityEditor.AssetDatabase.Refresh(); 
        Debug.Log($"画像を {filePath} に保存しました");
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
    public void Move(float motorInput, float steeringInput, bool isBraking)
    {
        float motor = maxMotorTorque * motorInput;
        float steering = maxSteeringAngle * steeringInput;
        float brakeTorque = isBraking ? maxBrakeTorque : 0f;

        foreach (AxleInfo axleInfo in axleInfos)
        {
            if (axleInfo.steering)
            {
                axleInfo.leftWheel.steerAngle = steering;
                axleInfo.rightWheel.steerAngle = steering;
            }
            if (axleInfo.motor)
            {
                axleInfo.leftWheel.motorTorque = isBraking ? 0f : motor;
                axleInfo.rightWheel.motorTorque = isBraking ? 0f : motor;
            }
            axleInfo.leftWheel.brakeTorque = brakeTorque;
            axleInfo.rightWheel.brakeTorque = brakeTorque;
        }
    }
    public void ApplyLocalPositionToVisuals(WheelCollider collider)
    {
        if (collider.transform.childCount == 0) return;
        
        Transform visualWheel = collider.transform.GetChild(0);
        
        collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
        
        visualWheel.transform.position = position;
        visualWheel.transform.rotation = rotation * Quaternion.Euler(0f, 0f, 90f); 
    }

} // <- VLMCarControllerクラス

// (AxleInfoクラスは変更なし)
[System.Serializable]
public class AxleInfo
{
    public WheelCollider leftWheel;
    public WheelCollider rightWheel;
    public bool motor;
    public bool steering;
}