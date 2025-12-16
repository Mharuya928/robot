using UnityEngine;

public class CarRandomizer : MonoBehaviour
{
    [Header("車のランダム配置設定")]
    [Tooltip("操作する車本体（CarControllerがついているオブジェクト）")]
    public Transform carTransform;

    [Tooltip("レーンの幅（メートル）。例: 1.5 にすると「左(-1.5) / 真ん中(0) / 右(+1.5)」になります")]
    public float laneWidth = 1.5f;

    // スタート地点を記憶しておく変数
    private Vector3 initialPosition;
    private Quaternion initialRotation;

    void Start()
    {
        if (carTransform != null)
        {
            initialPosition = carTransform.position;
            initialRotation = carTransform.rotation;
        }
        else
        {
            Debug.LogError("【エラー】CarRandomizer: Car Transform が設定されていません！");
        }

        Shuffle();
    }

    public void Shuffle()
    {
        if (carTransform == null) return;

        // 1. 物理挙動（スピード）をリセット
        Rigidbody rb = carTransform.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
        }

        // 2. 基準位置を用意
        Vector3 newPos = initialPosition;

        // 3. 「左・中・右」の3択をランダムに決める
        // Random.Range(int min, int max) は maxを含まないので 0, 1, 2 が出る
        int laneIndex = Random.Range(0, 3);
        string laneName = "Center";

        switch (laneIndex)
        {
            case 0: // 左
                newPos.x -= laneWidth;
                laneName = "Left";
                break;
            case 1: // 真ん中
                // xはそのまま (0)
                laneName = "Center";
                break;
            case 2: // 右
                newPos.x += laneWidth;
                laneName = "Right";
                break;
        }

        // 4. 位置と回転を適用
        carTransform.position = newPos;
        carTransform.rotation = initialRotation;

        if (rb != null) rb.WakeUp();

        Debug.Log($"【車配置】{laneName} (X: {newPos.x:F2})");
    }
}