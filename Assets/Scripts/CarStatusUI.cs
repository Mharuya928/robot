using UnityEngine;
using UnityEngine.UI;

public class CarStatusUI : MonoBehaviour
{
    [Header("Target Car")]
    public CarController targetCar;

    [Header("UI Elements (Auto Assigned)")]
    [SerializeField] private RectTransform wheelFL;
    [SerializeField] private RectTransform wheelFR;
    [SerializeField] private RectTransform wheelRL;
    [SerializeField] private RectTransform wheelRR;
    
    [SerializeField] private Slider motorSlider; 

    [Header("Visualization Settings")]
    public float steeringMultiplier = -1.0f; 
    public TMPro.TextMeshProUGUI speedText;

    void Start()
    {
        if (targetCar == null)
        {
            targetCar = FindObjectOfType<CarController>();
            if (targetCar == null) Debug.LogError("CarController がシーン内に見つかりません！");
        }

        // --- UI要素の自動取得 ---
        
        // MotorSlider の取得
        Transform sliderTr = transform.Find("MotorSlider");
        if (sliderTr != null)
        {
            motorSlider = sliderTr.GetComponent<Slider>();
        }
        else
        {
            Debug.LogWarning("'MotorSlider' が見つかりません。名前を確認してください。");
        }

        // SpeedText の取得
        Transform speedTr = transform.Find("SpeedText");
        if (speedTr != null)
        {
            speedText = speedTr.GetComponent<TMPro.TextMeshProUGUI>();
        }
        else
        {
            Debug.LogWarning("'SpeedText' が見つかりません。名前を確認してください。");
        }

        // ▼▼▼ 修正: タイヤの取得パスを変更 ▼▼▼
        Transform fl = transform.Find("Wheels/FrontLeft");
        Transform fr = transform.Find("Wheels/FrontRight");
        Transform rl = transform.Find("Wheels/BackLeft");
        Transform rr = transform.Find("Wheels/BackRight");

        // 取得できたかチェックして代入
        if (fl != null) wheelFL = fl.GetComponent<RectTransform>();
        else Debug.LogError("'Wheels/FrontLeft' が見つかりません。");

        if (fr != null) wheelFR = fr.GetComponent<RectTransform>();
        else Debug.LogError("'Wheels/FrontRight' が見つかりません。");

        if (rl != null) wheelRL = rl.GetComponent<RectTransform>();
        else Debug.LogError("'Wheels/BackLeft' が見つかりません。");

        if (rr != null) wheelRR = rr.GetComponent<RectTransform>();
        else Debug.LogError("'Wheels/BackRight' が見つかりません。");
    }

    void Update()
    {
        if (targetCar == null) return;

        // 1. ステアリング同期
        float currentSteerAngle = 0f;
        if (targetCar.axleInfos.Count > 0)
        {
            currentSteerAngle = targetCar.axleInfos[0].leftWheel.steerAngle;
        }
        ApplyWheelRotation(wheelFL, currentSteerAngle);
        ApplyWheelRotation(wheelFR, currentSteerAngle);
        ApplyWheelRotation(wheelRL, 0);
        ApplyWheelRotation(wheelRR, 0);


        // 2. アクセル/ブレーキをスライダーで表現
        float motorInput = Input.GetAxis("Vertical"); // -1 (バック) 〜 1 (前進)
        bool isBraking = Input.GetKey(KeyCode.Space);

        if (motorSlider != null)
        {
            if (isBraking)
            {
                // ブレーキ時は 0 (真ん中) にする
                motorSlider.value = 0.0f; 
            }
            else
            {
                // アクセル開度をそのまま適用
                motorSlider.value = motorInput;
            }
        }

        if(targetCar != null && speedText != null)
        {
            // 3. 現在の速度を表示
            if (speedText != null)
            {
                // 速度ベクトル(m/s)の長さを取得し、3.6倍して km/h に変換
                float speedKmh = targetCar.GetComponent<Rigidbody>().velocity.magnitude * 3.6f;
                
                // 小数点以下0桁（整数）で表示
                speedText.text = $"{speedKmh:F0} km/h";
            }
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