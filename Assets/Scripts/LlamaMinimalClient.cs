using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class LlamaMinimalClient : MonoBehaviour
{
    [Header("llama-server endpoint")]
    public string endpoint = "http://127.0.0.1:8080/v1/chat/completions";

    [Header("Readiness check")]
    public string modelsUrl = "http://127.0.0.1:8080/v1/models";
    public float pollIntervalSec = 0.2f;  // 200ms
    public float timeoutSec = 10.0f;      // 最大待ち時間

    [Header("Test image (optional)")]
    public Texture2D image; // なくてもOK（テキストだけで確認可能）
    [Range(1, 100)] public int jpgQuality = 80;

    void Start()
    {
        StartCoroutine(RunAfterServerReady());
    }

    // クラス内に追加
    private bool serverReady = false;

    IEnumerator RunAfterServerReady()
    {
        serverReady = false;

        // 1) /v1/models が返るまで待つ
        yield return WaitUntilModelsReady();

        // 2) 準備できてなければ呼ばない
        if (!serverReady)
        {
            Debug.LogError("Server not ready. Skip CallOnce().");
            yield break;
        }

        // 3) 1回だけ呼ぶ
        yield return CallOnce();
    }

    IEnumerator WaitUntilModelsReady()
    {
        float t0 = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - t0 < timeoutSec)
        {
            using var req = UnityWebRequest.Get(modelsUrl);
            req.timeout = 5;

            yield return req.SendWebRequest();

            if (req.responseCode == 200)
            {
                serverReady = true;
                Debug.Log("llama-server is ready (/v1/models OK).");
                yield break;
            }

            if (req.responseCode == 503)
            {
                // Loading model → 待つ
                yield return new WaitForSecondsRealtime(pollIntervalSec);
                continue;
            }

            // その他も少し待ってリトライ
            yield return new WaitForSecondsRealtime(pollIntervalSec);
        }

        serverReady = false;
        Debug.LogError($"Timeout waiting for llama-server readiness: {modelsUrl}");
    }



    IEnumerator CallOnce()
    {
        // 画像があれば base64 にする（なくてもOK）
        string imgPart = "";
        if (image != null)
        {
            byte[] jpg = image.EncodeToJPG(jpgQuality);
            string b64 = Convert.ToBase64String(jpg);
            imgPart =
                ",{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/jpeg;base64," + b64 + "\"}}";
        }

        // 最小の payload（text onlyでも可）
        string bodyJson =
            "{"
          + "\"messages\":[{"
          + "\"role\":\"user\","
          + "\"content\":["
          + "{\"type\":\"text\",\"text\":\"Describe the image briefly.\"}"
          + imgPart
          + "]"
          + "}],"
          + "\"max_tokens\":150,"
          + "\"temperature\":0.2"
          + "}";

        byte[] body = Encoding.UTF8.GetBytes(bodyJson);

        using var req = new UnityWebRequest(endpoint, "POST");
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 30;

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"HTTP Error: {req.error}\n{req.downloadHandler.text}");
            yield break;
        }

        Debug.Log("Raw response:\n" + req.downloadHandler.text);
    }
}
