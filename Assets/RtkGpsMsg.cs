using System;

[Serializable]
public class RtkGpsMsg
{
    public double timestamp;
    public RtkStatus rtk_status;
    public RtkPosition position;
    public RtkRotation rotation;
    public RtkVelocity velocity;
}

[Serializable]
public class RtkStatus
{
    public string fix_type;
    public int satellites_used;
    public float age;
    public RtkAccuracy accuracy;
}

[Serializable]
public class RtkAccuracy
{
    public float horizontal;
    public float vertical;
}

[Serializable]
public class RtkPosition
{
    public RtkGlobal global;
    public RtkRelative relative;
}

[Serializable]
public class RtkGlobal
{
    public double latitude;
    public double longitude;
    public float altitude;
}

[Serializable]
public class RtkRelative
{
    public RtkTranslation translation;
}

[Serializable]
public class RtkTranslation
{
    public float x;
    public float y;
    public float z;
}

[Serializable]
public class RtkRotation
{
    public float x;
    public float y;
    public float z;
    public float w;
}

[Serializable]
public class RtkVelocity
{
    public float x;
    public float y;
    public float z;
}
