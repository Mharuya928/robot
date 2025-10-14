// using UnityEngine;
// using UnityEngine.Networking;
// using TMPro;
// using System.Collections;
// using System.Text;
// using System;

// [System.Serializable]
// public class OllamaRequest
// {
//     public string model;
//     public string prompt;
//     public string[] images;
// }

// [System.Serializable]
// public class OllamaResponse
// {
//     public string response;
// }

// public class VLMExample : MonoBehaviour
// {
//     public Camera mainCamera;
//     public TMP_Text outputText;

//     void Start()
//     {
//         StartCoroutine(SendImageToOllama());
//     }

//     IEnumerator SendImageToOllama()
//     {
//         // カメラから画像取得
//         RenderTexture rt = new RenderTexture(512, 512, 24);
//         mainCamera.targetTexture = rt;
//         Texture2D tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
//         mainCamera.Render();
//         RenderTexture.active = rt;
//         tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
//         tex.Apply();

//         // Base64に変換
//         byte[] imageBytes = tex.EncodeToPNG();
//         string base64Image = Convert.ToBase64String(imageBytes);

//         // JSONデータ作成
//         OllamaRequest req = new OllamaRequest
//         {
//             model = "qwen2.5-vl:3b",
//             prompt = "この画像に写っているものを説明してください。",
//             images = new string[] { base64Image }
//         };

//         string jsonData = JsonUtility.ToJson(req);

//         // HTTPリクエスト送信
//         using (UnityWebRequest www = new UnityWebRequest("http://localhost:11434/api/generate", "POST"))
//         {
//             byte[] body = Encoding.UTF8.GetBytes(jsonData);
//             www.uploadHandler = new UploadHandlerRaw(body);
//             www.downloadHandler = new DownloadHandlerBuffer();
//             www.SetRequestHeader("Content-Type", "application/json");

//             yield return www.SendWebRequest();

//             if (www.result == UnityWebRequest.Result.Success)
//             {
//                 Debug.Log(www.downloadHandler.text);
//                 outputText.text = www.downloadHandler.text;
//             }
//             else
//             {
//                 Debug.LogError(www.error);
//                 outputText.text = "Error: " + www.error;
//             }
//         }
//     }
// }
