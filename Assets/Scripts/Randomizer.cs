using UnityEngine;

public class Randomizer : MonoBehaviour
{
    [Header("対象設定")]
    [Tooltip("操作する車本体")]
    public Transform carTransform;

    [Tooltip("左側に置いた階段オブジェクト")]
    public GameObject stairsLeft;

    [Tooltip("右側に置いた階段オブジェクト")]
    public GameObject stairsRight;

    [Header("配置設定")]
    [Tooltip("レーンの幅（メートル）。障害物の位置に合わせて調整してください")]
    public float laneWidth = 1.0f;

    // スタート地点を記憶しておく変数
    private Vector3 initialCarPosition;
    private Quaternion initialCarRotation;

    void Start()
    {
        // 1. 車の初期位置を記憶
        if (carTransform != null)
        {
            initialCarPosition = carTransform.position;
            initialCarRotation = carTransform.rotation;
        }
        else
        {
            Debug.LogError("【エラー】ScenarioRandomizer: Car Transform が設定されていません！");
        }

        // 2. シャッフル実行
        Shuffle();
    }

    public void Shuffle()
    {
        // --- A. 障害物の配置を決定 (0:左, 1:右) ---
        int sideIndex = Random.Range(0, 2);
        string sideName = (sideIndex == 0) ? "Left" : "Right";

        // 障害物の表示切り替え
        if (sideIndex == 0) // 左
        {
            if (stairsLeft) stairsLeft.SetActive(true);
            if (stairsRight) stairsRight.SetActive(false);
        }
        else // 右
        {
            if (stairsLeft) stairsLeft.SetActive(false);
            if (stairsRight) stairsRight.SetActive(true);
        }

        // --- B. 車を同じ側に配置 ---
        if (carTransform != null)
        {
            // 物理挙動リセット（慣性を消す）
            Rigidbody rb = carTransform.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.Sleep();
            }

            // 位置計算
            Vector3 newPos = initialCarPosition;

            if (sideIndex == 0) // 左 (Left)
            {
                newPos.x -= laneWidth;
            }
            else // 右 (Right)
            {
                newPos.x += laneWidth;
            }

            // 適用
            carTransform.position = newPos;
            carTransform.rotation = initialCarRotation;
            
            if (rb != null) rb.WakeUp();
        }

        Debug.Log($"【シナリオ配置】Side: {sideName} (障害物あり・車も同じ位置)");
    }
}