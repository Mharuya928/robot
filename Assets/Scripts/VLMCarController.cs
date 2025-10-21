using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;

// --- データ構造の定義 (変更なし) ---
[System.Serializable]
public class OllamaFormatSchema
{
    public string type = "object";
    public OllamaFormatProperties properties;
    public string[] required;
}

[System.Serializable]
public class OllamaFormatProperties
{
    // ここで "command" というキー名を定義
    public OllamaFormatCommand command;
}

[System.Serializable]
public class OllamaFormatCommand
{
    public string type = "string";
    // @enum は 'enum' がC#の予約語であるため @ を付けています
    public string[] @enum; 
}

// ▼▼▼ 修正: 平文用のリクエスト (formatフィールドを持たない) ▼▼▼
[System.Serializable]
public class OllamaRequest
{
    public string model;
    public bool stream;
    public Message[] messages;
}

// ▼▼▼ 修正: JSONスキーマ用のリクエスト (formatフィールドを持つ) ▼▼▼
[System.Serializable]
public class OllamaSchemaRequest
{
    public string model;
    public bool stream;
    public Message[] messages;
    public OllamaFormatSchema format; // スキーマを持つ
}

[System.Serializable]
public class Message { public string role; public string content; public string[] images; }
[System.Serializable]
public class OllamaResponse { public ResponseMessage message; }
[System.Serializable]
public class ResponseMessage { public string role; public string content; }
[System.Serializable]
public class AICommandResponse
{
    public string command;
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
    public string modelName = "qwen2.5vl:3b";

    [Header("Image Save Settings")]
    public string saveFolderName = "Images";

    private TMP_InputField inputField;
    private Button photoButton;
    private Button sendButton;    
    private TMP_Text responseText;
    private bool isInputFieldFocused = false;
    private bool isProcessing = false;
    private OllamaFormatSchema _commandSchema;

    private enum AiState
    {
        Idle,
        Forward,
        Backward,
        TurnRight,
        TurnLeft,
        Braking
    }
    private AiState currentAiState = AiState.Idle;

    // (中略: Startメソッドは変更なし)
    void Start()
    {
        if (canvas != null)
        {
            inputField = canvas.GetComponentInChildren<TMP_InputField>();
            Transform responseTextTransform = canvas.transform.Find("responseText");
            if (responseTextTransform != null) responseText = responseTextTransform.GetComponent<TMP_Text>();
            Transform photoButtonTransform = canvas.transform.Find("photoButton");
            if (photoButtonTransform != null) photoButton = photoButtonTransform.GetComponent<Button>();
            Transform sendButtonTransform = canvas.transform.Find("sendButton");
            if (sendButtonTransform != null) sendButton = sendButtonTransform.GetComponent<Button>();
        }
        if (carCamera == null) { Debug.LogError("Target Camera が設定されていません"); return; }
        if (inputField == null) { Debug.LogError("InputField (TMP_InputField) が見つかりません"); return; }
        if (photoButton == null) { Debug.LogError("photoButton (Button) が見つかりません"); return; }
        if (sendButton == null) { Debug.LogError("sendButton (Button) が見つかりません"); return; }

        inputField.onSelect.AddListener((_) => isInputFieldFocused = true);
        inputField.onDeselect.AddListener((_) => isInputFieldFocused = false);

        inputField.onSubmit.AddListener((_) => OnSend()); // EnterキーでOnSendを呼ぶ
        sendButton.onClick.AddListener(OnSend);
        photoButton.onClick.AddListener(OnPhoto);
        // ▼▼▼ 追加: 車両制御用のJSONスキーマをここで定義 ▼▼▼
        _commandSchema = new OllamaFormatSchema
        {
            type = "object",
            properties = new OllamaFormatProperties
            {
                // "command" というキーを定義
                command = new OllamaFormatCommand
                {
                    type = "string",
                    // 応答をこの5つの単語のいずれかに強制する
                    @enum = new string[] { "Forward", "Backward", "TurnLeft", "TurnRight", "Stop" }
                }
            },
            // "command" キーは必須であると指定
            required = new string[] { "command" }
        };
        Debug.Log("VLM Car Controller Initialized. AI State: " + currentAiState);
    }
    
    // (中略: FixedUpdateメソッドは変更なし)
    void FixedUpdate()
    {
        float manualMotor = Input.GetAxis("Vertical");
        float manualSteering = Input.GetAxis("Horizontal");
        bool manualBrake = Input.GetKey(KeyCode.Space);
        if (!isInputFieldFocused && (manualMotor != 0 || manualSteering != 0 || manualBrake))
        {
            currentAiState = AiState.Idle;
            Move(manualMotor, manualSteering, Input.GetKey(KeyCode.Space));
        }
        else
        {
            ExecuteAiState();
        }
        foreach (AxleInfo axleInfo in axleInfos)
        {
            ApplyLocalPositionToVisuals(axleInfo.leftWheel);
            ApplyLocalPositionToVisuals(axleInfo.rightWheel);
        }
    }

    // ========== UIイベントハンドラ ==========

    /// <summary>
    /// Photoボタンが押された時の処理
    /// </summary>
    private void OnPhoto()
    {
        string fixedQuestion = "please explain this photo concisely";
        // ▼▼▼ 修正: includeImageフラグを 'true' にして呼び出す ▼▼▼
        StartCoroutine(SendRequestToOllama(fixedQuestion, true, null));
    }

    /// <summary>
    /// Sendボタンが押された時の処理
    /// </summary>
    private void OnSend()
    {
        string message = inputField.text;
        if (string.IsNullOrWhiteSpace(message)) return;
        
        // ▼▼▼ 修正: プロンプトからJSONの指示を削除 ▼▼▼
        string prompt = @$"ユーザーの要求: ""{message}""";
        
        // ▼▼▼ 修正: 3番目の引数に定義済みスキーマ(_commandSchema)を渡す ▼▼▼
        StartCoroutine(SendRequestToOllama(prompt, false, _commandSchema));
    }

    // ========== VLMへのリクエスト送信 (共通コルーチン) ==========
    
    /// <summary>
    /// ▼▼▼ 修正: 引数に bool includeImage を追加 ▼▼▼
    /// </summary>
    private IEnumerator SendRequestToOllama(string prompt, bool includeImage, OllamaFormatSchema schema)
    {
        if (isProcessing) yield break;
        isProcessing = true;
        SetUIInteractable(false);
        if (responseText != null) responseText.text = "AI is processing your request...";

        string base64Image = null;

        // includeImageがtrueの場合のみ、画像処理を実行
        if (includeImage)
        {
            if (canvas != null) canvas.enabled = false;
            yield return null;
            Texture2D photo = CaptureCameraView(carCamera);
            if (canvas != null) canvas.enabled = true;
            byte[] bytes = photo.EncodeToJPG();
            SaveImageToFile(bytes);
            base64Image = System.Convert.ToBase64String(bytes);
            Destroy(photo);
        }

        // メッセージ本体を作成
        var message = new Message
        {
            role = "user",
            content = prompt,
            images = includeImage ? new string[] { base64Image } : null
        };

        string jsonBody = "";
        
        // ▼▼▼ 修正: schema の有無でシリアライズするC#クラス自体を変更 ▼▼▼
        if (schema != null)
        {
            // OnSend (JSONスキーマモード)
            // "format" フィールドを持つ OllamaSchemaRequest を使う
            OllamaSchemaRequest requestData = new OllamaSchemaRequest
            {
                model = modelName,
                stream = false,
                messages = new Message[] { message },
                format = schema
            };
            jsonBody = JsonUtility.ToJson(requestData);
            Debug.Log("Sending JSON (Schema Mode): " + jsonBody);
        }
        else
        {
            // OnPhoto (平文モード)
            // "format" フィールドを持たない OllamaRequest を使う
            OllamaRequest requestData = new OllamaRequest
            {
                model = modelName,
                stream = false,
                messages = new Message[] { message }
            };
            jsonBody = JsonUtility.ToJson(requestData);
            Debug.Log("Sending JSON (Plain Mode): " + jsonBody);
        }

        // 5. UnityWebRequestでOllamaにPOSTリクエストを送信
        using (UnityWebRequest request = new UnityWebRequest(ollamaUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            // 6. 結果を処理
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error: " + request.error);
                if (responseText != null) responseText.text = "エラー: " + request.error;

                // ▼▼▼ 修正: OnSendの場合のみエラー時に停止処理を呼ぶ ▼▼▼
                if(schema != null) HandleLLMResponse("Stop");
            }
            else
            {
                OllamaResponse response = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
                string responseMessage = response.message.content; 
                Debug.Log("Raw Response: " + responseMessage);

                // ▼▼▼ 修正: スキーマを使ったかどうか(schema != null)で処理を分岐 ▼▼▼
                if (schema != null)
                {
                    // OnSend (JSONモード) の場合の処理
                    try
                    {
                        AICommandResponse commandResponse = JsonUtility.FromJson<AICommandResponse>(responseMessage);
                        string command = commandResponse.command;
                        
                        Debug.Log("VLM suggested command: " + command);
                        if (responseText != null) responseText.text = "AI suggested command: " + command;
                        HandleLLMResponse(command); // 車を動かす
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError("AIのJSON応答の解析に失敗: " + e.Message + " | 応答: " + responseMessage);
                        if (responseText != null) responseText.text = "error: " + e.Message;
                        HandleLLMResponse("Stop"); // エラー時は停止
                    }
                }
                else
                {
                    // OnPhoto (平文モード) の場合の処理
                    if (responseText != null) responseText.text = responseMessage;
                    // 景色の説明なので車は動かさない
                }
            }
        }
        SetUIInteractable(true);
        isProcessing = false;
    }

private void HandleLLMResponse(string command)
    {
        // ▼▼▼ 修正: .Contains ではなく == で厳密に比較 ▼▼▼
        if (command == "Forward") currentAiState = AiState.Forward;
        else if (command == "Backward") currentAiState = AiState.Backward;
        else if (command == "TurnRight") currentAiState = AiState.TurnRight;
        else if (command == "TurnLeft") currentAiState = AiState.TurnLeft;
        else if (command == "Stop") currentAiState = AiState.Braking;
        else
        {
            Debug.LogWarning("不明な命令を受信: " + command + "。停止します。");
            currentAiState = AiState.Braking;
        }
        Debug.Log("New AI State: " + currentAiState);
    }
    private void ExecuteAiState()
    {
        float motor = 0f, steering = 0f;
        switch (currentAiState)
        {
            case AiState.Forward:   motor = 1.0f; steering = 0f;   break;
            case AiState.Backward:  motor = -1.0f;steering = 0f;   break;
            case AiState.TurnRight: motor = 0.8f; steering = 1.0f; break;
            case AiState.TurnLeft:  motor = 0.8f; steering = -1.0f;break;
            case AiState.Braking:
            case AiState.Idle:      motor = 0f;   steering = 0f;   break;
        }
        Move(motor, steering, (currentAiState == AiState.Braking || currentAiState == AiState.Idle));
    }

// ========== ヘルパー関数 ==========

    /// <summary>
    /// UI要素の有効/無効をまとめて切り替える
    /// </summary>
    private void SetUIInteractable(bool interactable)
    {
        photoButton.interactable = interactable;
        sendButton.interactable = interactable;
        inputField.interactable = interactable;

        if (interactable)
        {
            inputField.text = ""; // 処理完了後、InputFieldをクリア
            // InputFieldに再度フォーカスを当てる
            EventSystem.current.SetSelectedGameObject(inputField.gameObject); 
        }
    }

    /// <summary>
    /// 画像をファイルに保存する（Unityエディタ専用）
    /// </summary>
    private void SaveImageToFile(byte[] bytes)
    {
        // #if ... #endif はUnityエディタで実行している時だけコンパイルされるおまじない
        #if UNITY_EDITOR
        // Inspectorで指定したフォルダ名 (saveFolderName) を使用
        string folderPath = Path.Combine(Application.dataPath, saveFolderName); 
        
        // フォルダが存在しない場合は作成
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        // ファイル名が重複しないようにタイムスタンプを付与
        string fileName = $"capture_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.jpg";
        string filePath = Path.Combine(folderPath, fileName);
        
        // ファイルを書き出す
        File.WriteAllBytes(filePath, bytes);
        
        // Unityエディタに新しいアセットが作られたことを通知
        UnityEditor.AssetDatabase.Refresh(); 
        Debug.Log($"画像を {filePath} に保存しました");
        #endif
    }
    
    /// <summary>
    /// 指定されたカメラの映像をTexture2Dとしてキャプチャする
    /// </summary>
    private Texture2D CaptureCameraView(Camera camera)
    {
        // 1. カメラと同じ解像度のRenderTexture（一時的な描画先）を作成
        RenderTexture renderTexture = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 24);
        
        // 2. カメラの描画先を、画面ではなくこのRenderTextureに設定
        camera.targetTexture = renderTexture;
        
        // 3. カメラに強制的に1フレーム描画させる
        camera.Render();
        
        // 4. アクティブなRenderTextureを今作ったものに切り替え
        RenderTexture.active = renderTexture;
        
        // 5. RenderTextureの内容を新しいTexture2Dにピクセル単位でコピー
        Texture2D screenshot = new Texture2D(camera.pixelWidth, camera.pixelHeight, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, camera.pixelWidth, camera.pixelHeight), 0, 0);
        screenshot.Apply();

        // 6. 後片付け
        camera.targetTexture = null; // カメラの描画先を元に戻す
        RenderTexture.active = null; // アクティブなRenderTextureを元に戻す
        Destroy(renderTexture); // 一時的に作ったRenderTextureを破棄
        
        return screenshot;
    }

    // ========== 車のコア機能 ==========

    /// <summary>
    /// 入力に基づいて車輪のトルクや舵角を制御する
    /// </summary>
    public void Move(float motorInput, float steeringInput, bool isBraking)
    {
        // 入力値（-1.0〜+1.0）を実際のトルクと角度に変換
        float motor = maxMotorTorque * motorInput;
        float steering = maxSteeringAngle * steeringInput;
        // ブレーキ中なら最大ブレーキトルクを、そうでなければ0を設定
        float brakeTorque = isBraking ? maxBrakeTorque : 0f;

        foreach (AxleInfo axleInfo in axleInfos)
        {
            // ステアリングが有効な車軸の場合
            if (axleInfo.steering)
            {
                axleInfo.leftWheel.steerAngle = steering;
                axleInfo.rightWheel.steerAngle = steering;
            }
            // モーターが有効な車軸の場合
            if (axleInfo.motor)
            {
                // ブレーキ中はモーターのトルクを0にする
                axleInfo.leftWheel.motorTorque = isBraking ? 0f : motor;
                axleInfo.rightWheel.motorTorque = isBraking ? 0f : motor;
            }
            // 全ての車輪にブレーキトルクを適用
            axleInfo.leftWheel.brakeTorque = brakeTorque;
            axleInfo.rightWheel.brakeTorque = brakeTorque;
        }
    }

    /// <summary>
    /// WheelColliderの位置と回転を、見た目のメッシュ（子オブジェクト）に適用する
    /// </summary>
    public void ApplyLocalPositionToVisuals(WheelCollider collider)
    {
        // WheelColliderに子オブジェクト（見た目のタイヤモデル）がなければ何もしない
        if (collider.transform.childCount == 0) return;
        
        Transform visualWheel = collider.transform.GetChild(0);
        
        // WheelColliderから現在のワールド空間での位置と回転を取得
        collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
        
        // 見た目のタイヤモデルに位置と回転を適用
        visualWheel.transform.position = position;
        // アセットによってはモデルの向きが90度ずれているため、補正をかける
        visualWheel.transform.rotation = rotation * Quaternion.Euler(0f, 0f, 90f); 
    }

} // <- VLMCarControllerクラスはここで閉じます


// ========== 車軸情報のクラス ==========
/// <summary>
/// 左右のホイールと、その軸の特性を管理するクラス
/// [System.Serializable] を付けることで、Inspectorウィンドウに表示・設定できるようになる
/// </summary>
[System.Serializable]
public class AxleInfo
{
    public WheelCollider leftWheel;
    public WheelCollider rightWheel;
    public bool motor;    // このホイールはモーターにアタッチされているかどうか
    public bool steering; // このホイールはハンドルの角度を反映しているかどうか
}