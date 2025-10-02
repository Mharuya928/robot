
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class CarController : MonoBehaviour
{

	public List<AxleInfo> axleInfos; // 個々の車軸の情報
	public float maxMotorTorque; // ホイールの最大トルク
	public float maxSteeringAngle; // ホイールのハンドル最大角度
	public TMP_InputField inputField;
	private bool isInputFieldFocused = false;

	void Start()
	{
		// InputFieldのフォーカス状態を監視
		inputField.onSelect.AddListener((string text) => isInputFieldFocused = true);
		inputField.onDeselect.AddListener((string text) => isInputFieldFocused = false);
		// 入力終了時の処理を設定
		inputField.onEndEdit.AddListener(OnEndEdit);
		Debug.Log("CarStart");
	}

	private void OnEndEdit(string value)
	{
		// Enterキーが押された場合のみ無効化を解除
		if (Input.GetKeyDown(KeyCode.Return))
		{
			// フォーカスを解除
			EventSystem.current.SetSelectedGameObject(null);
			isInputFieldFocused = false;
		}
	}
	public void ApplyLocalPositionToVisuals(WheelCollider collider)
	{
		if (collider.transform.childCount == 0)
		{
			return;
		}

		Transform visualWheel = collider.transform.GetChild(0);

		// コライダーの位置と回転を取得。
		Vector3 position;
		Quaternion rotation;
		collider.GetWorldPose(out position, out rotation);

		visualWheel.transform.position = position;
		/**********************
		// タイヤにシリンダーを使ってる場合、Z軸を９０度回転させないとタイヤが横向きになってしまうためZ軸に９０度回転を常に加える
		// タイヤように作られた３Dモデルの場合 * Quaternion.Euler (0f, 0f, 90f) の部分は必要ない
		***********************/
		visualWheel.transform.rotation = rotation * Quaternion.Euler(0f, 0f, 90f);
	}

	// Update is called once per frame
	void Update()
	{

	}
	public void FixedUpdate()
	{
		if (!isInputFieldFocused)
		{
			Debug.Log("CarFUpdate");
			float motor = maxMotorTorque * Input.GetAxis("Vertical");
			float steering = maxSteeringAngle * Input.GetAxis("Horizontal");

			foreach (AxleInfo axleInfo in axleInfos)
			{
				if (axleInfo.steering)
				{
					axleInfo.leftWheel.steerAngle = steering;
					axleInfo.rightWheel.steerAngle = steering;
				}
				if (axleInfo.motor)
				{
					axleInfo.leftWheel.motorTorque = motor;
					axleInfo.rightWheel.motorTorque = motor;
				}
				ApplyLocalPositionToVisuals(axleInfo.leftWheel);
				ApplyLocalPositionToVisuals(axleInfo.rightWheel);
			}
		}

	}


public void SetInput(float motorInput, float steeringInput)
{
    // 入力を設定
    Input.SetAxis("Vertical", motorInput);
    Input.SetAxis("Horizontal", steeringInput);
}


[System.Serializable]
	public class AxleInfo {
	public WheelCollider leftWheel;
	public WheelCollider rightWheel;
	public bool motor; // このホイールはモーターにアタッチされているかどうか
	public bool steering; // このホイールはハンドルの角度を反映しているかどうか
}