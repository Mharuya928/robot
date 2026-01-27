using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

using Debug = UnityEngine.Debug;

public class OllamaModelManager : MonoBehaviour
{
    public static OllamaModelManager Instance { get; private set; }
    public bool IsModelReady { get; private set; } = false;

    [Header("Model (pull once)")]
    [Tooltip("このモデルが無ければ自動で pull します（例: qwen3-vl:latest など）")]
    public string modelToEnsure = "";

    [Tooltip("サーバ起動後にモデル存在チェックして、無ければ pull する")]
    public bool autoPullIfMissing = true;
    
    private OllamaServerManager _serverManager;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        _serverManager = OllamaServerManager.Instance;
        if (_serverManager == null)
        {
            Debug.LogError("[OllamaModelManager] OllamaServerManager instance not found!");
            return;
        }
        StartCoroutine(ModelManagementSequence());
    }

    IEnumerator ModelManagementSequence()
    {
        // Wait until server is ready
        while (!_serverManager.IsServerReady)
        {
            yield return null;
        }
        
        Debug.Log("[OllamaModelManager] Server is ready. Starting model management.");

        if (autoPullIfMissing && !string.IsNullOrEmpty(modelToEnsure))
        {
            bool has = false;
            yield return HasModel(modelToEnsure, h => has = h);

            if (!has)
            {
                Debug.Log($"[OllamaModelManager] model '{modelToEnsure}' not found. Pulling...");
                yield return PullModel(modelToEnsure);
            }
            else
            {
                Debug.Log($"[OllamaModelManager] model '{modelToEnsure}' is present");
            }
        }
        yield return LogAvailableModels();

        IsModelReady = true;
        Debug.Log("[OllamaModelManager] Model management complete. Ready for requests.");
    }
    
    public IEnumerator HasModel(string modelName, Action<bool> onDone)
    {
        using var req = UnityWebRequest.Get($"{_serverManager.BaseUrl}/api/tags");
        req.timeout = 3;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onDone?.Invoke(false);
            yield break;
        }

        var text = req.downloadHandler.text ?? "";
        onDone?.Invoke(text.Contains($"\"name\":\"{modelName}\"", StringComparison.Ordinal));
    }

    public IEnumerator PullModel(string modelName)
    {
        // Use the system-wide 'ollama' command. It's expected to be in the system's PATH.
        var exe = "ollama";

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"pull {EscapeArg(modelName)}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["OLLAMA_HOST"] = $"{_serverManager.host}:{_serverManager.port}";

        Process p = null;
        try
        {
            p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log("[Ollama pull] " + e.Data); };
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning("[Ollama pull] " + e.Data); };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }
        catch (Exception e)
        {
            Debug.LogError($"[OllamaModelManager] pull exception: {e.Message}");
            yield break;
        }

        while (p != null && !p.HasExited)
            yield return null;

        if (p == null) yield break;

        if (p.ExitCode == 0)
            Debug.Log($"[OllamaModelManager] pull completed: {modelName}");
        else
            Debug.LogError($"[OllamaModelManager] pull failed (exit {p.ExitCode}): {modelName}");
    }
    
    public IEnumerator LogAvailableModels()
    {
        using var req = UnityWebRequest.Get($"{_serverManager.BaseUrl}/api/tags");
        req.timeout = 3;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[OllamaModelManager] /api/tags failed: {req.error}");
            yield break;
        }
        
        var json = req.downloadHandler.text ?? "";
        try
        {
            var data = JsonUtility.FromJson<OllamaTagsResponse>(json);
            if (data?.models == null || data.models.Length == 0)
            {
                Debug.Log("[OllamaModelManager] available models: (none)");
                yield break;
            }

            Debug.Log($"[OllamaModelManager] available models ({data.models.Length}):");
            foreach (var m in data.models)
            {
                Debug.Log(m.name);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[OllamaModelManager] tags parse error: " + e.Message);
            Debug.Log("[OllamaModelManager] raw tags: " + json);
        }
    }

    static string EscapeArg(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("\"", "\\\"");
    }
    
    [Serializable]
    public class OllamaTagsResponse { public OllamaModelInfo[] models; }
    [Serializable]
    public class OllamaModelInfo { public string name; }
}

