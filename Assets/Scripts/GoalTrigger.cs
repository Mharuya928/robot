using UnityEngine;

public class GoalTrigger : MonoBehaviour
{
    [Tooltip("ゴール判定する対象のタグ（例: Player）")]
    public string targetTag = "Player";

    [Header("UI設定")]
    [Tooltip("ゴールした瞬間に表示する画像（またはパネル）のGameObject")]
    public GameObject goalImageObject; 

    private bool hasReachedGoal = false; 

    void Start()
    {
        // ゲーム開始時はゴール画像を隠しておく
        if (goalImageObject != null)
        {
            goalImageObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("GoalTrigger: Goal Image Object が設定されていません");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // まだゴールしておらず、入ってきたのが "Player" タグのオブジェクト（車）なら
        if (!hasReachedGoal && other.CompareTag(targetTag))
        {
            hasReachedGoal = true;
            Debug.Log("GOAL! " + other.name + " reached the goal.");

            // 画像を表示する
            if (goalImageObject != null)
            {
                goalImageObject.SetActive(true);
            }
        }
    }
}