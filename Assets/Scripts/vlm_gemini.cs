using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

// --- データ構造の定義 (前回と同じ) ---
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
public class vlm_gemini : MonoBehaviour
{
    [Header("UI Components")]
    public RawImage cameraView; // プレビュー用のUI（任意）
    public TMP_InputField questionInputField;
    public Button submitButton;
    public TMP_Text responseText;

    [Header("Ollama Settings")]
    private string ollamaUrl = "http://localhost:11434/api/chat";
    private string modelName = "qwen2.5-vl:3b";

    // ▼▼▼ 変更点 ▼▼▼
    // WebCamTextureは不要になったので削除
    // private WebCamTexture webCamTexture;

    // MainCameraへの参照を追加
    private Camera mainCamera;

    void Start()
    {
        // ▼▼▼ 変更点 ▼▼▼
        // MainCameraを自動で見つけてくる
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("シーンに 'MainCamera' タグの付いたカメラがありません。");
            return;
        }

        // ボタンにクリックイベントを登録
        submitButton.onClick.AddListener(OnSubmit);
    }

    private void OnSubmit()
    {
        StartCoroutine(SendRequestToOllama());
    }

    // カメラの見た目をキャプチャするヘルパーメソッド
    private Texture2D CaptureCameraView(Camera camera)
    {
        // 1. RenderTextureをカメラと同じサイズで一時的に作成
        RenderTexture renderTexture = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 24);
        
        // 2. カメラの描画先をこのRenderTextureに設定
        camera.targetTexture = renderTexture;
        
        // 3. カメラに描画を強制実行
        camera.Render();
        
        // 4. アクティブなRenderTextureを切り替えて、ピクセルを読み込む
        RenderTexture.active = renderTexture;
        Texture2D screenshot = new Texture2D(camera.pixelWidth, camera.pixelHeight, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, camera.pixelWidth, camera.pixelHeight), 0, 0);
        screenshot.Apply();

        // 5. 後片付け
        camera.targetTexture = null; // カメラの描画先を元に戻す (Gameビュー)
        RenderTexture.active = null;
        Destroy(renderTexture);
        
        return screenshot;
    }


    private IEnumerator SendRequestToOllama()
    {
        responseText.text = "AIが考えています...";
        submitButton.interactable = false;
        
        // ▼▼▼ 変更点 ▼▼▼
        // WebCamTextureからではなく、MainCameraから画像をキャプチャする
        // 1. カメラ映像をキャプチャしてBase64にエンコード
        Texture2D photo = CaptureCameraView(mainCamera);

        // (任意) プレビューUIにキャプチャした画像を表示
        if(cameraView != null)
        {
            cameraView.texture = photo;
        }
        
        byte[] bytes = photo.EncodeToJPG();
        string base64Image = System.Convert.ToBase64String(bytes);
        
        // Texture2Dはもう不要なので、メモリ解放のためにDestroyする
        // これをしないと、ボタンを押すたびにメモリ使用量が増え続ける
        if(cameraView == null || cameraView.texture != photo)
        {
             Destroy(photo);
        }

        // 2. Ollamaに送信するリクエストJSONを作成 (前回と同じ)
        OllamaRequest requestData = new OllamaRequest
        {
            model = modelName,
            stream = false,
            messages = new Message[]
            {
                new Message
                {
                    role = "user",
                    content = questionInputField.text,
                    images = new string[] { base64Image }
                }
            }
        };
        string jsonBody = JsonUtility.ToJson(requestData);

        // 3. UnityWebRequestでOllamaにPOSTリクエストを送信 (前回と同じ)
        using (UnityWebRequest request = new UnityWebRequest(ollamaUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            // 4. 結果を処理 (前回と同じ)
            if (request.result != UnityWebRequest.Result.Success)
            {
                responseText.text = "エラー: " + request.error;
            }
            else
            {
                OllamaResponse response = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
                responseText.text = response.message.content;
            }
        }

        submitButton.interactable = true;
    }
}