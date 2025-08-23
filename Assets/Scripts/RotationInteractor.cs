using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using TMPro;

public class RotationInteractor : MonoBehaviour
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
    private float cubeScale = 0.06f;

    private List<GameObject> spheres = new List<GameObject>();
    private GameObject thumbSphere, indexSphere, middleSphere;
    private float sphereScale = 0.01f;
    private float areaThreshold = 0.0001f, magThreshold = 0.00001f, dotThreshold = 0.999f;
    private float thumbAngleThreshold = 0.01f, triAngleThreshold = 30f;

    private LineRenderer lineRenderer;

    private bool isGrabbed = true, isClutching = false, isReset = false;

    [SerializeField]
    private int transferFunction = 0; // 0: linear, 1: accelerating(power), 2: decelerating(hyperbolic tangent)
    private float scaleFactor = 1f;
    private float powFactorA = 3f, tanhFactorA = 0.55f;
    private double powFactorB = 2.7, tanhFactorB = 4d;
    private Dictionary<KeyCode, Action> keyActions;

    private Quaternion origThumbRotation, origScaledThumbRotation;
    private Quaternion worldWristRotation;
    private Quaternion cubeRotation, prevCubeRotation;
    private Quaternion prevTriangleRotation;
    private float prevAngle;
    private Vector3 grabOffsetPosition, centroidPosition;
    private Quaternion grabOffsetRotation;

    [SerializeField]
    private TextMeshProUGUI textbox;
    private string[] transferText = { "linear", "power", "tanh" };

    public delegate Vector3 GetTriangleCenter(Vector3 p1, Vector3 p2, Vector3 p3);
    private GetTriangleCenter CenterCalculation;
    private float thumbWeight = 1f; // 2*2

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
            sphere.GetComponent<Collider>().isTrigger = true;
            sphere.tag = "TipSphere";
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
        cube.transform.position = new Vector3(0.1f, 1f, 0.3f);

        worldWristRotation = wristBone.Transform.rotation;
        origThumbRotation = Quaternion.Inverse(worldWristRotation) * thumbMetacarpal.Transform.rotation;
        origScaledThumbRotation = origThumbRotation;
        prevAngle = 0f;
        prevTriangleRotation = Quaternion.identity;
        // prevCubeRotation = Quaternion.Inverse(worldWristRotation) * cube.transform.rotation;
        prevCubeRotation = Quaternion.identity;
        grabOffsetPosition = Vector3.zero;
        grabOffsetRotation = Quaternion.identity;

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
            {KeyCode.Keypad2, () => scaleFactor = 2f},
            // {KeyCode.Q, () => CenterCalculation = GetTriangleCentroid},
            // {KeyCode.W, () => CenterCalculation = GetTriangleIncenter},
            // {KeyCode.E, () => CenterCalculation = GetTriangleCircumcenter},
            // {KeyCode.R, () => CenterCalculation = GetTriangleOrthocenter}
            {KeyCode.Q, () => thumbWeight = 1f},
            {KeyCode.W, () => thumbWeight = 2f},
            {KeyCode.E, () => thumbWeight = 3f},
            {KeyCode.R, () => thumbWeight = 4f}
        };

        // CenterCalculation = GetTriangleCentroid;
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

        bool isAngleValid = CalculateAngleAtVertex(thumbPosition, indexPosition, middlePosition, out float angle);
        bool isTriangleValid = CalculateTriangleOrientation(thumbPosition, indexPosition, middlePosition, out Quaternion triangleRotation);
        bool isTriangleSmall = CalculateTriangleArea(thumbPosition, indexPosition, middlePosition, out float area);
        // position cube with bones since spheres are modified
        // centroidPosition = GetTriangleCentroid(indexTipBone.Transform.position, middleTipBone.Transform.position, thumbTipBone.Transform.position);
        // centroidPosition = CenterCalculation.Invoke(thumbTipBone.Transform.position,indexTipBone.Transform.position, middleTipBone.Transform.position);
        centroidPosition = GetWeightedTriangleCentroid(thumbTipBone.Transform.position, indexTipBone.Transform.position, middleTipBone.Transform.position, thumbWeight);

        // if (isGrabbed)
        // {
            if (isClutching)
            {
                if (isReset)
                {
                    if (isAngleValid && isTriangleValid && isTriangleSmall)
                    {
                        float angleDifference = angle - prevAngle;
                        Vector3 triangleAxis = triangleRotation * Vector3.up;
                        Quaternion deltaShearRotation, deltaTriangleRotation;

                        deltaShearRotation = Quaternion.AngleAxis(angleDifference, triangleAxis);
                        deltaTriangleRotation = Quaternion.Inverse(prevTriangleRotation) * triangleRotation;
                        deltaTriangleRotation.ToAngleAxis(out float deltaAngle, out _);
                        if (deltaAngle < triAngleThreshold)
                        {
                            cubeRotation = deltaShearRotation * deltaTriangleRotation * prevCubeRotation;
                            cube.transform.rotation = worldWristRotation * cubeRotation * grabOffsetRotation;
                        }
                        prevCubeRotation = cubeRotation;
                    }
                }
                else
                {
                    isReset = true;
                }
            }
            else
            {
                cubeRotation = prevCubeRotation;
                cube.transform.rotation = worldWristRotation * cubeRotation * grabOffsetRotation;
            }
            cube.transform.position = grabOffsetPosition + centroidPosition;
        // }

        if (isAngleValid && isTriangleValid && isTriangleSmall)
        {
            prevAngle = angle;
            prevTriangleRotation = triangleRotation;
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

        if (angle < thumbAngleThreshold)
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
        // float centerX = (p1.x + p2.x + p3.x) / 3f;
        // float centerY = (p1.y + p2.y + p3.y) / 3f;
        // float centerZ = (p1.z + p2.z + p3.z) / 3f;

        // return new Vector3(centerX, centerY, centerZ);
        return (p1 + p2 + p3) / 3f;
    }

    public Vector3 GetWeightedTriangleCentroid(Vector3 p1, Vector3 p2, Vector3 p3, float w)
    {
        // float centerX = (p1.x + p2.x + p3.x) / 3f;
        // float centerY = (p1.y + p2.y + p3.y) / 3f;
        // float centerZ = (p1.z + p2.z + p3.z) / 3f;

        // return new Vector3(centerX, centerY, centerZ);
        // return (p1 + p2 + p3) / 3f;
        return (w * p1 + p2 + p3) / (w + 1f + 1f);
    }

    public Vector3 GetTriangleIncenter(Vector3 a, Vector3 b, Vector3 c)
    {
        // Get the lengths of the sides opposite each vertex.
        // Side 'a' is opposite vertex A, so its length is the distance from B to C.
        float sideA = Vector3.Distance(b, c);
        float sideB = Vector3.Distance(a, c);
        float sideC = Vector3.Distance(a, b);

        // Calculate the incenter using the formula.
        Vector3 incenter = (sideA * a + sideB * b + sideC * c) / (sideA + sideB + sideC);
        return incenter;
    }
    public Vector3 GetTriangleCircumcenter(Vector3 v1, Vector3 v2, Vector3 v3)
    {
        Vector3 a = v2 - v1;
        Vector3 b = v3 - v1;

        // Calculate the cross product, which is twice the signed area of the triangle.
        // It's also normal to the triangle plane.
        Vector3 cross = Vector3.Cross(a, b);
        float crossMagnitudeSquared = cross.sqrMagnitude;

        if (crossMagnitudeSquared < 1e-6f)
        {
            // The vertices are collinear. The circumcenter is not uniquely defined.
            // Returning the centroid as a safe fallback.
            return (v1 + v2 + v3) / 3.0f;
        }

        // The formula for the circumcenter:
        Vector3 circumcenter = v1 + (b.sqrMagnitude * Vector3.Cross(cross, a) + a.sqrMagnitude * Vector3.Cross(b, cross)) / (2.0f * crossMagnitudeSquared);

        return circumcenter;
    }

    public Vector3 GetTriangleOrthocenter(Vector3 a, Vector3 b, Vector3 c)
    {
        // The orthocenter, centroid, and circumcenter of a triangle are collinear.
        // This is known as the Euler Line. We can use this property for a simple calculation.
        // H (Orthocenter) = G (Centroid) + 2 * (G - C (Circumcenter))
        // Simplified: H = 3 * G - 2 * C

        Vector3 centroid = GetTriangleCentroid(a, b, c);
        Vector3 circumcenter = GetTriangleCircumcenter(a, b, c);

        // A direct calculation of the orthocenter.
        // This is a simplified approach, which works well for a non-degenerate triangle.
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 bc = c - b;

        // Use the dot product property: (H - a) . bc = 0
        // (H - b) . ac = 0
        // (H - c) . ab = 0

        float D = 2 * (ab.x * ac.y - ab.y * ac.x);

        // Check for near-zero denominator (collinear vertices)
        if (Mathf.Abs(D) < 1e-6f)
        {
            // Fallback for collinear case.
            return (a + b + c) / 3.0f;
        }

        float Hx = (ac.y * (ab.sqrMagnitude) - ab.y * (ac.sqrMagnitude)) / D;
        float Hy = (ab.x * (ac.sqrMagnitude) - ac.x * (ab.sqrMagnitude)) / D;

        Vector3 orthocenter = new Vector3(Hx, Hy, 0.0f) + a;

        // The Euler Line method is more robust and cleaner for 3D.
        // H = 3G - 2C
        return 3.0f * centroid - 2.0f * circumcenter;
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

    public bool getIsGrabbed()
    {
        return isGrabbed;
    }

    public void setIsGrabbed(bool b)
    {
        isGrabbed = b;

        if (isGrabbed)
        {
            grabOffsetPosition = cube.transform.position - centroidPosition;
            grabOffsetRotation = Quaternion.Inverse(wristBone.Transform.rotation) * cube.transform.rotation;
            prevCubeRotation = Quaternion.identity;
        }
        else
        {
            grabOffsetPosition = Vector3.zero;
            grabOffsetRotation = Quaternion.identity;
        }
    }
}
