using UnityEngine;

public class CameraToggle : MonoBehaviour
{
    [Tooltip("カメラのON/OFFを切り替えるキー")]
    public KeyCode toggleKey = KeyCode.Z; // デフォルトは 'Z' キー

    private Camera mainCamera;

    // 3D世界を非表示にする（UIのみにする）ための設定
    private CameraClearFlags hiddenClearFlags = CameraClearFlags.SolidColor;
    private Color hiddenBackgroundColor = Color.black; // 背景を黒にする
    private int hiddenCullingMask = 0; // 0 = "Nothing" (何も描画しない)

    // 3D世界を再表示するための、元の設定
    private CameraClearFlags originalClearFlags;
    private int originalCullingMask;

    // 現在の状態 (true = 3D世界が見えている)
    private bool isWorldVisible = true;

    void Start()
    {
        mainCamera = Camera.main; 
        if (mainCamera == null)
        {
            Debug.LogError("MainCameraが見つかりません。'MainCamera' タグがカメラに設定されているか確認してください。");
            enabled = false; // スクリプト自体を無効化
            return;
        }

        // カメラの元の設定を保存
        originalClearFlags = mainCamera.clearFlags;
        originalCullingMask = mainCamera.cullingMask;
    }

    void Update()
    {
        if (mainCamera == null) return;

        // 切り替えキーが押された瞬間に
        if (Input.GetKeyDown(toggleKey))
        {
            // 状態を反転
            isWorldVisible = !isWorldVisible;

            if (isWorldVisible)
            {
                // --- 3D世界を「表示」する ---
                // カメラの設定を元の状態に戻す
                mainCamera.clearFlags = originalClearFlags;
                mainCamera.cullingMask = originalCullingMask;
            }
            else
            {
                // --- 3D世界を「非表示」にする (UIのみ) ---
                // カメラは有効(enabled = true)のまま、
                // 背景を黒の単色で塗りつぶし (SolidColor)、
                // 3Dオブジェクトを一切描画しない(cullingMask = 0)ようにする
                mainCamera.clearFlags = hiddenClearFlags;
                mainCamera.backgroundColor = hiddenBackgroundColor;
                mainCamera.cullingMask = hiddenCullingMask;
            }
        }
    }
}