using UnityEngine;

public class ExcavatorController : MonoBehaviour
{
    [Header("关节引用")]
    public ArticulationBody cabin;
    public ArticulationBody boom;
    public ArticulationBody stick;
    public ArticulationBody bucket;

    [Header("速度（度/秒）")]
    public float cabinSpeed = 30f;
    public float boomSpeed = 20f;
    public float stickSpeed = 25f;
    public float bucketSpeed = 40f;

    [Header("驱动参数")]
    public float holdStiffness = 100000f;
    public float holdDamping = 10000f;
    public float moveDamping = 50000f;
    public float forceLimit = 99999999f;

    [Header("MQTT 控制")]
    [Tooltip("启用后 MQTT 角度指令优先于键盘输入")]
    public bool mqttControlEnabled = false;

    [Header("RTK 位姿")]
    [Tooltip("真实世界 1米 = Unity 多少单位（挖掘机约 10m 长，建议先用 1:1）")]
    public float worldScale = 1f;

    [Tooltip("位移插值速度，越大跟随越快")]
    public float positionLerpSpeed = 8f;

    [Tooltip("旋转插值速度，越大跟随越快")]
    public float rotationLerpSpeed = 8f;

    private float _targetCabin;
    private float _targetBoom;
    private float _targetStick;
    private float _targetBucket;

    private Vector3 _rtkTargetPos;
    private Quaternion _rtkTargetRot;
    private bool _rtkReady;

    // 挖掘机的根 ArticulationBody（base link），
    // 位姿通过 TeleportRoot 设置
    private ArticulationBody _rootBody;

    void Awake()
    {
        _rootBody = GetComponent<ArticulationBody>();
        _rtkTargetPos = transform.position;
        _rtkTargetRot = transform.rotation;
    }

    // ── 关节控制 API ─────────────────────────────────────────

    /// <summary>
    /// 由 MqttManager 调用，传入各关节相对前一关节的目标角度（度）。
    /// 调用后自动切换为 MQTT 控制模式。
    /// </summary>
    public void ApplyJointControl(float cabinAngle, float boomAngle, float stickAngle, float bucketAngle)
    {
        _targetCabin = cabinAngle;
        _targetBoom = boomAngle;
        _targetStick = stickAngle;
        _targetBucket = bucketAngle;
        mqttControlEnabled = true;
    }

    // ── RTK 位姿 API ────────────────────────────────────────

    /// <summary>
    /// 由 MqttManager 调用，传入 RTK 相对位移（米）和 ENU 四元数。
    /// RTK 坐标系 ENU: x=东, y=北, z=上
    /// Unity 坐标系:     x=右, y=上, z=前
    /// 转换: Unity.x = ENU.x, Unity.y = ENU.z, Unity.z = ENU.y
    /// </summary>
    public void ApplyRtkPose(RtkTranslation translation, RtkRotation rotation)
    {
        if (translation != null)
        {
            _rtkTargetPos = new Vector3(
                translation.x * worldScale,
                translation.z * worldScale,
                translation.y * worldScale
            );
        }

        if (rotation != null)
        {
            var enu = new Quaternion(rotation.x, rotation.y, rotation.z, rotation.w);
            _rtkTargetRot = EnuToUnity(enu);
        }

        _rtkReady = true;
    }

    // ── FixedUpdate ─────────────────────────────────────────

    void FixedUpdate()
    {
        // 关节驱动
        if (mqttControlEnabled)
        {
            DriveToAngle(cabin, _targetCabin);
            DriveToAngle(boom, _targetBoom);
            DriveToAngle(stick, _targetStick);
            DriveToAngle(bucket, _targetBucket);
        }
        else
        {
            float cabinInput = 0f;
            if (Input.GetKey(KeyCode.A)) cabinInput = cabinSpeed;
            if (Input.GetKey(KeyCode.D)) cabinInput = -cabinSpeed;
            Drive(cabin, cabinInput);

            float boomInput = 0f;
            if (Input.GetKey(KeyCode.W)) boomInput = boomSpeed;
            if (Input.GetKey(KeyCode.S)) boomInput = -boomSpeed;
            Drive(boom, boomInput);

            float stickInput = 0f;
            if (Input.GetKey(KeyCode.UpArrow)) stickInput = -stickSpeed;
            if (Input.GetKey(KeyCode.DownArrow)) stickInput = stickSpeed;
            Drive(stick, stickInput);

            float bucketInput = 0f;
            if (Input.GetKey(KeyCode.LeftArrow)) bucketInput = -bucketSpeed;
            if (Input.GetKey(KeyCode.RightArrow)) bucketInput = bucketSpeed;
            Drive(bucket, bucketInput);
        }

        // RTK 底盘位姿
        if (_rtkReady)
            ApplyRtkPoseSmooth();
    }

    // ── RTK 位姿平滑 ────────────────────────────────────────

    private void ApplyRtkPoseSmooth()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 pos = Vector3.Lerp(transform.position, _rtkTargetPos, positionLerpSpeed * dt);
        Quaternion rot = Quaternion.Slerp(transform.rotation, _rtkTargetRot, rotationLerpSpeed * dt);

        if (_rootBody != null)
        {
            _rootBody.TeleportRoot(pos, rot);
        }
        else
        {
            transform.SetPositionAndRotation(pos, rot);
        }
    }

    /// <summary>
    /// ENU 四元数 → Unity 四元数。
    /// ENU(x=东,y=北,z=上) → Unity(x=右,y=上,z=前)
    /// 交换 y↔z 并翻转手性（左手系）。
    /// </summary>
    private static Quaternion EnuToUnity(Quaternion enu)
    {
        return new Quaternion(enu.x, enu.z, enu.y, -enu.w);
    }

    /// <summary>以目标位置模式驱动关节到指定角度（度）。</summary>
    void DriveToAngle(ArticulationBody body, float targetAngleDeg)
    {
        var drive = body.xDrive;
        drive.driveType = ArticulationDriveType.Target;
        drive.target = targetAngleDeg;
        drive.stiffness = holdStiffness;
        drive.damping = holdDamping;
        drive.forceLimit = forceLimit;
        body.xDrive = drive;
    }

    /// <summary>以速度模式驱动关节；速度为 0 时锁定当前位置。</summary>
    void Drive(ArticulationBody body, float velocity)
    {
        var drive = body.xDrive;
        drive.forceLimit = forceLimit;

        if (Mathf.Approximately(velocity, 0f))
        {
            drive.driveType = ArticulationDriveType.Target;
            drive.target = body.jointPosition[0] * Mathf.Rad2Deg;
            drive.stiffness = holdStiffness;
            drive.damping = holdDamping;
        }
        else
        {
            drive.driveType = ArticulationDriveType.Velocity;
            drive.targetVelocity = velocity;
            drive.stiffness = 0f;
            drive.damping = moveDamping;
        }

        body.xDrive = drive;
    }
}