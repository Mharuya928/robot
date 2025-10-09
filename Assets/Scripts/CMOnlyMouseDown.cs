using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;

public class CMFreelookOnlyWhenRightMouseDown : MonoBehaviour {
    [Tooltip("リセット対象の VirtualCamera")]
    public CinemachineVirtualCamera vcam;

    // 初期値を保存しておくためのデータ
    private Vector3 initPosition;
    private Quaternion initRotation;

    // さらに、Transposer や POV の内部パラメータも戻したいならそれらも保存
    private Vector3 initTransposerOffset;
    private float initPOV_Yaw;
    private float initPOV_Pitch;

    private CinemachineTransposer transposer;
    private CinemachinePOV pov;

    public KeyCode resetKey = KeyCode.Mouse2; // デフォルトはマウスの中ボタン

    void Awake()
    {
        if (vcam == null)
        {
            Debug.LogError("vcam が設定されていません");
            enabled = false;
            return;
        }

        // Body モジュールが Transposer として使われていることを仮定
        transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
        if (transposer == null)
        {
            Debug.LogError("vcam に Transposer が設定されていません");
        }

        // Aim モジュールが POV として使われていることを仮定
        pov = vcam.GetCinemachineComponent<CinemachinePOV>();
        if (pov == null)
        {
            Debug.LogError("vcam に POV が設定されていません");
        }
    }
    void Start()
    {
        CinemachineCore.GetInputAxis = GetAxisCustom;

        // Transform の初期値を保存
        initPosition = vcam.transform.position;
        initRotation = vcam.transform.rotation;

        if (transposer != null)
        {
            initTransposerOffset = transposer.m_FollowOffset;
        }
        if (pov != null)
        {
            initPOV_Yaw = pov.m_HorizontalAxis.Value;
            initPOV_Pitch = pov.m_VerticalAxis.Value;
        }
    }

    void Update()
    {
        // マウス中ボタン（通常は MouseButton 2）を押したら
        if (Input.GetKeyDown(resetKey))
        {
            ResetCameraTransform();
        }
    }

    void ResetCameraTransform()
    {
        // まず Transform を戻す
        vcam.transform.position = initPosition;
        vcam.transform.rotation = initRotation;

        // Transposer のオフセットを戻す
        if (transposer != null)
        {
            transposer.m_FollowOffset = initTransposerOffset;
        }

        // POV のYaw / Pitch を戻す
        if (pov != null)
        {
            pov.m_HorizontalAxis.Value = initPOV_Yaw;
            pov.m_VerticalAxis.Value = initPOV_Pitch;
        }

    }
    
    public float GetAxisCustom(string axisName){
        if(axisName == "Mouse X"){
            if (Input.GetMouseButton(0)){
                return UnityEngine.Input.GetAxis("Mouse X");
            } else{
                return 0;
            }
        }
        else if (axisName == "Mouse Y"){
            if (Input.GetMouseButton(0)){
                return UnityEngine.Input.GetAxis("Mouse Y");
            } else{
                return 0;
            }
        }
        return UnityEngine.Input.GetAxis(axisName);
    }
}