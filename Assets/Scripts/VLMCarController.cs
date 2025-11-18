// using UnityEngine;
// using UnityEngine.UI;
// using TMPro;
// using UnityEngine.Networking;
// using UnityEngine.EventSystems;
// using System.Collections;
// using System.Collections.Generic;
// using System.Text;
// using System.IO;

// // --- リクエスト/レスポンス (共通) ---
// [System.Serializable]
// public class OllamaRequest
// {
//     public string model;
//     public bool stream;
//     public Message[] messages;
// }

// [System.Serializable]
// public class Message { public string role; public string content; public string[] images; }
// [System.Serializable]
// public class OllamaResponse { public ResponseMessage message; }
// [System.Serializable]
// public class ResponseMessage { public string role; public string content; }


// // --- 写真解析用のJSONスキーマ定義 ---

// [System.Serializable]
// public class OllamaPhotoSchemaRequest
// {
//     public string model;
//     public bool stream;
//     public Message[] messages;
//     public PhotoFormatSchema format; 
// }

// [System.Serializable]
// public class PhotoFormatSchema
// {
//     public string type = "object";
//     public PhotoFormatProperties properties;
//     public string[] required;
// }

// [System.Serializable]
// public class PhotoFormatProperties
// {
//     public SchemaPropertyArray detected_objects; // 物体検出のみ
// }

// [System.Serializable]
// public class SchemaPropertyBase
// {
//     public string type;
//     public string description;
// }

// [System.Serializable]
// public class SchemaPropertyArray : SchemaPropertyBase
// {
//     public SchemaPropertyBase items;
// }

// // --- 写真解析のJSON応答をパースするためのクラス ---
// [System.Serializable]
// public class AIPhotoResponse
// {
//     public string[] detected_objects; // 物体検出のみ
// }


// // --- メインのスクリプト ---
// public class VLMCarController : MonoBehaviour
// {
//     [Header("Car Physics Settings")]
//     public List<AxleInfo> axleInfos;
//     public float maxMotorTorque;
//     public float maxBrakeTorque;
//     public float maxSteeringAngle;

//     [Header("VLM and UI Settings")]
//     public Canvas canvas;
//     public Camera carCamera;
    
//     [Header("Ollama Settings")]
//     public string ollamaUrl = "http://localhost:11434/api/chat";
//     public string modelName = "qwen2.5vl:8b";

//     // ▼▼▼ 追加: VLM起動用のキーをInspectorから設定できるようにする ▼▼▼
//     [Tooltip("VLM（写真撮影）を起動するキー")]
//     public KeyCode vlmActivationKey = KeyCode.Tab; // デフォルトは 'Tab' キー

//     [Header("Image Save Settings")]
//     public string saveFolderName = "Images";

//     // private Button photoButton;
//     // ▼▼▼ 修正: テキスト変数を3つに分離 ▼▼▼
//     [Header("UI Text Fields")]
//     [SerializeField] private TMP_Text raycastText; // レイキャスト（距離）用
//     [SerializeField] private TMP_Text triggerText; // トリガー（ゾーン）用
//     [SerializeField] private TMP_Text VLMText;     // VLM（物体認識）用

//     // ▼▼▼ 追加: レイキャストのログ用（コンソールが溢れるのを防ぐため） ▼▼▼
//     private string lastRaycastTargetName = null;

//     // ▼▼▼ 追加: Gameビュー描画用のラインレンダラー ▼▼▼
//     private LineRenderer gameViewRaycastLine;
    
//     private bool isProcessing = false;
    
//     private PhotoFormatSchema _photoSchema; 

//     void Start()
//     {
//         if (canvas != null)
//         // {
//         //     Transform photoButtonTransform = canvas.transform.Find("photoButton");
//         //     if (photoButtonTransform != null) photoButton = photoButtonTransform.GetComponent<Button>();
//         // }
//         if (carCamera == null) { Debug.LogError("Target Camera が設定されていません"); return; }
//         // if (photoButton == null) { Debug.LogError("photoButton (Button) が見つかりません"); return; }

//         // ▼▼▼ 修正: 3つのテキストが設定されているか確認 ▼▼▼
//         if (raycastText == null) { Debug.LogError("raycastText が設定されていません"); return; }
//         if (triggerText == null) { Debug.LogError("triggerText が設定されていません"); return; }
//         if (VLMText == null) { Debug.LogError("VLMText が設定されていません"); return; }
        
//         // photoButton.onClick.AddListener(OnPhoto);
        
//         // 写真解析用のJSONスキーマを定義 (物体検出のみ)
//         _photoSchema = new PhotoFormatSchema
//         {
//             type = "object",
//             properties = new PhotoFormatProperties
//             {
//                 detected_objects = new SchemaPropertyArray
//                 {
//                     type = "array",
//                     description = "A list of noteworthy objects recognized in the scene.",
//                     items = new SchemaPropertyBase { type = "string" }
//                 }
//             },
//             required = new string[] { "detected_objects" }
//         };

//         Debug.Log("VLM Car Controller Initialized.");

//         // ▼▼▼ 追加: UIテキストの初期化 ▼▼▼
//         raycastText.text = "Raycast: All clear.";
//         triggerText.text = "Trigger: No target.";
//         VLMText.text = "VLM: Ready.";

//         // ▼▼▼ 追加: LineRendererの初期化 ▼▼▼
//         InitializeLineRenderer();
//     }

//     void Update()
//     {
//         if(Input.GetKeyDown(vlmActivationKey) && !isProcessing)
//         {
//             OnPhoto();
//         }
//     }

//     void FixedUpdate()
//     {
//         float manualMotor = Input.GetAxis("Vertical");
//         float manualSteering = Input.GetAxis("Horizontal");
//         bool manualBrake = Input.GetKey(KeyCode.Space);

//         Move(manualMotor, manualSteering, manualBrake);
        
//         foreach (AxleInfo axleInfo in axleInfos)
//         {
//             ApplyLocalPositionToVisuals(axleInfo.leftWheel);
//             ApplyLocalPositionToVisuals(axleInfo.rightWheel);
//         }

//         // ▼▼▼ 追加: 物理センサー（レイキャスト）を毎フレーム実行 ▼▼▼
//         CheckRaycastSensor();
//     }

//     // ========== 物理センサー (レイキャスト) ==========

//     /// <summary>
//     /// ▼▼▼ 新規追加: LineRendererを初期化する関数 ▼▼▼
//     /// </summary>
//     private void InitializeLineRenderer()
//     {
//         // 車のオブジェクトに LineRenderer コンポーネントを追加
//         gameViewRaycastLine = gameObject.AddComponent<LineRenderer>();

//         // 線の設定
//         gameViewRaycastLine.positionCount = 2; // 線の頂点数 (始点と終点)
//         gameViewRaycastLine.startWidth = 0.05f; // 線の太さ
//         gameViewRaycastLine.endWidth = 0.05f;

//         // 線のマテリアル (重要！)
//         // Unity標準の "Default-Line" マテリアルを使用します
//         gameViewRaycastLine.material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));

//         // 線の色 (赤色)
//         gameViewRaycastLine.startColor = Color.red;
//         gameViewRaycastLine.endColor = Color.red;
//     }
    
//     /// <summary>
//     /// ▼▼▼ 新規追加: レイキャストセンサーの処理 ▼▼▼
//     /// 車の真正面の物体までの「正確な距離」を測定する
//     /// </summary>
//     private void CheckRaycastSensor()
//     {
//         RaycastHit hit;
//         float maxDistance = 10.0f; // 10メートル先までスキャン
//         Vector3 rayOrigin = transform.position + new Vector3(0, 0.5f, 0); // 車の中心より少し上から撃つ

//         // レイキャストをデバッグ用にSceneビューに可視化（赤い線）
//         // Debug.DrawRay(rayOrigin, transform.forward * maxDistance, Color.red);

//         // ▼▼▼ 修正: LineRenderer の始点を設定 ▼▼▼
//         gameViewRaycastLine.SetPosition(0, rayOrigin);

//         // レイキャストを実行
//         if (Physics.Raycast(rayOrigin, transform.forward, out hit, maxDistance))
//         {
//             // 3メートル以内を「危険」と判断
//             if (hit.distance < 3.0f)
//             {
//                 raycastText.text = $"Raycast: DANGER (object {hit.distance:F1} m)";
//             }
//             else // 3m以上10m未満
//             {
//                 raycastText.text = $"Raycast: Warning (object {hit.distance:F1} m)";
//             }

//             // ▼▼▼ 追加: レイキャストのDebug.Log (状態が変わった時のみ) ▼▼▼
//             // 新しいオブジェクトに当たった瞬間にログを出す
//             if (lastRaycastTargetName != hit.collider.name)
//             {
//                 Debug.Log($"Raycast Hit: {hit.collider.name} at {hit.distance:F1} m");
//                 lastRaycastTargetName = hit.collider.name;
//             }

//             // ▼▼▼ 修正: LineRenderer の終点を「ぶつかった場所」に設定 ▼▼▼
//             gameViewRaycastLine.SetPosition(1, hit.point);
//         }
//         else
//         {
//             // --- UIの更新 (変更なし) ---
//             raycastText.text = "Raycast: All clear.";

//             // ▼▼▼ 追加: レイキャストのDebug.Log (状態が変わった時のみ) ▼▼▼
//             // オブジェクトを見失った瞬間にログを出す
//             if (lastRaycastTargetName != null)
//             {
//                 Debug.Log($"Raycast Clear: {lastRaycastTargetName} is no longer in range.");
//                 lastRaycastTargetName = null;
//             }

//             // ▼▼▼ 修正: LineRenderer の終点を「最大距離」に設定 ▼▼▼
//             Vector3 endPoint = rayOrigin + transform.forward * maxDistance;
//             gameViewRaycastLine.SetPosition(1, endPoint);
//         }
//     }

//     // ========== 物理センサー (トリガー) ==========

//     /// <summary>
//     /// ▼▼▼ 新規追加: トリガーゾーン（エリア）に何かが「入った」瞬間に呼ばれる ▼▼▼
//     /// </summary>
//     void OnTriggerEnter(Collider other)
//     {
//         Debug.Log("Trigger Enter: " + other.name);
//         triggerText.text = $"Trigger: DANGER (object inside)";
//     }

//     /// <summary>
//     /// ▼▼▼ 新規追加: トリガーゾーンに何かが「留まっている」間、毎フレーム呼ばれる ▼▼▼
//     /// </summary>
//     void OnTriggerStay()
//     {
//         // "Stay" は毎フレーム呼ばれるので、UIがちらつくのを防ぐため Enter と同じ表示を維持
//         triggerText.text = $"Trigger: DANGER (object inside)";
//     }

//     /// <summary>
//     /// ▼▼▼ 新規追加: トリガーゾーンから何かが「出た」瞬間に呼ばれる ▼▼▼
//     /// </summary>
//     void OnTriggerExit(Collider other)
//     {
//         Debug.Log("Trigger Exit: " + other.name);
//         triggerText.text = "Trigger: No target.";
//     }

//     // ▼▼▼ 削除: 複雑な UpdateResponseText 関数 (不要になりました) ▼▼▼
//     // private void UpdateResponseText(string message) { ... }

//     // ========== UIイベントハンドラ (VLM) ==========

//     /// <summary>
//     /// Photoボタンが押された時の処理 (VLMの呼び出し)
//     /// </summary>
//     private void OnPhoto()
//     {
//         // ▼▼▼ 修正: プロンプトを「一人称視点」に変更 ▼▼▼
//         string fixedQuestion = "Analyze this picture. List all noteworthy objects you see.";
        
//         StartCoroutine(SendRequestToOllama(fixedQuestion));
//     }


//     // ========== VLMへのリクエスト送信 (共通コルーチン) ==========
    
//     private IEnumerator SendRequestToOllama(string prompt)
//     {
//         if (isProcessing) yield break;
//         isProcessing = true;
//         // SetUIInteractable(false);
//         if (VLMText != null) VLMText.text = "VLM: Processing..."; // VLM処理中はUIを上書き

//         string base64Image = null;

//         // if (canvas != null) canvas.enabled = false;

//         // ▼▼▼ 修正: 写真を撮る前に LineRenderer を非表示にする ▼▼▼
//         if (gameViewRaycastLine != null) gameViewRaycastLine.enabled = false;

//         yield return null;
//         Texture2D photo = CaptureCameraView(carCamera);

//         // ▼▼▼ 修正: 写真を撮ったら LineRenderer を再表示する ▼▼▼
//         if (gameViewRaycastLine != null) gameViewRaycastLine.enabled = true;

//         // if (canvas != null) canvas.enabled = true;
//         byte[] bytes = photo.EncodeToJPG();
//         SaveImageToFile(bytes);
//         base64Image = System.Convert.ToBase64String(bytes);
//         Destroy(photo);
        
//         var message = new Message
//         {
//             role = "user",
//             content = prompt,
//             images = new string[] { base64Image } 
//         };

//         string jsonBody = "";
        
//         OllamaPhotoSchemaRequest requestData = new OllamaPhotoSchemaRequest
//         {
//             model = modelName,
//             stream = false,
//             messages = new Message[] { message },
//             format = _photoSchema 
//         };
//         jsonBody = JsonUtility.ToJson(requestData);
//         Debug.Log("Sending JSON (Photo Schema Mode): " + jsonBody);
        
//         using (UnityWebRequest request = new UnityWebRequest(ollamaUrl, "POST"))
//         {
//             byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
//             request.uploadHandler = new UploadHandlerRaw(bodyRaw);
//             request.downloadHandler = new DownloadHandlerBuffer();
//             request.SetRequestHeader("Content-Type", "application/json");

//             yield return request.SendWebRequest();

//             if (request.result != UnityWebRequest.Result.Success)
//             {
//                 Debug.LogError("Error: " + request.error);
//                 if (VLMText != null) VLMText.text = "VLM:\n Error: " + request.error;
//             }
//             else
//             {
//                 OllamaResponse response = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
//                 string responseMessage = response.message.content;
//                 Debug.Log("Raw Response: " + responseMessage);

//                 try
//                 {
//                     AIPhotoResponse photoResponse = JsonUtility.FromJson<AIPhotoResponse>(responseMessage);

//                     // VLMの結果を表示 (これは UpdateResponseText を介さない)
//                     // string formattedResponse = $"<b>Objects:</b> {string.Join(", ", photoResponse.detected_objects)}";
//                     string formattedResponse = $"VLM: {string.Join(", ", photoResponse.detected_objects)}";
//                     Debug.Log("VLM photo analysis: " + formattedResponse);
//                     if (VLMText != null) VLMText.text = formattedResponse;
//                 }
//                 catch (System.Exception e)
//                 {
//                     Debug.LogError("AIのJSON応答の解析に失敗: " + e.Message + " | 応答: " + responseMessage);
//                     if (VLMText != null) VLMText.text = "VLM: Invalid JSON" + responseMessage;
//                 }
//             }
//         }
//         // SetUIInteractable(true);
//         isProcessing = false;
//     }

// // ========== ヘルパー関数 ==========

//     // private void SetUIInteractable(bool interactable)
//     // {
//     //     photoButton.interactable = interactable;
//     // }
    
//     private void SaveImageToFile(byte[] bytes)
//     {
//         #if UNITY_EDITOR
//         string folderPath = Path.Combine(Application.dataPath, saveFolderName); 
        
//         if (!Directory.Exists(folderPath))
//         {
//             Directory.CreateDirectory(folderPath);
//         }
//         string fileName = $"capture_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.jpg";
//         string filePath = Path.Combine(folderPath, fileName);
        
//         File.WriteAllBytes(filePath, bytes);
        
//         UnityEditor.AssetDatabase.Refresh(); 
//         Debug.Log($"画像を {filePath} に保存しました");
//         #endif
//     }
//     private Texture2D CaptureCameraView(Camera camera)
//     {
//         RenderTexture renderTexture = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 24);
//         camera.targetTexture = renderTexture;
//         camera.Render();
//         RenderTexture.active = renderTexture;
//         Texture2D screenshot = new Texture2D(camera.pixelWidth, camera.pixelHeight, TextureFormat.RGB24, false);
//         screenshot.ReadPixels(new Rect(0, 0, camera.pixelWidth, camera.pixelHeight), 0, 0);
//         screenshot.Apply();
//         camera.targetTexture = null; 
//         RenderTexture.active = null; 
//         Destroy(renderTexture); 
//         return screenshot;
//     }
//     public void Move(float motorInput, float steeringInput, bool isBraking)
//     {
//         float motor = maxMotorTorque * motorInput;
//         float steering = maxSteeringAngle * steeringInput;
//         float brakeTorque = isBraking ? maxBrakeTorque : 0f;

//         foreach (AxleInfo axleInfo in axleInfos)
//         {
//             if (axleInfo.steering)
//             {
//                 axleInfo.leftWheel.steerAngle = steering;
//                 axleInfo.rightWheel.steerAngle = steering;
//             }
//             if (axleInfo.motor)
//             {
//                 axleInfo.leftWheel.motorTorque = isBraking ? 0f : motor;
//                 axleInfo.rightWheel.motorTorque = isBraking ? 0f : motor;
//             }
//             axleInfo.leftWheel.brakeTorque = brakeTorque;
//             axleInfo.rightWheel.brakeTorque = brakeTorque;
//         }
//     }
//     public void ApplyLocalPositionToVisuals(WheelCollider collider)
//     {
//         if (collider.transform.childCount == 0) return;
        
//         Transform visualWheel = collider.transform.GetChild(0);
        
//         collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
        
//         visualWheel.transform.position = position;
//         visualWheel.transform.rotation = rotation * Quaternion.Euler(0f, 0f, 90f); 
//     }

// } // <- VLMCarControllerクラス

// [System.Serializable]
// public class AxleInfo
// {
//     public WheelCollider leftWheel;
//     public WheelCollider rightWheel;
//     public bool motor;
//     public bool steering;
// }