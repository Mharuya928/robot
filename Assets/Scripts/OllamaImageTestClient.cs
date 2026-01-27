using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System;

using Debug = UnityEngine.Debug;

public class OllamaImageTestClient : MonoBehaviour
{
    [Header("Test Settings")]
    [Tooltip("A VLM model that is compatible with the /api/chat endpoint, e.g., llava, qwen3-vl")]
    public string modelToTest = "qwen3-vl:8b-instruct";
    [Tooltip("The text prompt to send with the image.")]
    public string prompt = "What is in this image? Describe it in detail.";
    [Tooltip("Assign a test image from your project assets here.")]
    public Texture2D imageToSend;

    private OllamaServerManager _serverManager;
    
    [ContextMenu("Run Image Generation Test (via Chat Endpoint)")]
    public void RunTest()
    {
        _serverManager = OllamaServerManager.Instance;
        if (_serverManager == null)
        {
            Debug.LogError("[OllamaImageTestClient] OllamaServerManager instance not found! Please add it to your scene.");
            return;
        }

        if (imageToSend == null)
        {
            Debug.LogError("[OllamaImageTestClient] No image has been assigned to 'imageToSend' in the Inspector! Please assign an image.");
            return;
        }
        
        StartCoroutine(ChatTestCoroutine());
    }

    IEnumerator ChatTestCoroutine()
    {
        if (!_serverManager.IsServerReady)
        {
            Debug.LogWarning("[OllamaImageTestClient] Ollama server is not ready. Test aborted.");
            yield break;
        }

        Debug.Log($"[OllamaImageTestClient] Sending prompt and image to model '{modelToTest}' via /api/chat...");

        byte[] imageBytes = imageToSend.EncodeToJPG(); 
        string base64Image = Convert.ToBase64String(imageBytes);

        var message = new ChatMessage
        {
            role = "user",
            content = prompt,
            images = new string[] { base64Image }
        };

        var requestData = new ChatRequest
        {
            model = modelToTest,
            stream = false,
            messages = new ChatMessage[] { message }
        };

        string json = JsonUtility.ToJson(requestData);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest($"{_serverManager.BaseUrl}/api/chat", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            
            request.timeout = 120;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[OllamaImageTestClient] Error: {request.error}");
                Debug.LogError($"[OllamaImageTestClient] Response Code: {request.responseCode}");
                Debug.LogError($"[OllamaImageTestClient] Details: {request.downloadHandler.text}");
            }
            else
            {
                Debug.Log("[OllamaImageTestClient] Success! Response received.");
                Debug.Log($"[OllamaImageTestClient] Raw Response:\n{request.downloadHandler.text}");

                try
                {
                    var response = JsonUtility.FromJson<ChatResponse>(request.downloadHandler.text);
                    Debug.Log($"[OllamaImageTestClient] Model Response: {response.message.content}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[OllamaImageTestClient] Could not parse the JSON response. Error: {e.Message}");
                }
            }
        }
    }
    
    [System.Serializable]
    private class ChatMessage
    {
        public string role;
        public string content;
        public string[] images;
    }

    [System.Serializable]
    private class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public bool stream;
    }

    [System.Serializable]
    private class ChatResponse
    {
        public ChatMessage message;
        // Other fields might exist
    }
}
