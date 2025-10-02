using UnityEngine;
using TMPro;
using LLMUnity;

public class AIController : MonoBehaviour
{
    public TMP_InputField inputField;
    public CarController carController;
    public LLMCharacter llmCharacter;

    private void Start()
    {
        // LLMCharacterの初期設定
        llmCharacter.SetPrompt("あなたは車輪ロボットのAIです。ユーザーの指示に従って動作してください。");

        // TMP_InputFieldのonEndEditイベントにリスナーを追加
        inputField.onEndEdit.AddListener(OnInputFieldEndEdit);
    }

    private void OnInputFieldEndEdit(string userInput)
    {
        // ユーザーが入力したテキストをLLMに送信
        llmCharacter.Chat(userInput, HandleLLMResponse);
    }

    private void HandleLLMResponse(string response)
    {
        // LLMの応答に基づいてロボットの動作を制御
        if (response.Contains("前進"))
        {
            carController.SetInput(1, 0);
        }
        else if (response.Contains("後退"))
        {
            carController.SetInput(-1, 0);
        }
        else if (response.Contains("右"))
        {
            carController.SetInput(0, 1);
        }
        else if (response.Contains("左"))
        {
            carController.SetInput(0, -1);
        }
        else
        {
            Debug.Log("無効なコマンド: " + response);
        }
    }
}
