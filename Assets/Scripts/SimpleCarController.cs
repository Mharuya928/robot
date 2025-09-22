
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CarController : MonoBehaviour {

	public List<AxleInfo> axleInfos; // 個々の車軸の情報
	public float maxMotorTorque; // ホイールの最大トルク
	public float maxSteeringAngle; // ホイールのハンドル最大角度

	public void ApplyLocalPositionToVisuals(WheelCollider collider)
	{
		if (collider.transform.childCount == 0) {
			return;
		}

		Transform visualWheel = collider.transform.GetChild (0);

		// コライダーの位置と回転を取得。
		Vector3 position;
		Quaternion rotation;
		collider.GetWorldPose (out position, out rotation);


		visualWheel.transform.position = position;
		/**********************
		// タイヤにシリンダーを使ってる場合、Z軸を９０度回転させないとタイヤが横向きになってしまうためZ軸に９０度回転を常に加える
		// タイヤように作られた３Dモデルの場合 * Quaternion.Euler (0f, 0f, 90f) の部分は必要ない
		***********************/
		visualWheel.transform.rotation = rotation * Quaternion.Euler (0f, 0f, 90f);
	}

	// Use this for initialization
	void Start () {
		Debug.Log ("CarStart");
	}
	
	// Update is called once per frame
	void Update () {
		
	}
	public void FixedUpdate()
	{
		Debug.Log ("CarFUpdate");
		float motor = maxMotorTorque * Input.GetAxis("Vertical");
		float steering = maxSteeringAngle * Input.GetAxis("Horizontal");

		foreach (AxleInfo axleInfo in axleInfos) {
			if (axleInfo.steering) {
				axleInfo.leftWheel.steerAngle = steering;
				axleInfo.rightWheel.steerAngle = steering;
			}
			if (axleInfo.motor) {
				axleInfo.leftWheel.motorTorque = motor;
				axleInfo.rightWheel.motorTorque = motor;
			}
			ApplyLocalPositionToVisuals(axleInfo.leftWheel);
			ApplyLocalPositionToVisuals(axleInfo.rightWheel);
		}
	}

}

[System.Serializable]
	public class AxleInfo {
	public WheelCollider leftWheel;
	public WheelCollider rightWheel;
	public bool motor; // このホイールはモーターにアタッチされているかどうか
	public bool steering; // このホイールはハンドルの角度を反映しているかどうか
}