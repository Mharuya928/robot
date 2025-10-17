using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;

// LLMによって呼び出される関数を定義する静的クラス
public static class CarCommands
{
    public static string Forward() { return "Forward"; }
    public static string Backward() { return "Backward"; }
    public static string TurnLeft() { return "TurnLeft"; }
    public static string TurnRight() { return "TurnRight"; }
    public static string Stop() { return "Stop"; }
}

// --- データ構造の定義 ---
[System.Serializable]
public class OllamaRequest
{
    public string model;
    public bool stream;
    public Message[] messages;
}

[System.Serializable]
public class Message
{
    public string role;
    public string content;
    public string[] images;
}

[System.Serializable]
public class OllamaResponse
{
    public ResponseMessage message;
}

[System.Serializable]
public class ResponseMessage
{
    public string role;
    public string content;
}

// --- メインのスクリプト ---
public class VLMCarController : MonoBehaviour
{
    // ========== 車の物理制御に関する変数 ==========
    [Header("Car Physics Settings")]
    public List<AxleInfo> axleInfos;
    public float maxMotorTorque;
    public float maxBrakeTorque;
    public float maxSteeringAngle;

    // ========== AIとUI連携に関する変数 ==========
    [Header("VLM and UI Settings")]
    public Canvas canvas;
    public Camera carCamera;
    // スクリプト内部で使用する変数
    private TMP_InputField inputField;
    private Button photoButton;
    private Button sendButton;
    private bool isInputFieldFocused = false;
    private bool isProcessing = false; // AI処理中かどうかを示す変数

    [Header("Ollama Settings")]
    private string ollamaUrl = "http://localhost:11434/api/chat";
    private string modelName = "qwen2.5vl:3b"; // ご利用のモデル名に合わせてください

    [Header("Image Save Settings")]
    public string saveFolderName = "Images"; // Inspectorから保存先フォルダ名を変更できます

    // ========== AIによる状態管理 ==========
    // AIが命令した車の現在の状態を定義
    private enum AiState
    {
        Idle,
        Forward,
        Backward,
        TurnRight,
        TurnLeft,
        Braking
    }
    
    // AIによる現在の命令状態を保存する変数。初期状態は停止。
    private AiState currentAiState = AiState.Idle;
    void Start()
    {
        if (canvas != null)
        {
            inputField = canvas.GetComponentInChildren<TMP_InputField>();
            photoButton = canvas.transform.Find("SubmitButton").GetComponent<Button>();
            sendButton = canvas.transform.Find("SendButton").GetComponent<Button>();
        }

        if (carCamera == null)
        {
            Debug.LogError("Target Camera が設定されていません");
            return;
        }

        inputField.onSelect.AddListener((_) => isInputFieldFocused = true);
        inputField.onDeselect.AddListener((_) => isInputFieldFocused = false);
        photoButton.onClick.AddListener(OnSubmit);
        Debug.Log("VLM Car Controller Initialized.");
    }
    
    void FixedUpdate()
    {
        // プレイヤーによる手動操作を優先
        float manualMotor = Input.GetAxis("Vertical");
        float manualSteering = Input.GetAxis("Horizontal");

        if (manualMotor != 0 || manualSteering != 0)
        {
            currentAiState = AiState.Idle; // 手動操作中はAIの命令をリセット
            Move(manualMotor, manualSteering, Input.GetKey(KeyCode.Space));
        }
        else
        {
            // 手動操作がない場合はAIの状態に従う
            // ExecuteAiState();
        }

        // ホイールの見た目を更新
        foreach (AxleInfo axleInfo in axleInfos)
        {
            ApplyLocalPositionToVisuals(axleInfo.leftWheel);
            ApplyLocalPositionToVisuals(axleInfo.rightWheel);
        }
    }

    private void OnSubmit()
    {
        StartCoroutine(SendRequestToOllama());
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


    private IEnumerator SendRequestToOllama()
    {
        Debug.Log("Sending request to Ollama...");
        photoButton.interactable = false;

        // UIを非表示
        if (canvas != null)
        {
            canvas.enabled = false;
        }

        // 1. カメラ映像をキャプチャ
        Texture2D photo = CaptureCameraView(carCamera);

        // UIを表示
        if (canvas != null)
        {
            canvas.enabled = true;
        }

        // 2. 画像をJPG形式のバイト配列に変換
        byte[] bytes = photo.EncodeToJPG();

        // 3. 画像を保存
#if UNITY_EDITOR // Unity Editorで実行された場合
        string folderPath = Path.Combine(Application.dataPath, "Images");
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        // 4. タイムスタンプを使ってユニークなファイル名を生成
        string fileName = $"capture_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.jpg";
        string filePath = Path.Combine(folderPath, fileName);

        // 5. ファイルを書き出す
        File.WriteAllBytes(filePath, bytes);

        // 6. Unityエディタに新しいファイルが作成されたことを通知
        UnityEditor.AssetDatabase.Refresh();

        Debug.Log($"画像を {filePath} に保存しました");
#endif
        string base64Image = System.Convert.ToBase64String(bytes);
        Destroy(photo);

        string fixedQuestion = "この景色を説明してください。";

        // Ollamaに送信するリクエストJSONを作成
        OllamaRequest requestData = new OllamaRequest
        {
            model = modelName,
            stream = false,
            messages = new Message[]
            {
                new Message
                {
                    role = "user",
                    content = fixedQuestion, // ここで固定の質問を使用
                    images = new string[] { base64Image }
                }
            }
        };
        string jsonBody = JsonUtility.ToJson(requestData);

        // UnityWebRequestでOllamaにPOSTリクエストを送信
        using (UnityWebRequest request = new UnityWebRequest(ollamaUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            // 結果を処理
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.Log("Error: " + request.error);
            }
            else
            {
                OllamaResponse response = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
                Debug.Log("Response: " + response.message.content);
            }
        }

        photoButton.interactable = true;
    }
    
    async void OnInputFieldSubmit(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            // EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(inputField.gameObject);
            Debug.Log("Input field is empty.");
            return;
        }

        Debug.Log("Input field submitted: " + message);
        inputField.interactable = false;
        string functionName = "";
        Debug.Log($"LLM suggested function: {functionName}");

        // LLMの応答に基づいてAIの状態を変更する
        HandleLLMResponse(functionName);

        inputField.interactable = true;
        inputField.text = "";
        EventSystem.current.SetSelectedGameObject(inputField.gameObject);
        // EventSystem.current.SetSelectedGameObject(null); // Enterキー押下後、フォーカスを外す
    }

    // LLMの応答（関数名）に基づいて、AIの状態（currentAiState）を変更する
    private void HandleLLMResponse(string command)
    {
        switch (command)
        {
            case "Forward":
                currentAiState = AiState.Forward;
                break;
            case "Backward":
                currentAiState = AiState.Backward;
                break;
            case "TurnRight":
                currentAiState = AiState.TurnRight;
                break;
            case "TurnLeft":
                currentAiState = AiState.TurnLeft;
                break;
            case "Stop":
                currentAiState = AiState.Braking;
                break;
            default:
                Debug.Log("Invalid command received. Setting state to Idle.");
                currentAiState = AiState.Braking;
                break;
        }
    }

    // ========== 車のコア機能 ==========
    public void Move(float motorInput, float steeringInput, bool isBraking) 
    {
        /* ...変更なし... */
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
        /* ...変更なし... */
        if (collider.transform.childCount == 0) return;
        Transform visualWheel = collider.transform.GetChild(0);
        collider.GetWorldPose(out Vector3 position, out Quaternion rotation); visualWheel.transform.position = position;
        visualWheel.transform.rotation = rotation * Quaternion.Euler(0f, 0f, 90f);
    }
}

	[System.Serializable]
	public class AxleInfo {
		public WheelCollider leftWheel;
		public WheelCollider rightWheel;
		public bool motor; // このホイールはモーターにアタッチされているかどうか
		public bool steering; // このホイールはハンドルの角度を反映しているかどうか
	}