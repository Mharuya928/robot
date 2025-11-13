using UnityEngine;

// Rigidbody がアタッチされていることを前提とします
[RequireComponent(typeof(Rigidbody))]
public class CenterOfMassVisualizer : MonoBehaviour
{
    // ギズモ（目印）のサイズと色
    public float gizmoRadius = 0.1f;
    public Color gizmoColor = Color.cyan;

    private Rigidbody rb;

    void Start()
    {
        // 実行時にRigidbodyへの参照を取得
        rb = GetComponent<Rigidbody>();
    }

    // Sceneビューにギズモを描画する特別な関数
    void OnDrawGizmos()
    {
        // Rigidbodyの参照がない場合は取得を試みる
        // (Unityエディタが非再生モードの時にも機能させるため)
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null) return; // Rigidbodyが見つからない場合は何もしない
        }

        // --- 重心の位置を計算 ---
        // rb.centerOfMass は「ローカル座標（オブジェクト基準）」です。
        // ギズモを描画するには「ワールド座標（世界基準）」に変換する必要があります。
        Vector3 worldCoM = transform.TransformPoint(rb.centerOfMass);

        // --- ギズモを描画 ---
        Gizmos.color = gizmoColor;
        Gizmos.DrawSphere(worldCoM, gizmoRadius);
    }
}