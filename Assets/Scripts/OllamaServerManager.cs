using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Debug = UnityEngine.Debug;

public class OllamaServerManager : MonoBehaviour
{
    public static OllamaServerManager Instance { get; private set; }

    [Header("Ollama Serve Settings")]
    public string host = "127.0.0.1";
    public int port = 11434;

    [Tooltip("起動チェックの最大待ち時間（秒）")]
    public float startupTimeoutSec = 12f;

    [Tooltip("アプリ終了時に、このスクリプトが起動したollamaを停止する")]
    public bool killOnQuit = true;

    [Header("Paths (Linux)")]
    [Tooltip("StreamingAssets内のollama相対パス")]
    public string streamingRelativePath = "ollama/ollama";

    [Tooltip("persistentDataPath配下に作る実行ファイル配置フォルダ名")]
    public string persistentBinDirName = "ollama_bin";

    [Tooltip("ログを persistentDataPath に保存する")]
    public bool writeLogs = true;

    private Process _serveProc;
    private bool _startedByThisScript = false;
    private bool _isServerReady = false;

    public string BaseUrl => $"http://{host}:{port}";
    public string PersistentBinDir => Path.Combine(Application.persistentDataPath, persistentBinDirName);
    public string PersistentOllamaPath => Path.Combine(PersistentBinDir, "ollama");
    string LogDir => Path.Combine(Application.persistentDataPath, "ollama_logs");
    string CurrentLogPath => Path.Combine(LogDir, $"ollama_{DateTime.Now:yyyyMMdd_HHmmss}.log");
    
    public bool IsServerReady => _isServerReady;


#if UNITY_EDITOR
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
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
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        StartCoroutine(BootSequence());
    }
#endif

    void OnApplicationQuit() => StopOllamaIfNeeded();
    void OnDisable() => StopOllamaIfNeeded();

    IEnumerator BootSequence()
    {
        bool alive = false;
        yield return CheckAlive(a => alive = a);

        if (!alive)
        {
            yield return CopyOllamaToPersistent();
            if (!EnsureExecutable(PersistentOllamaPath))
            {
                Debug.LogError("[OllamaServer] chmod failed. Check file permissions.");
                yield break;
            }

            if (!StartServe(PersistentOllamaPath))
            {
                Debug.LogError("[OllamaServer] failed to start serve process.");
                yield break;
            }

            float t = 0f;
            while (t < startupTimeoutSec)
            {
                bool ok = false;
                yield return CheckAlive(a => ok = a);
                if (ok)
                {
                    Debug.Log("[OllamaServer] serve is up");
                    alive = true;
                    break;
                }
                t += 0.25f;
                yield return new WaitForSeconds(0.25f);
            }

            if (!alive)
            {
                Debug.LogError("[OllamaServer] start timeout");
                yield break;
            }
        }
        else
        {
            Debug.Log("[OllamaServer] already running (external or previous instance)");
            _startedByThisScript = IsPersistentOllamaServing();
        }
        
        _isServerReady = true;
        Debug.Log("[OllamaServer] Server is ready.");
    }
    
    public IEnumerator CheckAlive(Action<bool> onDone)
    {
        using var req = UnityEngine.Networking.UnityWebRequest.Get($"{BaseUrl}/api/tags");
        req.timeout = 1;
        yield return req.SendWebRequest();
        bool ok = req.result == UnityEngine.Networking.UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300;
        onDone?.Invoke(ok);
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
            Debug.LogError($"[OllamaServer] not found in StreamingAssets: {src}");
            yield break;
        }

        try
        {
            bool needCopy = !File.Exists(PersistentOllamaPath) ||
                            new FileInfo(PersistentOllamaPath).Length != new FileInfo(src).Length;

            if (needCopy)
            {
                File.Copy(src, PersistentOllamaPath, overwrite: true);
                Debug.Log($"[OllamaServer] copied to: {PersistentOllamaPath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[OllamaServer] copy failed: {e.Message}");
        }
        yield return null;
    }

    bool EnsureExecutable(string path)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = "chmod", Arguments = $" +x \"{path}\"", UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Exception e)
        {
            Debug.LogError($"[OllamaServer] chmod exception: {e.Message}");
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
            psi.Environment["OLLAMA_HOST"] = $"{host}:{port}";

            _serveProc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _serveProc.OutputDataReceived += (_, e) => {
                if (string.IsNullOrEmpty(e.Data)) return;
                if (writeLogs) AppendLog(CurrentLogPath, e.Data);
                Debug.Log("[Ollama] " + e.Data);
            };
            _serveProc.ErrorDataReceived += (_, e) => {
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
            Debug.LogError($"[OllamaServer] serve start exception: {e.Message}");
            return false;
        }
    }

    void StopOllamaIfNeeded()
    {
        if (!killOnQuit || !_startedByThisScript) return;
        
        try
        {
            if (_serveProc != null && !_serveProc.HasExited)
            {
                KillProcessTreeLinux(_serveProc);
                Debug.Log("[OllamaServer] Stopped own process tree.");
            }
            else
            {
                string commandFilter = $"{PersistentOllamaPath} serve";
                Debug.Log($"[OllamaServer] Stopping orphaned process by command match: {commandFilter}");
                RunBash($"pkill -f \"{commandFilter}\" ");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OllamaServer] Exception while stopping: {e.Message}");
        }
        finally
        {
             _serveProc = null;
            _startedByThisScript = false;
            _isServerReady = false;
        }
    }

    static void KillProcessTreeLinux(Process proc)
    {
        if (proc == null || proc.HasExited) return;
        try
        {
            RunBash($"pkill -TERM -P {proc.Id} || true");
            proc.Kill();
            proc.Dispose();
        }
        catch { }
    }

    static void RunBash(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = "bash", Arguments = $"-lc \"{cmd}\"", UseShellExecute = false, CreateNoWindow = true };
            Process.Start(psi)?.WaitForExit();
        }
        catch { }
    }
    
    static void AppendLog(string path, string line)
    {
        try { File.AppendAllText(path, line + "\n"); } catch { }
    }
}
