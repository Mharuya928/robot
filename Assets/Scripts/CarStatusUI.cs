using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CarStatusUI : MonoBehaviour
{
    [Header("Target Car")]
    public CarController targetCar;

    [Header("UI Containers")]
    [Tooltip("車体とタイヤをまとめた親オブジェクト（回転用）")]
    public RectTransform carContainer; 

    [Header("UI Elements (Auto Assigned)")]
    [SerializeField] private RectTransform wheelFL;
    [SerializeField] private RectTransform wheelFR;
    [SerializeField] private RectTransform wheelRL;
    [SerializeField] private RectTransform wheelRR;

    [SerializeField] private Slider motorSlider;

    [Header("Visualization Settings")]
    public float steeringMultiplier = -1.0f;
    
    [Header("Text Displays")]
    public TextMeshProUGUI speedText;
    // headingText は削除しました

    void Start()
    {
        if (targetCar == null)
        {
            targetCar = FindObjectOfType<CarController>();
            if (targetCar == null) Debug.LogError("CarController がシーン内に見つかりません！");
        }

        // --- UI要素の自動取得 ---

        // コンテナの自動取得
        if (carContainer == null)
        {
            Transform containerTr = transform.Find("CarVisContainer");
            if (containerTr == null) containerTr = transform.Find("CarContainer");
            
            if (containerTr != null) carContainer = containerTr.GetComponent<RectTransform>();
            else Debug.LogWarning("回転させるための 'CarVisContainer' が見つかりません。");
        }

        // MotorSlider の取得
        Transform sliderTr = transform.Find("MotorSlider");
        if (sliderTr != null) motorSlider = sliderTr.GetComponent<Slider>();

        // SpeedText の取得
        Transform speedTr = transform.Find("SpeedText");
        if (speedTr != null) speedText = speedTr.GetComponent<TextMeshProUGUI>();

        // タイヤの取得 (コンテナ内検索対応)
        wheelFL = FindWheel("FrontLeft");
        wheelFR = FindWheel("FrontRight");
        wheelRL = FindWheel("BackLeft");
        wheelRR = FindWheel("BackRight");
    }

    RectTransform FindWheel(string wheelName)
    {
        Transform t = transform.Find($"Wheels/{wheelName}");
        if (t == null) t = transform.Find($"CarVisContainer/Wheels/{wheelName}");
        if (t == null && carContainer != null) t = carContainer.Find($"Wheels/{wheelName}");

        if (t != null) return t.GetComponent<RectTransform>();
        
        Debug.LogError($"'{wheelName}' が見つかりません。階層を確認してください。");
        return null;
    }

    void Update()
    {
        if (targetCar == null) return;

        // 1. 車体全体の回転（オドメトリ反映）
        if (carContainer != null)
        {
            // 車のY回転（0=北, 90=東）を取得
            float carYaw = targetCar.transform.eulerAngles.y;
            
            // UIではZ軸回転。マイナスをかけて方向を合わせる
            carContainer.localRotation = Quaternion.Euler(0, 0, -carYaw);
        }

        // 2. ステアリング同期
        float currentSteerAngle = 0f;
        if (targetCar.axleInfos.Count > 0)
        {
            currentSteerAngle = targetCar.axleInfos[0].leftWheel.steerAngle;
        }
        ApplyWheelRotation(wheelFL, currentSteerAngle);
        ApplyWheelRotation(wheelFR, currentSteerAngle);
        ApplyWheelRotation(wheelRL, 0);
        ApplyWheelRotation(wheelRR, 0);

        // 3. アクセル/ブレーキ表示
        float motorInput = Input.GetAxis("Vertical");
        bool isBraking = Input.GetKey(KeyCode.Space);

        if (motorSlider != null)
        {
            if (isBraking) motorSlider.value = -1.0f;
            else motorSlider.value = motorInput;
        }

        // 4. 速度表示
        if (speedText != null)
        {
            float speedMs = targetCar.GetComponent<Rigidbody>().velocity.magnitude;
            speedText.text = $"{speedMs:F1} m/s";
        }
    }

    void ApplyWheelRotation(RectTransform wheel, float angle)
    {
        if (wheel != null)
        {
            wheel.localRotation = Quaternion.Euler(0, 0, angle * steeringMultiplier);
        }
    }
}