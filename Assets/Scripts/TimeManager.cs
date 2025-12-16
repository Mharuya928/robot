using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.IO;
using System;

public class TimeManager : MonoBehaviour
{
    [Header("UI設定")]
    public TMP_Text timerText;
    public Button startStopButton;
    public TMP_Text startStopLabel; 
    public Button resetButton;

    // [Header("連携")]
    // public ObstacleRandomizer obstacleRandomizer;
    // public CarRandomizer carRandomizer;

    [Header("自動化設定")]
    [Tooltip("チェックを入れると、ゲーム開始(VLM起動)と同時にタイマーが動きます")]
    public bool autoStart = false; // 自動スタートはOFF(手動/TAB推奨)

    [Header("保存設定")]
    public bool saveResults = true;
    
    [Tooltip("Assetsフォルダ内のどのフォルダに保存するか")]
    public string saveFolderName = "BenchmarkData"; // ★追加

    [Tooltip("ファイル名")]
    public string fileName = "BenchmarkResults.csv";

    // 内部変数
    private bool isRunning = false;
    public bool IsRunning => isRunning;
    private float startTime;
    private float elapsedOffset = 0f;

    void Start()
    {
        if (startStopButton != null) startStopButton.onClick.AddListener(OnStartStopPressed);
        if (resetButton != null) resetButton.onClick.AddListener(OnResetPressed);
        
        UpdateTimerText(0f);
        
        if (autoStart) StartTimer();
    }

    void Update()
    {
        if (isRunning)
        {
            float currentTime = (Time.time - startTime) + elapsedOffset;
            UpdateTimerText(currentTime);
        }
    }

    private void UpdateTimerText(float time)
    {
        if (timerText != null) timerText.text = "Time: " + time.ToString("F2");
    }

    public void OnStartStopPressed()
    {
        if (isRunning) StopTimer();
        else StartTimer();
    }

    public void StartTimer()
    {
        if (isRunning) return;
        isRunning = true;
        startTime = Time.time;
        
        if (startStopLabel != null) startStopLabel.text = "Stop";
        if (resetButton != null) resetButton.interactable = false;
        
        Debug.Log("Timer Started");
    }

    public void OnResetPressed()
    {
        if (!isRunning)
        {
            elapsedOffset = 0f;
            UpdateTimerText(0f);
            
            // if (obstacleRandomizer != null) obstacleRandomizer.Shuffle();
            // if (carRandomizer != null) carRandomizer.Shuffle();

            Debug.Log("Reset Complete.");
        }
    }

    public float StopTimer()
    {
        if (isRunning)
        {
            isRunning = false;
            float currentRunTime = Time.time - startTime;
            elapsedOffset += currentRunTime;
            
            if (startStopLabel != null) startStopLabel.text = "Start";
            if (resetButton != null) resetButton.interactable = true;

            if (saveResults) SaveResultToFile(elapsedOffset);
        }
        return elapsedOffset;
    }

    // ▼▼▼ 修正: Assetsフォルダ内に保存する処理 ▼▼▼
    private void SaveResultToFile(float time)
    {
        // 1. 保存先フォルダのパスを作る (Assets/BenchmarkData)
        string folderPath = Path.Combine(Application.dataPath, saveFolderName);

        // 2. フォルダがなければ作る
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        // 3. ファイルパスを作る
        string filePath = Path.Combine(folderPath, fileName);
        
        string date = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        string line = $"{date}, {time:F2}";

        try
        {
            if (!File.Exists(filePath)) File.WriteAllText(filePath, "Date, Time(sec)\n");
            File.AppendAllText(filePath, line + "\n");
            
            Debug.Log($"Result saved to: {filePath} -> {line}");

            // 4. Unityエディタ上でファイルが表示されるように更新をかける (エディタ実行時のみ)
            #if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            #endif
        }
        catch (Exception e)
        {
            Debug.LogError("Save Error: " + e.Message);
        }
    }
    // ▲▲▲ 修正ここまで ▲▲▲
}