using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TimeManager : MonoBehaviour
{
    [Header("UI設定")]
    [Tooltip("タイムを表示するテキスト")]
    public TMP_Text timerText;

    [Tooltip("スタート/ストップを切り替えるボタン")]
    public Button startStopButton;
    
    [Tooltip("スタートボタンのラベルテキスト (Start/Stopの文字変更用)")]
    public TMP_Text startStopLabel; 

    [Tooltip("リセットボタン")]
    public Button resetButton;

    // 内部変数
    private bool isRunning = false; // 計測中かどうか
    private float startTime;        // 計測開始時刻
    private float elapsedOffset = 0f; // 一時停止するまでの累積時間

    void Start()
    {
        // ボタンに処理を登録
        if (startStopButton != null)
            startStopButton.onClick.AddListener(OnStartStopPressed);

        if (resetButton != null)
            resetButton.onClick.AddListener(OnResetPressed);

        // ▼▼▼ 追加: ゲーム開始時に0秒を表示する ▼▼▼
        if (timerText != null)
        {
            timerText.text = "Time: 0.00";
        }
        // ▲▲▲ 追加ここまで ▲▲▲
    }

    void Update()
    {
        // 計測中ならタイムを更新
        if (isRunning)
        {
            float currentTime = (Time.time - startTime) + elapsedOffset;
            if (timerText != null)
            {
                timerText.text = "Time: " + currentTime.ToString("F2");
            }
        }
    }

    // スタート/ストップボタンが押された時の処理
    public void OnStartStopPressed()
    {
        if (isRunning)
        {
            // ストップ処理
            StopTimer();
        }
        else
        {
            // スタート（または再開）処理
            isRunning = true;
            startTime = Time.time;
            
            // UI更新
            if (startStopLabel != null) startStopLabel.text = "Stop";
            if (resetButton != null) resetButton.interactable = false; // 走行中はリセット不可
        }
    }

    // リセットボタンが押された時の処理
    public void OnResetPressed()
    {
        // 計測中でない場合のみリセット可能
        if (!isRunning)
        {
            elapsedOffset = 0f;
            if (timerText != null) timerText.text = "Time: 0.00";
            Debug.Log("Timer Reset");
        }
    }

    // タイマーを止める（ボタンまたはGoalTriggerから呼ばれる）
    public float StopTimer()
    {
        if (isRunning)
        {
            isRunning = false;
            // 経過時間を保存しておく（再開時に使うため）
            elapsedOffset += Time.time - startTime;
            
            // UI更新
            if (startStopLabel != null) startStopLabel.text = "Start";
            if (resetButton != null) resetButton.interactable = true; // 停止中はリセット可能
        }
        return elapsedOffset;
    }
}