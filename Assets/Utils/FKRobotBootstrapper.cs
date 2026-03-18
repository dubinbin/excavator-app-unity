using System.Collections;
using UnityEngine;
using Unity.Robotics.UrdfImporter.Control;

/// <summary>
/// Workaround for URDF Importer FKRobot NullReferenceException:
/// FKRobot initializes dh/jointChain in Start(), but FixedUpdate() can run before Start().
/// This script disables FKRobot on Awake and re-enables it after a short delay.
/// </summary>
public class FKRobotBootstrapper : MonoBehaviour
{
    public bool includeInactive = true;

    [Range(1, 10)]
    public int enableAfterFrames = 1;

    FKRobot[] _fkRobots;

    void Awake()
    {
        _fkRobots = GetComponentsInChildren<FKRobot>(includeInactive);
        foreach (var fk in _fkRobots)
        {
            if (fk != null)
                fk.enabled = false;
        }
    }

    void OnEnable()
    {
        StartCoroutine(EnableLater());
    }

    IEnumerator EnableLater()
    {
        for (int i = 0; i < enableAfterFrames; i++)
            yield return null;

        foreach (var fk in _fkRobots)
        {
            if (fk != null)
                fk.enabled = true;
        }
    }
}

