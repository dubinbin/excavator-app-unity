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

    void FixedUpdate()
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