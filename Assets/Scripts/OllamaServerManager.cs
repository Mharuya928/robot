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

    [Header("Logging")]
    [Tooltip("ログを persistentDataPath に保存する")]
    public bool writeLogs = true;

    private Process _serveProc;
    private bool _startedByThisScript = false;
    private bool _isServerReady = false;

    public string BaseUrl => $"http://{host}:{port}";
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
            Debug.Log("[OllamaServer] No running server detected. Attempting to start system-installed 'ollama'.");
            
            // Use the system-installed 'ollama' command.
            if (!StartServe("ollama"))
            {
                Debug.LogError("[OllamaServer] Failed to start 'ollama serve' process. Is Ollama installed on the system and in the PATH?");
                yield break;
            }

            float t = 0f;
            while (t < startupTimeoutSec)
            {
                bool ok = false;
                yield return CheckAlive(a => ok = a);
                if (ok)
                {
                    Debug.Log("[OllamaServer] 'ollama serve' is up.");
                    alive = true;
                    break;
                }
                t += 0.25f;
                yield return new WaitForSeconds(0.25f);
            }

            if (!alive)
            {
                Debug.LogError($"[OllamaServer] 'ollama serve' start timeout after {startupTimeoutSec} seconds. Check system logs for details.");
                yield break;
            }
        }
        else
        {
            Debug.Log("[OllamaServer] Detected an already running Ollama server. Using external instance.");
            _startedByThisScript = false; // We didn't start this one.
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
    
    bool StartServe(string exePath)
    {
        if (writeLogs && !Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);

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
            // The OLLAMA_HOST environment variable will be respected by the 'ollama serve' command.
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
            Debug.LogError($"[OllamaServer] 'ollama serve' start exception: {e.Message}. Ensure 'ollama' is in your system's PATH.");
            return false;
        }
    }

    void StopOllamaIfNeeded()
    {
        // Only stop the server if this script started it.
        if (!killOnQuit || !_startedByThisScript) return;
        
        try
        {
            if (_serveProc != null && !_serveProc.HasExited)
            {
                KillProcessTreeLinux(_serveProc);
                Debug.Log("[OllamaServer] Stopped 'ollama serve' process tree started by this script.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OllamaServer] Exception while stopping 'ollama serve' process: {e.Message}");
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
            // Use pkill to send TERM signal to the process group of the parent process.
            // This is a more robust way to clean up child processes.
            RunBash($"pkill -TERM -P {proc.Id}");
            proc.Kill(); // Force kill if it didn't terminate.
            proc.Dispose();
        }
        catch { }
    }

    static void RunBash(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = "bash", Arguments = $"-c \"{cmd}\"", UseShellExecute = false, CreateNoWindow = true };
            using(var p = Process.Start(psi))
            {
                p?.WaitForExit(1000); // Wait for 1 second
            }
        }
        catch(Exception e) 
        {
            Debug.LogWarning($"[OllamaServer] RunBash failed for cmd '{cmd}': {e.Message}");
        }
    }
    
    static void AppendLog(string path, string line)
    {
        try { File.AppendAllText(path, line + "\n"); } catch { }
    }
}
