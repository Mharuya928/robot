using UnityEngine;
using TMPro;
using StarterAssets;
using System.Collections;

public class UIInputController : MonoBehaviour
{
    public TMP_InputField commandInputField;
    public StarterAssetsInputs targetInputs;

    void Start()
    {
        if (commandInputField == null)
        {
            Debug.LogError("UIInputController: TMP_InputFieldが設定されていません。");
            return;
        }
        if (targetInputs == null)
        {
            targetInputs = FindObjectOfType<StarterAssetsInputs>();
            if (targetInputs == null)
            {
                Debug.LogError("UIInputController: StarterAssetsInputsを持つキャラクターが見つかりません。");
                return;
            }
        }
        commandInputField.onEndEdit.AddListener(ProcessInput);
    }

    // InputFieldから受け取った文字列を処理するメソッド
    private void ProcessInput(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        string command = input.ToLower();

        // ★ 5秒後にコマンドを実行するコルーチンを開始する
        StartCoroutine(ExecuteCommandAfterDelay(command));

        // 入力後、InputFieldはすぐに空にする
        commandInputField.text = "";
        commandInputField.ActivateInputField();
    }

    // ★ 新しく追加したコルーチン
    // 5秒待ってから、受け取ったコマンドを実行する
    private IEnumerator ExecuteCommandAfterDelay(string command)
    {
        // ここで5秒間処理を待機する
        // Debug.Log(command + " というコマンドを5秒後に実行します...");
        yield return new WaitForSeconds(5f);
        // Debug.Log("コマンドを実行！");

        // 5秒後に実行される処理（以前ProcessInputにあったロジック）
        if (command.Contains("jump") || command.Contains("ジャンプ") || command.Contains("飛"))
        {
            StartCoroutine(ExecuteJump());
        }
        else if (command.Contains("forward") || command.Contains("前"))
        {
            targetInputs.MoveInput(new Vector2(0, 1));
        }
        else if (command.Contains("back") || command.Contains("後ろ"))
        {
            targetInputs.MoveInput(new Vector2(0, -1));
        }
        else if (command.Contains("left") || command.Contains("左"))
        {
            targetInputs.MoveInput(new Vector2(-1, 0));
        }
        else if (command.Contains("right") || command.Contains("右"))
        {
            targetInputs.MoveInput(new Vector2(1, 0));
        }
        else if (command.Contains("stop") || command.Contains("止"))
        {
            targetInputs.MoveInput(Vector2.zero);
            targetInputs.SprintInput(false);
        }
        else if (command.Contains("sprint on") || command.Contains("ダッシュ開") || command.Contains("走"))
        {
            targetInputs.SprintInput(true);
        }
        else if (command.Contains("sprint off") || command.Contains("ダッシュ止"))
        {
            targetInputs.SprintInput(false);
        }
        else
        {
            Debug.LogWarning("不明なコマンドです: " + command);
        }
    }

    private IEnumerator ExecuteJump()
    {
        targetInputs.JumpInput(true);
        yield return null;
        targetInputs.JumpInput(false);
    }

    private void OnDestroy()
    {
        if (commandInputField != null)
        {
            commandInputField.onEndEdit.RemoveListener(ProcessInput);
        }
    }
}