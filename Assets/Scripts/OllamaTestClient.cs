using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

using Debug = UnityEngine.Debug;

public class OllamaTestClient : MonoBehaviour
{
    [Header("Test Settings")]
    public string modelToTest = "qwen3-vl:8b-instruct";
    public string prompt = "Explain 'Photosynthesis' in one sentence.";

    private OllamaServerManager _serverManager;
    
    [ContextMenu("Run Generation Test")]
    public void RunTest()
    {
        _serverManager = OllamaServerManager.Instance;
        if (_serverManager == null)
        {
            Debug.LogError("[OllamaTestClient] OllamaServerManager instance not found!");
            return;
        }
        StartCoroutine(GenerationTestCoroutine());
    }

    IEnumerator GenerationTestCoroutine()
    {
        if (!_serverManager.IsServerReady)
        {
            Debug.LogWarning("[OllamaTestClient] Server not ready. Test aborted.");
            yield break;
        }

        Debug.Log($"[OllamaTestClient] Sending prompt to model '{modelToTest}'...");

        var requestData = new GenerateRequest
        {
            model = modelToTest,
            prompt = prompt,
            stream = false // For a single response
        };
        string json = JsonUtility.ToJson(requestData);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest($"{_serverManager.BaseUrl}/api/generate", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[OllamaTestClient] Error: {request.error}");
                Debug.LogError($"[OllamaTestClient] Details: {request.downloadHandler.text}");
            }
            else
            {
                Debug.Log("[OllamaTestClient] Success! Response received.");
                // Note: The response is a series of JSON objects, even with stream=false.
                // We'll just log the raw text for this simple test.
                Debug.Log($"[OllamaTestClient] Raw Response:\n{request.downloadHandler.text}");

                string[] jsonObjects = request.downloadHandler.text.Trim().Split('\n');
                if (jsonObjects.Length > 0)
                {
                     try
                     {
                        var lastResponse = JsonUtility.FromJson<GenerateResponse>(jsonObjects[jsonObjects.Length -1]);
                        Debug.Log($"[OllamaTestClient] Model Response: {lastResponse.response}");
                     }
                     catch(System.Exception e)
                     {
                        Debug.LogWarning($"[OllamaTestClient] Could not parse final JSON object. Error: {e.Message}");
                     }
                }
            }
        }
    }
    
    [System.Serializable]
    private class GenerateRequest
    {
        public string model;
        public string prompt;
        public bool stream;
    }

    [System.Serializable]
    private class GenerateResponse
    {
        public string model;
        public string created_at;
        public string response;
        public bool done;
        // ... other fields exist, but we only need 'response' for this test.
    }
}
