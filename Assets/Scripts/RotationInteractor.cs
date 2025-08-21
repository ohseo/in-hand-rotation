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
    private OVRBone indexTipBone, middleTipBone, thumbTipBone, palmBone, thumbMetacarpal, thumbProximal, wristBone;

    [SerializeField]
    private GameObject cube;

    // all local rotations are based on the wrist
    private Quaternion prevCubeRotation, prevLocalCubeRotation, origCubeRotation, origLocalCubeRotation;
    private Quaternion origTriRotation, currTriRotation;
    private float origAngle, currAngle;
    private Vector3 origThumbPosition, origSpherePosition;
    private Quaternion origLocalCMCRotation, origScaledLocalCMCRotation;

    private Vector3 rotationAxis = Vector3.forward;
    private float cubeScale = 0.03f;
    private float scaleFactor = 1.0f;

    [SerializeField]
    private int scaleMode = 0; // 0: angle, 1: thumb-tip position, 2: cmc
    [SerializeField]
    private int transferFunction = 0; // 0: linear, 1: accelerating(power), 2: decelerating(hyperbolic tangent)

    private List<GameObject> spheres = new List<GameObject>();
    private float sphereScale = 0.01f;
    private float areaThreshold = 0.0001f;

    private LineRenderer lineRenderer;

    private bool isReset = false;

    [SerializeField]
    private TextMeshProUGUI textbox;
    private string uitext = "";
    private string[] scaleMethodText = { "angle", "cartesian", "metacarpal" };
    private string[] transferText = {"as-is", "power", "tanh"};

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 4;

        if (!isComponentsVisible)
        {
            lineRenderer.enabled = false;
        }

        if (isLeftHanded)
        {
            ovrSkeleton = ovrLeftSkeleton;
        }
        else
        {
            ovrSkeleton = ovrRightSkeleton;
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

        indexTipBone = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip);
        middleTipBone = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleTip);
        thumbTipBone = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbTip);
        // palmBone = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexProximal);
        wristBone = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_Wrist);
        thumbMetacarpal = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbMetacarpal);
        // thumbProximal = ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbProximal);

        spheres[1].transform.position = indexTipBone.Transform.position;
        spheres[2].transform.position = middleTipBone.Transform.position;
        spheres[0].transform.position = thumbTipBone.Transform.position;

        origCubeRotation = Quaternion.identity;
        origLocalCubeRotation = cube.transform.localRotation;
        origThumbPosition = wristBone.Transform.InverseTransformPoint(thumbTipBone.Transform.position);
        origSpherePosition = origThumbPosition;

        origLocalCMCRotation = Quaternion.Inverse(wristBone.Transform.rotation) * thumbMetacarpal.Transform.rotation;
        origScaledLocalCMCRotation = origLocalCMCRotation;

        prevLocalCubeRotation = origLocalCubeRotation;

        if (!GetTriangleOrientation(spheres[0].transform.position, spheres[1].transform.position, spheres[2].transform.position, origTriRotation, out origTriRotation))
        {
            origTriRotation = cube.transform.localRotation;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!Input.anyKey)
        {
            isReset = false;
        }

        if ((Input.anyKey && !Input.GetKey(KeyCode.Space)) || (Input.GetKey(KeyCode.Space) && !isReset))
        {
            origThumbPosition = wristBone.Transform.InverseTransformPoint(thumbTipBone.Transform.position);
            origSpherePosition = origThumbPosition;
            origLocalCMCRotation = Quaternion.Inverse(wristBone.Transform.rotation) * thumbMetacarpal.Transform.rotation;
            origScaledLocalCMCRotation = origLocalCMCRotation;
            origCubeRotation = cube.transform.rotation;
            origLocalCubeRotation = Quaternion.Inverse(wristBone.Transform.rotation) * cube.transform.rotation;
        }

        if (Input.GetKey(KeyCode.KeypadEnter))
        {
            // scaleFactor = 2.0f;
            cube.transform.rotation = Quaternion.identity * wristBone.Transform.rotation;
        }
        else if (Input.GetKey(KeyCode.KeypadPlus))
        {
            scaleFactor = 1.0f;
        }
        else if (Input.GetKey(KeyCode.Keypad0))
        {
            scaleMode = 0;
        }
        else if (Input.GetKey(KeyCode.Keypad1))
        {
            scaleMode = 1;
        }
        else if (Input.GetKey(KeyCode.Keypad2))
        {
            scaleMode = 2;
        }
        else if (Input.GetKey(KeyCode.Keypad7))
        {
            transferFunction = 0;
        }
        else if (Input.GetKey(KeyCode.Keypad8))
        {
            transferFunction = 1;
        }
        else if (Input.GetKey(KeyCode.Keypad9))
        {
            transferFunction = 2;
        }
        else if (Input.GetKey(KeyCode.KeypadMinus))
        {
            isComponentsVisible = false;
            foreach (GameObject sphere in spheres)
            {
                sphere.GetComponent<Renderer>().enabled = false;
            }
            lineRenderer.enabled = false;
        }
        else if (Input.GetKey(KeyCode.KeypadMultiply))
        {
            isComponentsVisible = true;
            foreach (GameObject sphere in spheres)
            {
                sphere.GetComponent<Renderer>().enabled = true;
            }
            lineRenderer.enabled = true;
        }

        // textbox.text = string.Format("{0}, scale factor {1}", uitext, scaleFactor);
        // textbox.text = string.Format("{0}, transfer function {1}", uitext, transferFunction);
        textbox.text = scaleMethodText[scaleMode] + ", " + transferText[transferFunction];


        // cube.transform.position = palmBone.Transform.position - palmBone.Transform.up * 0.04f + palmBone.Transform.forward * 0.03f;
        // cube.transform.rotation = palmBone.Transform.rotation * prevCubeLocalRotation;

        spheres[1].transform.position = indexTipBone.Transform.position;
        spheres[2].transform.position = middleTipBone.Transform.position;
        spheres[0].transform.position = thumbTipBone.Transform.position;

        if (scaleMode == 1)
        {
            Vector3 currLocalPose = wristBone.Transform.InverseTransformPoint(thumbTipBone.Transform.position);
            Vector3 delta = currLocalPose - origThumbPosition;
            Vector3 exaggerated = origSpherePosition + delta * scaleFactor;
            spheres[0].transform.position = wristBone.Transform.TransformPoint(exaggerated);
            // prevThumbPosition = currLocalPose;
            // prevSpherePosition = exaggerated;
        }
        else if (scaleMode == 2)
        {
            Quaternion currWristRotation = wristBone.Transform.rotation;
            Quaternion currCMCRotation = thumbMetacarpal.Transform.rotation;

            Quaternion currLocalCMCRotation = Quaternion.Inverse(currWristRotation) * currCMCRotation;
            Quaternion deltaLocalRotation = Quaternion.Inverse(origLocalCMCRotation) * currLocalCMCRotation;
            deltaLocalRotation.ToAngleAxis(out float angle, out Vector3 axis);

            double angleRadian = angle / 180.0 * Math.PI;
            float modifiedAngle = 0f;
            if (transferFunction == 0)
            {
                modifiedAngle = angle * scaleFactor;
            }
            else if (transferFunction == 1)
            {
                modifiedAngle = 3f * (float)Math.Pow(angleRadian, 2.7) * 180f / (float)Math.PI;
            }
            else if (transferFunction == 2)
            {
                modifiedAngle = 0.55f * (float)Math.Tanh(4d * angleRadian) * 180f / (float)Math.PI;
                // modifiedAngle = 0.65f * (float)Math.Pow(angleRadian, 0.33) * 180f / (float)Math.PI;
            }

            Quaternion scaledRot_wrist = Quaternion.AngleAxis(modifiedAngle, axis);
            Quaternion currScaledCMCRot_wrist = origScaledLocalCMCRotation * scaledRot_wrist;

            Vector3 currCMCPos_wrist = wristBone.Transform.InverseTransformPoint(thumbMetacarpal.Transform.position);
            Vector3 currTipPos_CMC = thumbMetacarpal.Transform.InverseTransformPoint(thumbTipBone.Transform.position);

            Vector3 currScaledTipPos_wrist = currCMCPos_wrist + currScaledCMCRot_wrist * currTipPos_CMC;
            Vector3 currScaledTipPos = wristBone.Transform.TransformPoint(currScaledTipPos_wrist);

            spheres[0].transform.position = currScaledTipPos;
        }

        lineRenderer.SetPosition(0, spheres[0].transform.position);
        lineRenderer.SetPosition(1, spheres[1].transform.position);
        lineRenderer.SetPosition(2, spheres[2].transform.position);
        lineRenderer.SetPosition(3, spheres[0].transform.position);

        cube.transform.position = CalculateCentroid(indexTipBone.Transform.position, middleTipBone.Transform.position, thumbTipBone.Transform.position);

        if (GetAngleAtVertex(spheres[0].transform.position, spheres[1].transform.position, spheres[2].transform.position, out currAngle))
        {
            if (GetTriangleOrientation(spheres[0].transform.position, spheres[1].transform.position, spheres[2].transform.position, origTriRotation, out currTriRotation))
            {
                if (CalculateTriangleArea(spheres[0].transform.position, spheres[1].transform.position, spheres[2].transform.position) > areaThreshold)
                {
                    if (Input.GetKey(KeyCode.Space))
                    {
                        if (isReset)
                        {
                            float angleDifference = currAngle - origAngle;
                            Vector3 triAxis = currTriRotation * Vector3.up;
                            Quaternion deltaShearRotation, deltaTriRotation;
                            if (scaleMode == 0)
                            {
                                deltaShearRotation = Quaternion.AngleAxis(angleDifference, triAxis);
                                deltaTriRotation = currTriRotation * Quaternion.Inverse(origTriRotation);
                                Quaternion combinedRotation = deltaShearRotation * deltaTriRotation;
                                combinedRotation.ToAngleAxis(out float angle, out Vector3 axis);
                                // cube.transform.rotation = Quaternion.AngleAxis(angle * scaleFactor, axis) * origLocalCubeRotation * wristBone.Transform.rotation;
                                cube.transform.rotation = Quaternion.AngleAxis(angle * scaleFactor, axis) * origCubeRotation;
                            }
                            else
                            {
                                deltaShearRotation = Quaternion.AngleAxis(angleDifference, triAxis);
                                deltaTriRotation = currTriRotation * Quaternion.Inverse(origTriRotation);
                                // cube.transform.rotation = deltaShearRotation * deltaTriRotation * origLocalCubeRotation * wristBone.Transform.rotation;
                                cube.transform.rotation = deltaShearRotation * deltaTriRotation * origCubeRotation;
                            }
                            // prevCubeRotation = cube.transform.rotation;
                            prevLocalCubeRotation = Quaternion.Inverse(wristBone.Transform.rotation) * cube.transform.rotation;
                        }
                        else
                        {
                            origAngle = currAngle;
                            origTriRotation = currTriRotation;
                            // cube.transform.rotation = wristBone.Transform.rotation * prevLocalCubeRotation;
                            isReset = true;
                            Debug.Log(prevLocalCubeRotation.eulerAngles);
                        }
                    }
                    else
                    {
                        cube.transform.rotation = wristBone.Transform.rotation * prevLocalCubeRotation;
                    }
                }
                // prevAngle = currAngle;
                // prevTriRotation = currTriRotation;
            }
        }
    }

    private bool GetTriangleOrientation(Vector3 point1, Vector3 point2, Vector3 point3, Quaternion origOrientation, out Quaternion orientation)
    {
        Vector3 forward = (point2 - point1).normalized;
        Vector3 roughUp = (point3 - point1).normalized;

        // Handle collinear points case
        if (forward.magnitude < 0.0001f || roughUp.magnitude < 0.0001f || Vector3.Dot(forward, roughUp) > 0.999f || Vector3.Dot(forward, roughUp) < -0.999f)
        {
            // Points are too close or collinear, cannot form a valid plane normal.
            // Return identity or the last valid rotation.
            // Debug.LogWarning("Spheres are collinear or too close. Cannot calculate stable triangle orientation.");
            orientation = origOrientation; // Or return current cube rotation to stop it from jumping
            return false;
        }

        Vector3 normal = Vector3.Cross(forward, roughUp).normalized;

        // If the normal is zero (due to collinear points), LookRotation will throw an error.
        if (normal.magnitude < 0.0001f)
        {
            //  Debug.LogWarning("Calculated normal is zero. Spheres might be collinear.");
            orientation = origOrientation;
            return false;
        }

        // LookRotation aligns transform.forward (Z-axis) with 'forward' parameter,
        // and transform.up (Y-axis) with 'upwards' parameter (best fit).
        orientation = Quaternion.LookRotation(forward, normal);
        return true;
    }

    public float CalculateTriangleArea(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        // Form two vectors from a common vertex
        Vector3 vectorAB = p2 - p1;
        Vector3 vectorAC = p3 - p1;

        // Calculate the cross product of these two vectors
        Vector3 crossProduct = Vector3.Cross(vectorAB, vectorAC);

        // The magnitude of the cross product is twice the area of the triangle
        float area = crossProduct.magnitude / 2f;

        // Optionally, check for collinear points:
        // If the area is very close to zero, the points are collinear or identical.
        if (area < 0.00001f) // Use a small epsilon for floating point comparison
        {
            // Debug.LogWarning("Points are collinear or too close to form a valid triangle.");
            return 0f;
        }

        return area;
    }

    public Vector3 CalculateCentroid(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float centerX = (p1.x + p2.x + p3.x) / 3f;
        float centerY = (p1.y + p2.y + p3.y) / 3f;
        float centerZ = (p1.z + p2.z + p3.z) / 3f;

        return new Vector3(centerX, centerY, centerZ);
    }

    private bool GetAngleAtVertex(Vector3 vertex, Vector3 other1, Vector3 other2, out float angle)
    {
        Vector3 vec1 = other1 - vertex;
        Vector3 vec2 = other2 - vertex;

        // Check if vectors are too short, which would lead to division by zero or highly inaccurate normalized vectors.
        if (vec1.sqrMagnitude < 0.000001f || vec2.sqrMagnitude < 0.000001f)
        {
            angle = float.NaN;
            return false;
        }

        // Vector3.Angle returns the angle in degrees between two vectors.
        angle = Vector3.Angle(vec1, vec2);
        return true;
    }
}
