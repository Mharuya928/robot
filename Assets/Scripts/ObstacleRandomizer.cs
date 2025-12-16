using UnityEngine;

public class ObstacleRandomizer : MonoBehaviour
{
    [Header("配置する障害物")]
    [Tooltip("左側に置いた階段オブジェクト")]
    public GameObject stairsLeft;

    [Tooltip("右側に置いた階段オブジェクト")]
    public GameObject stairsRight;

    void Start()
    {
        // ゲーム開始時に一度ランダム化する
        Shuffle();
    }

    // どちらか片方を有効にするメソッド
    public void Shuffle()
    {
        // 0か1の乱数を生成 (0:左, 1:右)
        int coinFlip = Random.Range(0, 2);

        if (coinFlip == 0)
        {
            // 左をオン、右をオフ
            if(stairsLeft) stairsLeft.SetActive(true);
            if(stairsRight) stairsRight.SetActive(false);
            Debug.Log("Pattern: Left Obstacle (Avoid Right)");
        }
        else
        {
            // 右をオン、左をオフ
            if(stairsRight) stairsRight.SetActive(true);
            if(stairsLeft) stairsLeft.SetActive(false);
            Debug.Log("Pattern: Right Obstacle (Avoid Left)");
        }
    }
}