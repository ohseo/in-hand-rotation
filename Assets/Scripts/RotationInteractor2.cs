using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using TMPro;

public class RotationInteractor2 : MonoBehaviour
{
    [SerializeField]
    private bool isComponentsVisible = true;
    [SerializeField]
    private bool isLeftHanded = false;
    [SerializeField]
    private OVRSkeleton ovrRightSkeleton, ovrLeftSkeleton;
    private OVRSkeleton ovrSkeleton;
    private OVRBone indexTipBone, middleTipBone, thumbTipBone, thumbMetacarpal, wristBone;

    [SerializeField]
    private GameObject cube;
    private float cubeScale = 0.03f;

    private List<GameObject> spheres = new List<GameObject>();
    private GameObject thumbSphere, indexSphere, middleSphere;
    private float sphereScale = 0.01f;
    private float areaThreshold = 0.0001f, magThreshold = 0.00001f, dotThreshold = 0.999f, angleThreshold = 0.001f;

    private LineRenderer lineRenderer;

    private bool isClutching = false, isReset = false;

    [SerializeField]
    private int transferFunction = 0; // 0: linear, 1: accelerating(power), 2: decelerating(hyperbolic tangent)
    private float scaleFactor = 1f;
    private float powFactorA = 3f, tanhFactorA = 0.55f;
    private double powFactorB = 2.7, tanhFactorB = 4d;
    private Dictionary<KeyCode, Action> keyActions;

    private Quaternion origThumbRotation, origScaledThumbRotation;
    private Quaternion origTriangleRotation;
    private Quaternion cubeRotation, origCubeRotation, prevCubeRotation;
    private Quaternion worldWristRotation;
    private float origAngle;

    [SerializeField]
    private TextMeshProUGUI textbox;
    private string[] transferText = { "linear", "power", "tanh" };

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 4;

        if (!isComponentsVisible)
        {
            lineRenderer.enabled = false;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        cube.transform.localScale = new Vector3(cubeScale, cubeScale, cubeScale);
        for (int i = 0; i < 3; i++)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.localScale = new Vector3(sphereScale, sphereScale, sphereScale);
            if (!isComponentsVisible)
            {
                sphere.GetComponent<Renderer>().enabled = false;
            }
            spheres.Add(sphere);
        }

        thumbSphere = spheres[0];
        indexSphere = spheres[1];
        middleSphere = spheres[2];

        if (isLeftHanded)
        {
            ovrSkeleton = ovrLeftSkeleton;
        }
        else
        {
            ovrSkeleton = ovrRightSkeleton;
        }

        indexTipBone = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip);
        middleTipBone = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleTip);
        thumbTipBone = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbTip);
        wristBone = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_Wrist);
        thumbMetacarpal = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbMetacarpal);

        thumbSphere.transform.position = thumbTipBone.Transform.position;
        indexSphere.transform.position = indexTipBone.Transform.position;
        middleSphere.transform.position = middleTipBone.Transform.position;

        worldWristRotation = wristBone.Transform.rotation;
        origThumbRotation = Quaternion.Inverse(worldWristRotation) * thumbMetacarpal.Transform.rotation;
        origScaledThumbRotation = origThumbRotation;
        origCubeRotation = Quaternion.Inverse(worldWristRotation) * cube.transform.rotation;
        origAngle = 0f;
        origTriangleRotation = Quaternion.identity;
        prevCubeRotation = origCubeRotation;

        keyActions = new Dictionary<KeyCode, Action>
        {
            {KeyCode.KeypadEnter, () => cube.transform.rotation = wristBone.Transform.rotation},
            {KeyCode.KeypadMinus, () => ToggleComponentsVisibility(false)},
            {KeyCode.KeypadPlus, () => ToggleComponentsVisibility(true)},
            {KeyCode.Space, () => isClutching = true},
            {KeyCode.Keypad7, () => transferFunction = 0},
            {KeyCode.Keypad8, () => transferFunction = 1},
            {KeyCode.Keypad9, () => transferFunction = 2},
            {KeyCode.Keypad1, () => scaleFactor = 1f},
            {KeyCode.Keypad2, () => scaleFactor = 2f}
        };
    }

    // Update is called once per frame
    void Update()
    {
        // transforms are on the local coordinates based on the wrist if not stated otherwise
        worldWristRotation = wristBone.Transform.rotation;

        if (!Input.anyKey) isReset = false;
        if (!Input.GetKey(KeyCode.Space)) isClutching = false;

        if ((Input.anyKey && !isClutching) || (isClutching && !isReset))
        {
            origThumbRotation = Quaternion.Inverse(worldWristRotation) * thumbMetacarpal.Transform.rotation;
            origScaledThumbRotation = origThumbRotation;
            origCubeRotation = Quaternion.Inverse(worldWristRotation) * cube.transform.rotation;
        }

        foreach (var entry in keyActions)
        {
            if (Input.GetKey(entry.Key))
            {
                entry.Value.Invoke();
            }
        }

        textbox.text = transferText[transferFunction] + string.Format(", scale factor {0}", scaleFactor);
        
        Vector3 thumbPosition = TransferBoneMovement();
        Vector3 indexPosition = wristBone.Transform.InverseTransformPoint(indexTipBone.Transform.position);
        Vector3 middlePosition = wristBone.Transform.InverseTransformPoint(middleTipBone.Transform.position);
        Vector3 worldThumbPosition = wristBone.Transform.TransformPoint(thumbPosition);

        thumbSphere.transform.position = worldThumbPosition;
        indexSphere.transform.position = indexTipBone.Transform.position;
        middleSphere.transform.position = middleTipBone.Transform.position;

        lineRenderer.SetPosition(0, worldThumbPosition);
        lineRenderer.SetPosition(1, indexTipBone.Transform.position);
        lineRenderer.SetPosition(2, middleTipBone.Transform.position);
        lineRenderer.SetPosition(3, worldThumbPosition);

        // position cube with bones since spheres are modified
        cube.transform.position = GetTriangleCentroid(indexTipBone.Transform.position, middleTipBone.Transform.position, thumbTipBone.Transform.position);

        bool isAngleValid = CalculateAngleAtVertex(thumbPosition, indexPosition, middlePosition, out float angle);
        bool isTriangleValid = CalculateTriangleOrientation(thumbPosition, indexPosition, middlePosition, out Quaternion triangleRotation);
        bool isTriangleSmall = CalculateTriangleArea(thumbPosition, indexPosition, middlePosition, out float area);

        if (!isAngleValid) angle = origAngle;
        if (!isTriangleValid) triangleRotation = origTriangleRotation;
        
        if (isAngleValid && isTriangleValid && isTriangleSmall)
        {
            if (isClutching)
            {
                if (isReset)
                {
                    float angleDifference = angle - origAngle;
                    Vector3 triangleAxis = triangleRotation * Vector3.up;
                    Quaternion deltaShearRotation, deltaTriangleRotation;

                    deltaShearRotation = Quaternion.AngleAxis(angleDifference, triangleAxis);
                    deltaTriangleRotation = Quaternion.Inverse(origTriangleRotation) * triangleRotation;
                    cubeRotation = deltaShearRotation * deltaTriangleRotation * origCubeRotation;
                    cube.transform.rotation = worldWristRotation * cubeRotation;

                    prevCubeRotation = cubeRotation;
                }
                else
                {
                    origAngle = angle;
                    origTriangleRotation = triangleRotation;
                    isReset = true;
                }
            }
            else
            {
                cubeRotation = prevCubeRotation;
                cube.transform.rotation = worldWristRotation * cubeRotation;
            }
        }
    }

    private void ToggleComponentsVisibility(bool b)
    {
        isComponentsVisible = b;
        foreach (GameObject sphere in spheres)
        {
            sphere.GetComponent<Renderer>().enabled = b;
        }
        lineRenderer.enabled = b;
    }

    private void TransferCartesian()
    {

    }

    private Vector3 TransferBoneMovement()
    {
        Quaternion worldWristRotation = wristBone.Transform.rotation;
        Quaternion worldThumbRotation = thumbMetacarpal.Transform.rotation;

        Vector3 thumbPosition = wristBone.Transform.InverseTransformPoint(thumbMetacarpal.Transform.position);
        Vector3 thumbTipPosition = wristBone.Transform.InverseTransformPoint(thumbTipBone.Transform.position);
        Quaternion thumbRotation = Quaternion.Inverse(worldWristRotation) * worldThumbRotation;
        Quaternion deltaThumbRotation = Quaternion.Inverse(origThumbRotation) * thumbRotation;
        deltaThumbRotation.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle < angleThreshold)
        {
            return thumbTipPosition;
        }

        double angleRadian = angle / 180.0 * Math.PI;
        float modifiedAngle = angle;

        if (transferFunction == 0)
        {
            modifiedAngle = angle * scaleFactor;
        }
        else if (transferFunction == 1)
        {
            modifiedAngle = powFactorA * (float)Math.Pow(angleRadian, powFactorB) * 180f / (float)Math.PI;
        }
        else if (transferFunction == 2)
        {
            modifiedAngle = tanhFactorA * (float)Math.Tanh(tanhFactorB * angleRadian) * 180f / (float)Math.PI;
        }

        Quaternion scaledDeltaRotation = Quaternion.AngleAxis(modifiedAngle, axis);
        Quaternion scaledThumbRotation = origScaledThumbRotation * scaledDeltaRotation;

        Vector3 localTipPosition = thumbMetacarpal.Transform.InverseTransformPoint(thumbTipBone.Transform.position); // local based on thumb metacarpal
        Vector3 scaledTipPosition = thumbPosition + scaledThumbRotation * localTipPosition;

        return scaledTipPosition;
        // Vector3 worldScaledTipPosition = wristBone.Transform.TransformPoint(scaledTipPosition);

        // thumbSphere.transform.position = worldScaledTipPosition;
    }

    public Vector3 GetTriangleCentroid(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float centerX = (p1.x + p2.x + p3.x) / 3f;
        float centerY = (p1.y + p2.y + p3.y) / 3f;
        float centerZ = (p1.z + p2.z + p3.z) / 3f;

        return new Vector3(centerX, centerY, centerZ);
    }

    public bool CalculateTriangleArea(Vector3 p1, Vector3 p2, Vector3 p3, out float area)
    {
        Vector3 vectorAB = p2 - p1;
        Vector3 vectorAC = p3 - p1;

        Vector3 crossProduct = Vector3.Cross(vectorAB, vectorAC);

        area = crossProduct.magnitude / 2f;

        if (area < areaThreshold)
        {
            area = float.NaN;
            return false;
        }

        return true;
    }

    private bool CalculateAngleAtVertex(Vector3 vertex, Vector3 other1, Vector3 other2, out float angle)
    {
        Vector3 vec1 = other1 - vertex;
        Vector3 vec2 = other2 - vertex;

        if (vec1.sqrMagnitude < magThreshold || vec2.sqrMagnitude < magThreshold)
        {
            angle = float.NaN;
            return false;
        }

        angle = Vector3.Angle(vec1, vec2);
        return true;
    }

    private bool CalculateTriangleOrientation(Vector3 point1, Vector3 point2, Vector3 point3, out Quaternion orientation)
    {
        Vector3 forward = (point2 - point1).normalized;
        Vector3 roughUp = (point3 - point1).normalized;

        if (forward.magnitude < magThreshold || roughUp.magnitude < magThreshold || Vector3.Dot(forward, roughUp) > dotThreshold || Vector3.Dot(forward, roughUp) < -dotThreshold)
        {
            orientation = Quaternion.identity;
            return false;
        }

        Vector3 normal = Vector3.Cross(forward, roughUp).normalized;

        if (normal.magnitude < magThreshold)
        {
            orientation = Quaternion.identity;
            return false;
        }

        orientation = Quaternion.LookRotation(forward, normal);
        return true;
    }
}
