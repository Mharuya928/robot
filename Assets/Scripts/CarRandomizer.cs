using UnityEngine;

public class CarRandomizer : MonoBehaviour
{
    [Header("車のランダム配置設定")]
    [Tooltip("操作する車本体（CarControllerがついているオブジェクト）")]
    public Transform carTransform;

    [Tooltip("隣り合うレーンの間隔（メートル）。")]
    public float laneWidth = 1.0f; // 5分割なので、少し狭め(1.0など)に調整すると良いかもしれません

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

        // 3. 5択をランダムに決める (0〜4)
        int laneIndex = Random.Range(0, 5);
        string laneName = "Center";

        switch (laneIndex)
        {
            case 0: // 左端 (Far Left)
                newPos.x -= (laneWidth * 2);
                laneName = "Far Left";
                break;
            case 1: // 左 (Left)
                newPos.x -= laneWidth;
                laneName = "Left";
                break;
            case 2: // 真ん中 (Center)
                // xはそのまま
                laneName = "Center";
                break;
            case 3: // 右 (Right)
                newPos.x += laneWidth;
                laneName = "Right";
                break;
            case 4: // 右端 (Far Right)
                newPos.x += (laneWidth * 2);
                laneName = "Far Right";
                break;
        }

        // 4. 位置と回転を適用
        carTransform.position = newPos;
        carTransform.rotation = initialRotation;

        if (rb != null) rb.WakeUp();

        Debug.Log($"【車配置】{laneName} (X: {newPos.x:F2})");
    }
}