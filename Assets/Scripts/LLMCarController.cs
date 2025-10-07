using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LLMUnity;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.EventSystems;

// LLMによって呼び出される関数を定義する静的クラス
public static class CarCommands
{
    public static string Forward() { return "Forward"; }
    public static string Backward() { return "Backward"; }
    public static string TurnLeft() { return "TurnLeft"; }
    public static string TurnRight() { return "TurnRight"; }
    public static string Stop() { return "Stop"; }
}

public class LLMCarController : MonoBehaviour
{
    // ========== 車の物理制御に関する変数 ==========
    [Header("Car Physics Settings")]
    public List<AxleInfo> axleInfos; // 各車軸の情報
    public float maxMotorTorque;     // ホイールの最大トルク
    public float maxBrakeTorque;     // ブレーキの最大トルク（この行を追加）
    public float maxSteeringAngle;   // ハンドルの最大角度

    // ========== AIとUI連携に関する変数 ==========
    [Header("AI and UI Settings")]
    public LLMCharacter llmCharacter;
    public TMP_InputField inputField;

    private bool isInputFieldFocused = false;

    // ========== AIによる状態管理 ==========
    // AIが命令した車の現在の状態を定義
    private enum AiState
    {
        Idle,
        Forward,
        Backward,
        TurnRight,
        TurnLeft,
        Braking 
    }
    // AIによる現在の命令状態を保存する変数。初期状態は停止。
    private AiState currentAiState = AiState.Idle;


    // ========== 初期化処理 ==========
    void Start()
    {
        inputField.onSelect.AddListener((_) => isInputFieldFocused = true);
        inputField.onDeselect.AddListener((_) => isInputFieldFocused = false);
        inputField.onSubmit.AddListener(OnInputFieldSubmit);
        llmCharacter.grammarString = MultipleChoiceGrammar();
        Debug.Log("LLM Car Controller Initialized.");
    }

    // ========== 物理演算と状態の実行 ==========
    void FixedUpdate()
    {
        float motor = 0;
        float steering = 0;

        // プレイヤーがキーボードで手動操作しているかチェック
        float manualMotor = Input.GetAxis("Vertical");
        float manualSteering = Input.GetAxis("Horizontal");

        // 手動操作中（InputFieldが非選択かつキー入力がある）の場合
        if (!isInputFieldFocused && (manualMotor != 0 || manualSteering != 0))
        {
            // 手動操作を優先し、AIの状態をリセットする
            motor = manualMotor;
            steering = manualSteering;
            currentAiState = AiState.Idle; 
        }
        else
        {
            // 手動操作がない場合は、AIの状態に従って動作する
            switch (currentAiState)
            {
                case AiState.Forward:
                    motor = 1.0f;
                    steering = 0f;
                    break;
                case AiState.Backward:
                    motor = -1.0f;
                    steering = 0f;
                    break;
                case AiState.TurnRight:
                    motor = 0.8f;
                    steering = 1.0f;
                    break;
                case AiState.TurnLeft:
                    motor = 0.8f;
                    steering = -1.0f;
                    break;
                case AiState.Braking:
                case AiState.Idle:
                    motor = 0f;
                    steering = 0f;
                    break;
            }
        }

        bool isBraking = Input.GetKey(KeyCode.Space) || currentAiState == AiState.Braking;
        Move(motor, steering, isBraking);

        // ホイールの見た目を更新
        foreach (AxleInfo axleInfo in axleInfos)
        {
            ApplyLocalPositionToVisuals(axleInfo.leftWheel);
            ApplyLocalPositionToVisuals(axleInfo.rightWheel);
        }
    }

    // ========== LLMによるAI制御 ==========
    async void OnInputFieldSubmit(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            // EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(inputField.gameObject);
            Debug.Log("Input field is empty.");
            return;
        }
        inputField.interactable = false;
        string functionName = await llmCharacter.Chat(ConstructPrompt(message));
        Debug.Log($"LLM suggested function: {functionName}");

        // LLMの応答に基づいてAIの状態を変更する
        HandleLLMResponse(functionName);

        inputField.interactable = true;
        inputField.text = "";
        EventSystem.current.SetSelectedGameObject(inputField.gameObject);
        // EventSystem.current.SetSelectedGameObject(null); // Enterキー押下後、フォーカスを外す
    }

    // LLMの応答（関数名）に基づいて、AIの状態（currentAiState）を変更する
    private void HandleLLMResponse(string command)
    {
        switch (command)
        {
            case "Forward":
                currentAiState = AiState.Forward;
                break;
            case "Backward":
                currentAiState = AiState.Backward;
                break;
            case "TurnRight":
                currentAiState = AiState.TurnRight;
                break;
            case "TurnLeft":
                currentAiState = AiState.TurnLeft;
                break;
            case "Stop":
                currentAiState = AiState.Braking;
                break;
            default:
                Debug.Log("Invalid command received. Setting state to Idle.");
                currentAiState = AiState.Braking;
                break;
        }
    }


    // ========== LLMプロンプトとグラマーのヘルパー関数 ==========
    string[] GetFunctionNames()
    {
        List<string> functionNames = new List<string>();
        foreach (var function in typeof(CarCommands).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            functionNames.Add(function.Name);
        }
        return functionNames.ToArray();
    }
    string MultipleChoiceGrammar()
    {
        /* ...変更なし... */
        return "root ::= (\"" + string.Join("\" | \"", GetFunctionNames()) + "\")";
    }
    string ConstructPrompt(string message)
    {
        /* ...変更なし... */
        string prompt = "Which of the following functions best matches the user's input?\n\n";
        prompt += "Input: " + message + "\n\n";
        prompt += "Functions:\n"; foreach (string functionName in GetFunctionNames()) { prompt += $"- {functionName}\n"; }
        prompt += "\nRespond with only the name of the function.";
        return prompt;
    }

    // ========== 車のコア機能 ==========
    public void Move(float motorInput, float steeringInput, bool isBraking) 
    {
        /* ...変更なし... */
        float motor = maxMotorTorque * motorInput;
        float steering = maxSteeringAngle * steeringInput;
        float brakeTorque = isBraking ? maxBrakeTorque : 0f;
        foreach (AxleInfo axleInfo in axleInfos)
        {
            if (axleInfo.steering)
            {
                axleInfo.leftWheel.steerAngle = steering;
                axleInfo.rightWheel.steerAngle = steering;
            }
            if (axleInfo.motor)
            {
                axleInfo.leftWheel.motorTorque = isBraking ? 0f : motor;
                axleInfo.rightWheel.motorTorque = isBraking ? 0f : motor;
            }
            axleInfo.leftWheel.brakeTorque = brakeTorque;
            axleInfo.rightWheel.brakeTorque = brakeTorque;
        }
    }
    public void ApplyLocalPositionToVisuals(WheelCollider collider)
    {
        /* ...変更なし... */
        if (collider.transform.childCount == 0) return;
        Transform visualWheel = collider.transform.GetChild(0);
        collider.GetWorldPose(out Vector3 position, out Quaternion rotation); visualWheel.transform.position = position;
        visualWheel.transform.rotation = rotation * Quaternion.Euler(0f, 0f, 90f);
    }
}

	[System.Serializable]
	public class AxleInfo {
		public WheelCollider leftWheel;
		public WheelCollider rightWheel;
		public bool motor; // このホイールはモーターにアタッチされているかどうか
		public bool steering; // このホイールはハンドルの角度を反映しているかどうか
	}