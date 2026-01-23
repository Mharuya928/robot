using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class OllamaEmbeddedServerLinux : MonoBehaviour
{



    [Header("Ollama Serve Settings")]
    public string host = "127.0.0.1";
    public int port = 11434;

    [Tooltip("起動チェックの最大待ち時間（秒）")]
    public float startupTimeoutSec = 12f;

    [Tooltip("アプリ終了時に、このスクリプトが起動したollamaを停止する")]
    public bool killOnQuit = true;

    [Header("Model (pull once)")]
    [Tooltip("このモデルが無ければ自動で pull します（例: qwen3-vl:latest など）")]
    public string modelToEnsure = "";  // 空ならpullしない

    [Tooltip("サーバ起動後にモデル存在チェックして、無ければ pull する")]
    public bool autoPullIfMissing = true;

    [Header("Paths (Linux)")]
    [Tooltip("StreamingAssets内のollama相対パス")]
    public string streamingRelativePath = "ollama/ollama"; // Assets/StreamingAssets/ollama/ollama

    [Tooltip("persistentDataPath配下に作る実行ファイル配置フォルダ名")]
    public string persistentBinDirName = "ollama_bin";

    [Tooltip("ログを persistentDataPath に保存する")]
    public bool writeLogs = true;

    private Process _serveProc;

    string BaseUrl => $"http://{host}:{port}";
    string TagsUrl => $"{BaseUrl}/api/tags";
    string PersistentBinDir => Path.Combine(Application.persistentDataPath, persistentBinDirName);
    string PersistentOllamaPath => Path.Combine(PersistentBinDir, "ollama");
    string LogDir => Path.Combine(Application.persistentDataPath, "ollama_logs");
    string CurrentLogPath => Path.Combine(LogDir, $"ollama_{DateTime.Now:yyyyMMdd_HHmmss}.log");

    [Serializable]
    public class OllamaTagsResponse
    {
        public OllamaModelInfo[] models;
    }

    [Serializable]
    public class OllamaModelInfo
    {
        public string name;
        public long size;
        public string modified_at;
        public string digest;
    }

    private bool _startedByThisScript = false;

#if UNITY_EDITOR
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        StartCoroutine(BootSequence());
    }

    void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            StopOllamaIfNeeded();
        }
    }

    void OnDestroy()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        StopOllamaIfNeeded();
    }
#else
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        StartCoroutine(BootSequence());
    }
#endif


    IEnumerator BootSequence()
    {
        // 1) 既に起動しているなら何もしない
        bool alive = false;
        yield return CheckAlive(a => alive = a);

        if (!alive)
        {
            // 2) StreamingAssets -> persistent へコピー
            yield return CopyOllamaToPersistent();

            // 3) chmod +x
            if (!EnsureExecutable(PersistentOllamaPath))
            {
                Debug.LogError("[Ollama] chmod failed. Check file permissions.");
                yield break;
            }

            // 4) serve 起動
            if (!StartServe(PersistentOllamaPath))
            {
                Debug.LogError("[Ollama] failed to start serve process.");
                yield break;
            }

            // 5) 起動待ち
            float t = 0f;
            while (t < startupTimeoutSec)
            {
                bool ok = false;
                yield return CheckAlive(a => ok = a);
                if (ok)
                {
                    Debug.Log("[Ollama] serve is up");
                    alive = true;
                    break;
                }
                t += 0.25f;
                yield return new WaitForSeconds(0.25f);
            }

            if (!alive)
            {
                Debug.LogError("[Ollama] start timeout");
                yield break;
            }
        }
        else
        {
            Debug.Log("[Ollama] already running (external or previous instance)");
            _startedByThisScript = IsPersistentOllamaServing();
        }


        // 6) モデルが無ければ pull（1回だけ）
        if (autoPullIfMissing && !string.IsNullOrEmpty(modelToEnsure))
        {
            bool has = false;
            yield return HasModel(modelToEnsure, h => has = h);

            if (!has)
            {
                Debug.Log($"[Ollama] model '{modelToEnsure}' not found. Pulling...");
                yield return PullModel(modelToEnsure);
            }
            else
            {
                Debug.Log($"[Ollama] model '{modelToEnsure}' is present");
            }
        }
        yield return LogAvailableModels();
    }

    bool IsPersistentOllamaServing()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-lc \"ps aux | grep '[o]llama serve'\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return text.Contains(PersistentOllamaPath + " serve");
        }
        catch { return false; }
    }


    IEnumerator CopyOllamaToPersistent()
    {
        Directory.CreateDirectory(PersistentBinDir);
        if (writeLogs) Directory.CreateDirectory(LogDir);

        string src = Path.Combine(Application.streamingAssetsPath, streamingRelativePath);

        if (!File.Exists(src))
        {
            Debug.LogError($"[Ollama] not found in StreamingAssets: {src}");
            yield break;
        }

        try
        {
            // 差分があれば上書き
            bool needCopy = !File.Exists(PersistentOllamaPath) ||
                            new FileInfo(PersistentOllamaPath).Length != new FileInfo(src).Length;

            if (needCopy)
            {
                File.Copy(src, PersistentOllamaPath, overwrite: true);
                Debug.Log($"[Ollama] copied to: {PersistentOllamaPath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[Ollama] copy failed: " + e.Message);
        }

        yield return null;
    }

    bool EnsureExecutable(string path)
    {
        try
        {
            // chmod +x "<path>"
            var psi = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Exception e)
        {
            Debug.LogError("[Ollama] chmod exception: " + e.Message);
            return false;
        }
    }

    bool StartServe(string exePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // 明示（安定用）
            psi.Environment["OLLAMA_HOST"] = $"{host}:{port}";

            _serveProc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _serveProc.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                if (writeLogs) AppendLog(CurrentLogPath, e.Data);
                Debug.Log("[Ollama] " + e.Data);
            };

            _serveProc.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                if (writeLogs) AppendLog(CurrentLogPath, "[ERR] " + e.Data);
                Debug.LogWarning("[Ollama] " + e.Data);
            };

            _serveProc.Start();
            _serveProc.BeginOutputReadLine();
            _serveProc.BeginErrorReadLine();

            _startedByThisScript = true;

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[Ollama] serve start exception: " + e.Message);
            return false;
        }
    }

    IEnumerator CheckAlive(Action<bool> onDone)
    {
        using var req = UnityWebRequest.Get(TagsUrl);
        req.timeout = 1;
        yield return req.SendWebRequest();

        bool ok = req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300;
        onDone?.Invoke(ok);
    }

    IEnumerator HasModel(string modelName, Action<bool> onDone)
    {
        using var req = UnityWebRequest.Get(TagsUrl);
        req.timeout = 3;
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onDone?.Invoke(false);
            yield break;
        }

        // /api/tags の JSON に modelName が含まれるかを雑にチェック（まずはこれで十分）
        // 厳密にやるなら JSON パース推奨
        var text = req.downloadHandler.text ?? "";
        onDone?.Invoke(text.Contains($"\"name\":\"{modelName}\"", StringComparison.Ordinal));
    }

    IEnumerator PullModel(string modelName)
    {
        var exe = PersistentOllamaPath;
        if (!File.Exists(exe))
        {
            Debug.LogError("[Ollama] embedded ollama not found: " + exe);
            yield break;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"pull {EscapeArg(modelName)}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["OLLAMA_HOST"] = $"{host}:{port}";

        Process p = null;

        // ✅ try/catch は「開始」までに限定（yieldしない）
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
            Debug.LogError("[Ollama] pull exception: " + e.Message);
            yield break;
        }

        // ✅ yield する部分は try/catch の外
        while (p != null && !p.HasExited)
            yield return null;

        if (p == null)
            yield break;

        if (p.ExitCode == 0)
            Debug.Log($"[Ollama] pull completed: {modelName}");
        else
            Debug.LogError($"[Ollama] pull failed (exit {p.ExitCode}): {modelName}");
    }


    static string EscapeArg(string s)
    {
        // スペース等があっても壊れにくい簡易版
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("\"", "\\\"");
    }

    static void AppendLog(string path, string line)
    {
        try { File.AppendAllText(path, line + "\n"); } catch { }
    }

    void OnApplicationQuit() => StopOllamaIfNeeded();
    void OnDisable() => StopOllamaIfNeeded();

    static void KillProcessTreeLinux(Process proc)
    {
        try
        {
            if (proc == null) return;
            if (proc.HasExited) return;

            int pid = proc.Id;

            // 子プロセスを先に終了（Linux）
            RunBash($"pkill -TERM -P {pid} || true");

            // 本体を終了
            proc.Kill();   // ← entireProcessTree は使わない
            proc.Dispose();
        }
        catch { }
    }

    static void RunBash(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-lc \"{cmd}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch { }
    }

    IEnumerator LogAvailableModels()
    {
        using var req = UnityEngine.Networking.UnityWebRequest.Get(TagsUrl);
        req.timeout = 3;
        yield return req.SendWebRequest();

        if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[Ollama] /api/tags failed: {req.error}");
            yield break;
        }

        var json = req.downloadHandler.text ?? "";
        OllamaTagsResponse data = null;

        try
        {
            data = JsonUtility.FromJson<OllamaTagsResponse>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Ollama] tags parse error: " + e.Message);
            Debug.Log("[Ollama] raw tags: " + json);
            yield break;
        }

        if (data?.models == null || data.models.Length == 0)
        {
            Debug.Log("[Ollama] available models: (none)");
            yield break;
        }

        Debug.Log($"[Ollama] available models ({data.models.Length}):");
        foreach (var m in data.models)
        {
            Debug.Log(m.name);
        }
    }

    void StopOllamaIfNeeded()
    {
        if (!killOnQuit) return;
        if (!_startedByThisScript) return;

        try
        {
            KillProcessTreeLinux(_serveProc);
            _serveProc = null;
            _startedByThisScript = false;
            Debug.Log("[Ollama] stopped");
        }
        catch { }
    }

}


