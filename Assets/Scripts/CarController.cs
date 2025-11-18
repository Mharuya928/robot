using UnityEngine;
using TMPro;
using System.Collections.Generic;

// 車輪情報のクラス
[System.Serializable]
public class AxleInfo
{
    public WheelCollider leftWheel;
    public WheelCollider rightWheel;
    public bool motor;
    public bool steering;
}

public class CarController : MonoBehaviour
{
    [Header("Car Physics Settings")]
    public List<AxleInfo> axleInfos;
    public float maxMotorTorque;
    public float maxBrakeTorque;
    public float maxSteeringAngle;

    [Header("Sensor UI")]
    [SerializeField] private TMP_Text raycastText; // レイキャスト（距離）用
    [SerializeField] private TMP_Text triggerText; // トリガー（ゾーン）用

    // 内部変数
    private string lastRaycastTargetName = null;
    private LineRenderer gameViewRaycastLine;

    void Start()
    {
        // UIチェック
        if (raycastText == null) Debug.LogError("raycastText が設定されていません");
        if (triggerText == null) Debug.LogError("triggerText が設定されていません");

        // 初期化
        if (raycastText != null) raycastText.text = "Raycast: All clear.";
        if (triggerText != null) triggerText.text = "Trigger: No target.";

        InitializeLineRenderer();
        Debug.Log("Car Controller Initialized.");
    }

    void FixedUpdate()
    {
        // 車の移動処理
        float manualMotor = Input.GetAxis("Vertical");
        float manualSteering = Input.GetAxis("Horizontal");
        bool manualBrake = Input.GetKey(KeyCode.Space);

        Move(manualMotor, manualSteering, manualBrake);
        
        // 車輪の見た目を更新
        foreach (AxleInfo axleInfo in axleInfos)
        {
            ApplyLocalPositionToVisuals(axleInfo.leftWheel);
            ApplyLocalPositionToVisuals(axleInfo.rightWheel);
        }

        // センサー処理
        CheckRaycastSensor();
    }

    // ========== 公開メソッド (VLMから呼び出す用) ==========

    /// <summary>
    /// 写真撮影時にレイキャストの線を消すためのメソッド
    /// </summary>
    public void SetRaycastLineVisibility(bool isVisible)
    {
        if (gameViewRaycastLine != null)
        {
            gameViewRaycastLine.enabled = isVisible;
        }
    }

    // ========== 物理センサー (レイキャスト) ==========

    private void InitializeLineRenderer()
    {
        gameViewRaycastLine = gameObject.AddComponent<LineRenderer>();
        gameViewRaycastLine.positionCount = 2; 
        gameViewRaycastLine.startWidth = 0.05f; 
        gameViewRaycastLine.endWidth = 0.05f;
        gameViewRaycastLine.material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
        gameViewRaycastLine.startColor = Color.red;
        gameViewRaycastLine.endColor = Color.red;
    }
    
    private void CheckRaycastSensor()
    {
        RaycastHit hit;
        float maxDistance = 10.0f; 
        Vector3 rayOrigin = transform.position + new Vector3(0, 0.5f, 0); 

        // LineRenderer の始点を設定
        if(gameViewRaycastLine != null) gameViewRaycastLine.SetPosition(0, rayOrigin);

        if (Physics.Raycast(rayOrigin, transform.forward, out hit, maxDistance))
        {
            if (hit.distance < 3.0f)
            {
                if(raycastText) raycastText.text = $"Raycast: DANGER (object {hit.distance:F1} m)";
            }
            else 
            {
                if(raycastText) raycastText.text = $"Raycast: Warning (object {hit.distance:F1} m)";
            }

            if (lastRaycastTargetName != hit.collider.name)
            {
                Debug.Log($"Raycast Hit: {hit.collider.name} at {hit.distance:F1} m");
                lastRaycastTargetName = hit.collider.name;
            }

            // LineRenderer の終点を設定
            if(gameViewRaycastLine != null) gameViewRaycastLine.SetPosition(1, hit.point);
        }
        else
        {
            if(raycastText) raycastText.text = "Raycast: All clear.";

            if (lastRaycastTargetName != null)
            {
                Debug.Log($"Raycast Clear: {lastRaycastTargetName} is no longer in range.");
                lastRaycastTargetName = null;
            }

            // LineRenderer の終点を設定
            Vector3 endPoint = rayOrigin + transform.forward * maxDistance;
            if(gameViewRaycastLine != null) gameViewRaycastLine.SetPosition(1, endPoint);
        }
    }

    // ========== 物理センサー (トリガー) ==========

    void OnTriggerEnter(Collider other)
    {
        Debug.Log("Trigger Enter: " + other.name);
        if(triggerText) triggerText.text = $"Trigger: DANGER (object inside)";
    }

    void OnTriggerStay(Collider other)
    {
        if(triggerText) triggerText.text = $"Trigger: DANGER (object inside)";
    }

    void OnTriggerExit(Collider other)
    {
        Debug.Log("Trigger Exit: " + other.name);
        if(triggerText) triggerText.text = "Trigger: No target.";
    }

    // ========== 車両制御ロジック ==========

    public void Move(float motorInput, float steeringInput, bool isBraking)
    {
        float motor = maxMotorTorque * motorInput;
        float steering = maxSteeringAngle * steeringInput;
        float brakeTorque = isBraking ? maxBrakeTorque : 0f;

        foreach (AxleInfo axleInfo in axleInfos)
        {
            if (axleInfo.steering)
            {
                axleInfo.leftWheel.steerAngle = steering;
                axleInfo.rightWheel.steerAngle = steering;
            }
            if (axleInfo.motor)
            {
                axleInfo.leftWheel.motorTorque = isBraking ? 0f : motor;
                axleInfo.rightWheel.motorTorque = isBraking ? 0f : motor;
            }
            axleInfo.leftWheel.brakeTorque = brakeTorque;
            axleInfo.rightWheel.brakeTorque = brakeTorque;
        }
    }

    public void ApplyLocalPositionToVisuals(WheelCollider collider)
    {
        if (collider.transform.childCount == 0) return;
        
        Transform visualWheel = collider.transform.GetChild(0);
        collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
        visualWheel.transform.position = position;
        visualWheel.transform.rotation = rotation * Quaternion.Euler(0f, 0f, 90f); 
    }
}