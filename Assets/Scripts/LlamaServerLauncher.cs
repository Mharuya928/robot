using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

public class LlamaServerLauncher : MonoBehaviour
{
    [Header("Server")]
    public int port = 8080;
    public int ctxSize = 8192;
    public int gpuLayers = 999; // -ngl
    public string host = "127.0.0.1";

    [Header("Model files (relative to StreamingAssets/llama/models)")]
    public string modelFile = "Qwen3VL-8B-Instruct-Q4_K_M.gguf";
    public string mmprojFile = "mmproj-Qwen3VL-8B-Instruct-F16.gguf";

    [Header("Optional: log to file")]
    public bool logToFile = true;

    private Process proc;

    void Start()
    {
        StartServer();
    }

    void StartServer()
    {
        string baseDir = Path.Combine(Application.streamingAssetsPath, "llama");
        string serverPath = Path.Combine(baseDir, "llama-server");
        string modelsDir = Path.Combine(baseDir, "models");
        string modelPath = Path.Combine(modelsDir, modelFile);
        string mmprojPath = Path.Combine(modelsDir, mmprojFile);

        if (!File.Exists(serverPath))
        {
            UnityEngine.Debug.LogError($"llama-server not found: {serverPath}");
            return;
        }
        if (!File.Exists(modelPath))
        {
            UnityEngine.Debug.LogError($"model not found: {modelPath}");
            return;
        }
        if (!File.Exists(mmprojPath))
        {
            UnityEngine.Debug.LogError($"mmproj not found: {mmprojPath}");
            return;
        }

        // 既に同ポートで動いている場合は二重起動しない（簡易）
        // ※ 必要ならHTTPで /v1/models を叩いて判定する方が確実
        UnityEngine.Debug.Log($"Starting llama-server on {host}:{port}");

        string args =
            $"-m \"{modelPath}\" " +
            $"--mmproj \"{mmprojPath}\" " +
            $"-ngl {gpuLayers} -c {ctxSize} " +
            $"--host {host} --port {port}";

        // ★ここに置く（args作成後 / proc.Start前）
        UnityEngine.Debug.Log($"llama-server path: {serverPath}");
        UnityEngine.Debug.Log($"llama-server args: {args}");


        proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = serverPath,
            Arguments = args,
            WorkingDirectory = baseDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        proc.StartInfo.EnvironmentVariables["PATH"] =
            "/usr/local/cuda/bin:" + (proc.StartInfo.EnvironmentVariables["PATH"] ?? "");

        proc.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] =
            "/usr/local/cuda/lib64:/usr/local/cuda/targets/x86_64-linux/lib:" +
            (proc.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] ?? "");


        if (logToFile)
        {
            string logDir = Path.Combine(Application.persistentDataPath, "llama_logs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, $"llama-server-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var writer = new StreamWriter(logPath) { AutoFlush = true };

            proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) { writer.WriteLine(e.Data); } };
            proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) { writer.WriteLine(e.Data); } };
            UnityEngine.Debug.Log($"llama-server logs: {logPath}");
        }
        else
        {
            proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.LogWarning(e.Data); };
        }

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
    }

    void OnApplicationQuit()
    {
        StopServer();
    }

    void StopServer()
    {
        try
        {
            if (proc != null && !proc.HasExited)
            {
                proc.Kill();          // ← 引数なし
                proc.WaitForExit(2000);
            }
            proc?.Dispose();
            proc = null;
        }
        catch { /* ignore */ }
    }

}
