using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GizmoVisualizer : MonoBehaviour
{
    [SerializeField]
    private LineRenderer x, y, z;
    private RotationInteractor rotationInteractor;

    private Vector3 pos;
    private Quaternion rot;
    private float length = 0.1f;
    // Start is called before the first frame update
    void Start()
    {

    }

    void Update()
    {

    }

    // Update is called once per frame
    void LateUpdate()
    {
        rotationInteractor.GetTriangleTransform(out pos, out rot);
        // rotationInteractor.GetTriangleTransformRaw(out Vector3 f, out Vector3 u);
        DrawAxis(x, pos, rot * Vector3.right);
        DrawAxis(y, pos, rot * Vector3.up);
        DrawAxis(z, pos, rot * Vector3.forward);
        // DrawAxis(z, pos, f);
        // DrawAxis(y, pos, u);
    }

    void DrawAxis(LineRenderer lineRenderer, Vector3 p, Vector3 d)
    {
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, p);
        lineRenderer.SetPosition(1, p + d * length);
    }

    public void SetRotationInteractor(RotationInteractor r)
    {
        rotationInteractor = r;
    }
}
