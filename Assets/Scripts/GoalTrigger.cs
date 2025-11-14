using UnityEngine;
using TMPro; // TMP_Textを使うため

public class GoalTrigger : MonoBehaviour
{
    [Tooltip("ゴール判定する対象のタグ（例: Player）")]
    public string targetTag = "Player"; // Unityで車に "Player" タグを付けてください

    [Tooltip("結果を表示するUIテキスト")]
    public TMP_Text resultText; // InspectorでVLMTextなどを割り当てる

    private bool hasReachedGoal = false; // 2重判定を防ぐフラグ

    // このトリガー（GoalZone）に他のコライダーが入った瞬間に呼ばれる
    void OnTriggerEnter(Collider other)
    {
        // まだゴールしておらず、入ってきたのが "Player" タグのオブジェクト（車）なら
        if (!hasReachedGoal && other.CompareTag(targetTag))
        {
            // ゴール！
            hasReachedGoal = true;
            Debug.Log("GOAL " + other.name + " has reached the goal.");

            // UIテキストにゴールを表示
            if (resultText != null)
            {
                resultText.text = "GOAL";
            }
            
            // （オプション）ここでシミュレーションを停止したり、次のレベルに進む処理を呼ぶ
            // Time.timeScale = 0f; // 時間を停止
        }
    }
}