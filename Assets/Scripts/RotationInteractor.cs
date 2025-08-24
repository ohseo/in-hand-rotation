using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using TMPro;

public class RotationInteractor : MonoBehaviour
{
    [SerializeField]
    private bool _isComponentsVisible = true;
    [SerializeField]
    private bool _isLeftHanded = false;
    [SerializeField]
    private bool _isShearFactorOn = true;
    [SerializeField]
    private OVRSkeleton _ovrRightSkeleton, _ovrLeftSkeleton;
    private OVRSkeleton _ovrSkeleton;
    private OVRBone _indexTipBone, _middleTipBone, _thumbTipBone, _thumbMetacarpal, _wristBone;

    private GameObject _cube;

    private List<GameObject> _spheres = new List<GameObject>();
    private GameObject _thumbSphere, _indexSphere, _middleSphere;
    private float _sphereScale = 0.01f;
    private float _areaThreshold = 0.0001f, _magThreshold = 0.00001f, _dotThreshold = 0.999f;
    private float _thumbAngleThreshold = 0.01f, _triAngleThreshold = 30f;

    private LineRenderer _lineRenderer;

    private bool _isGrabbed = false, _isClutching = false, _isReset = false, _isOnTarget = false, _isTaskComplete = false;

    [SerializeField]
    private int _transferFunction = 0; // 0: linear, 1: accelerating(power), 2: decelerating(hyperbolic tangent)
    private float _scaleFactor = 1f;
    // private float _powFactorA = 0.955f, _tanhFactorA = 1.094f;
    // private double _powFactorB = 2d, _tanhFactorB = 1.829d;
    private float _powFactorA = 1.910f, _tanhFactorA = 0.547f;
    private double _powFactorB = 2d, _tanhFactorB = 3.657d;
    private Dictionary<KeyCode, Action> _keyActions;

    private Quaternion _origThumbRotation, _origScaledThumbRotation;
    private Quaternion _worldWristRotation;
    private Quaternion _cubeRotation, _prevCubeRotation;
    private Quaternion _prevTriangleRotation, _triangleRotation;
    private float _prevAngle;
    private Vector3 _grabOffsetPosition, _centroidPosition;
    private Quaternion _grabOffsetRotation;
    Vector3 _triangleForward, _triangleUp;
    private Outline _outline;

    [SerializeField]
    private TextMeshProUGUI _textbox;
    private string[] _transferText = { "linear", "power", "tanh" };

    public delegate Vector3 GetTriangleCenter(Vector3 p1, Vector3 p2, Vector3 p3);
    private GetTriangleCenter _CenterCalculation;
    private float _thumbWeight = 1.41f; // 2*2
    private float _fingerWeight = 1f;

    void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.positionCount = 4;

        if (!_isComponentsVisible)
        {
            _lineRenderer.enabled = false;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        for (int i = 0; i < 3; i++)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.localScale = new Vector3(_sphereScale, _sphereScale, _sphereScale);
            if (!_isComponentsVisible)
            {
                sphere.GetComponent<Renderer>().enabled = false;
            }
            sphere.GetComponent<Collider>().isTrigger = true;
            sphere.tag = "TipSphere";
            _spheres.Add(sphere);
        }

        _thumbSphere = _spheres[0];
        _indexSphere = _spheres[1];
        _middleSphere = _spheres[2];

        if (_isLeftHanded) _ovrSkeleton = _ovrLeftSkeleton;
        else _ovrSkeleton = _ovrRightSkeleton;

        InitGeometry();

        _keyActions = new Dictionary<KeyCode, Action>
        {
            {KeyCode.KeypadEnter, () => _cube.transform.rotation = _wristBone.Transform.rotation},
            {KeyCode.KeypadMinus, () => ToggleComponentsVisibility(false)},
            {KeyCode.KeypadPlus, () => ToggleComponentsVisibility(true)},
            {KeyCode.Space, () => _isClutching = true},
            {KeyCode.Keypad7, () => _transferFunction = 0},
            {KeyCode.Keypad8, () => _transferFunction = 1},
            {KeyCode.Keypad9, () => _transferFunction = 2},
            {KeyCode.Keypad1, () => _scaleFactor = 1f},
            {KeyCode.Keypad2, () => _scaleFactor = 2f},
            // {KeyCode.Q, () => _CenterCalculation = GetWeightedTriangleCentroid},
            // {KeyCode.W, () => _CenterCalculation = GetTriangleIncenter},
            // {KeyCode.E, () => _CenterCalculation = GetTriangleCircumcenter},
            // {KeyCode.R, () => _CenterCalculation = GetTriangleOrthocenter},
            {KeyCode.Q, () => _thumbWeight = 1f},
            {KeyCode.W, () => _thumbWeight = 2f},
            {KeyCode.E, () => _thumbWeight = 3f},
            {KeyCode.R, () => _thumbWeight = 4f},
            {KeyCode.S, () => _isShearFactorOn = true},
            {KeyCode.D, () => _isShearFactorOn = false}
        };

        _CenterCalculation = GetWeightedTriangleCentroid;

        _cube.GetComponentInChildren<DieGrabHandler>().SetRotationInteractor(this);
        _cube.GetComponentInChildren<DieReleaseHandler>().SetRotationInteractor(this);
        _outline = _cube.GetComponentInChildren<Outline>();
        _outline.OutlineColor = Color.blue;
        _outline.enabled = false;
    }

    // Update is called once per frame
    void Update()
    {
        // transforms are on the local coordinates based on the wrist if not stated otherwise
        _worldWristRotation = _wristBone.Transform.rotation;

        if (!Input.anyKey) _isReset = false;
        if (!Input.GetKey(KeyCode.Space)) _isClutching = false;

        if ((Input.anyKey && !_isClutching) || (_isClutching && !_isReset))
        {
            _origThumbRotation = Quaternion.Inverse(_worldWristRotation) * _thumbMetacarpal.Transform.rotation;
            _origScaledThumbRotation = _origThumbRotation;
        }

        foreach (var entry in _keyActions)
        {
            if (Input.GetKey(entry.Key))
            {
                entry.Value.Invoke();
            }
        }

        _textbox.text = _transferText[_transferFunction] + string.Format(", scale factor {0}", _scaleFactor);

        Vector3 thumbPosition = TransferBoneMovement();
        Vector3 indexPosition = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        Vector3 middlePosition = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);
        Vector3 worldThumbPosition = _wristBone.Transform.TransformPoint(thumbPosition);

        // _thumbSphere.transform.position = worldThumbPosition;
        _thumbSphere.transform.position = _thumbTipBone.Transform.position;
        _indexSphere.transform.position = _indexTipBone.Transform.position;
        _middleSphere.transform.position = _middleTipBone.Transform.position;

        _lineRenderer.SetPosition(0, worldThumbPosition);
        _lineRenderer.SetPosition(1, _indexTipBone.Transform.position);
        _lineRenderer.SetPosition(2, _middleTipBone.Transform.position);
        _lineRenderer.SetPosition(3, worldThumbPosition);

        bool isAngleValid = CalculateAngleAtVertex(thumbPosition, indexPosition, middlePosition, out float angle);
        bool isTriangleValid = CalculateTriangleOrientation(thumbPosition, indexPosition, middlePosition, out _triangleRotation);
        bool isTriangleSmall = CalculateTriangleArea(thumbPosition, indexPosition, middlePosition, out float area);
        // position cube with bones since spheres are modified
        // centroidPosition = GetTriangleCentroid(indexTipBone.Transform.position, middleTipBone.Transform.position, thumbTipBone.Transform.position);
        _centroidPosition = _CenterCalculation.Invoke(_thumbTipBone.Transform.position, _indexTipBone.Transform.position, _middleTipBone.Transform.position);
        // centroidPosition = GetWeightedTriangleCentroid(thumbTipBone.Transform.position, indexTipBone.Transform.position, middleTipBone.Transform.position, fingerWeight);

        if (_isGrabbed)
        {
            if (_isClutching)
            {
                if (_isReset)
                {
                    if (isAngleValid && isTriangleValid && isTriangleSmall)
                    {
                        float angleDifference = angle - _prevAngle;
                        Vector3 triangleAxis = _triangleRotation * Vector3.up;
                        Quaternion deltaShearRotation, deltaTriangleRotation;
                        if (_isShearFactorOn) deltaShearRotation = Quaternion.AngleAxis(angleDifference, triangleAxis);
                        else deltaShearRotation = Quaternion.identity;
                        deltaTriangleRotation = _triangleRotation * Quaternion.Inverse(_prevTriangleRotation);
                        deltaTriangleRotation.ToAngleAxis(out float deltaAngle, out _);
                        if (deltaAngle < _triAngleThreshold)
                        {
                            _cubeRotation = deltaShearRotation * deltaTriangleRotation * _prevCubeRotation;
                            _cube.transform.rotation = _worldWristRotation * _cubeRotation * _grabOffsetRotation;
                        }
                        _prevCubeRotation = _cubeRotation;
                    }
                }
                else
                {
                    _grabOffsetPosition = _cube.transform.position - _centroidPosition;
                    _grabOffsetRotation = Quaternion.Inverse(_wristBone.Transform.rotation) * _cube.transform.rotation;
                    _prevCubeRotation = Quaternion.identity;
                    _cubeRotation = Quaternion.identity;
                    _isReset = true;
                }
            }
            else
            {
                _cubeRotation = _prevCubeRotation;
                _cube.transform.rotation = _worldWristRotation * _cubeRotation * _grabOffsetRotation;
            }
            // _cube.transform.position = rotatedOffset + _centroidPosition;
            _cube.transform.position = _worldWristRotation * _cubeRotation * Quaternion.Inverse(_worldWristRotation) * _grabOffsetPosition + _centroidPosition;
            // _cube.transform.position = _grabOffsetPosition + _centroidPosition;
        }

        if (isAngleValid && isTriangleValid && isTriangleSmall)
        {
            _prevAngle = angle;
            _prevTriangleRotation = _triangleRotation;
        }
    }

    public void Reset()
    {
        InitGeometry();
        IsGrabbed = false;
        IsClutching = false;
        IsOnTarget = false;
        IsTaskComplete = false;
    }

    private void InitGeometry()
    {
        _indexTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip);
        _middleTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleTip);
        _thumbTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbTip);
        _wristBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_Wrist);
        _thumbMetacarpal = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbMetacarpal);

        _thumbSphere.transform.position = _thumbTipBone.Transform.position;
        _indexSphere.transform.position = _indexTipBone.Transform.position;
        _middleSphere.transform.position = _middleTipBone.Transform.position;

        _worldWristRotation = _wristBone.Transform.rotation;
        _origThumbRotation = Quaternion.Inverse(_worldWristRotation) * _thumbMetacarpal.Transform.rotation;
        _origScaledThumbRotation = _origThumbRotation;
        _prevAngle = 0f;
        _prevTriangleRotation = Quaternion.identity;
        // prevCubeRotation = Quaternion.Inverse(worldWristRotation) * cube.transform.rotation;
        _prevCubeRotation = Quaternion.identity;
        _grabOffsetPosition = Vector3.zero;
        _grabOffsetRotation = Quaternion.identity;
    }

    private void ToggleComponentsVisibility(bool b)
    {
        _isComponentsVisible = b;
        foreach (GameObject sphere in _spheres)
        {
            sphere.GetComponent<Renderer>().enabled = b;
        }
        _lineRenderer.enabled = b;
    }

    private void TransferCartesian()
    {

    }

    private Vector3 TransferBoneMovement()
    {
        Quaternion worldWristRotation = _wristBone.Transform.rotation;
        Quaternion worldThumbRotation = _thumbMetacarpal.Transform.rotation;

        Vector3 thumbPosition = _wristBone.Transform.InverseTransformPoint(_thumbMetacarpal.Transform.position);
        Vector3 thumbTipPosition = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        Quaternion thumbRotation = Quaternion.Inverse(worldWristRotation) * worldThumbRotation;
        Quaternion deltaThumbRotation = thumbRotation * Quaternion.Inverse(_origThumbRotation);
        deltaThumbRotation.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle < _thumbAngleThreshold)
        {
            return thumbTipPosition;
        }

        double angleRadian = angle / 180.0 * Math.PI;
        float modifiedAngle = angle;

        if (_transferFunction == 0)
        {
            modifiedAngle = angle * _scaleFactor;
        }
        else if (_transferFunction == 1)
        {
            modifiedAngle = _powFactorA * (float)Math.Pow(angleRadian, _powFactorB) * 180f / (float)Math.PI;
        }
        else if (_transferFunction == 2)
        {
            modifiedAngle = _tanhFactorA * (float)Math.Tanh(_tanhFactorB * angleRadian) * 180f / (float)Math.PI;
        }

        Quaternion scaledDeltaRotation = Quaternion.AngleAxis(modifiedAngle, axis);
        Quaternion scaledThumbRotation = scaledDeltaRotation * _origScaledThumbRotation;

        Vector3 localTipPosition = _thumbMetacarpal.Transform.InverseTransformPoint(_thumbTipBone.Transform.position); // local based on thumb metacarpal
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

    // public Vector3 GetWeightedTriangleCentroid(Vector3 p1, Vector3 p2, Vector3 p3, float w)
    public Vector3 GetWeightedTriangleCentroid(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        // float centerX = (p1.x + p2.x + p3.x) / 3f;
        // float centerY = (p1.y + p2.y + p3.y) / 3f;
        // float centerZ = (p1.z + p2.z + p3.z) / 3f;

        // return new Vector3(centerX, centerY, centerZ);
        // return (p1 + p2 + p3) / 3f;
        return (_thumbWeight * p1 + p2 + p3) / (_thumbWeight + 1f + 1f);
        // return (p1 + w * p2 + w * p3) / (1f + w + w);
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

        if (area < _areaThreshold)
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

        if (vec1.sqrMagnitude < _magThreshold || vec2.sqrMagnitude < _magThreshold)
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
        _triangleForward = forward;
        _triangleUp = roughUp;

        if (forward.magnitude < _magThreshold || roughUp.magnitude < _magThreshold || Vector3.Dot(forward, roughUp) > _dotThreshold || Vector3.Dot(forward, roughUp) < -_dotThreshold)
        {
            orientation = Quaternion.identity;
            return false;
        }

        Vector3 normal = Vector3.Cross(forward, roughUp).normalized;

        if (normal.magnitude < _magThreshold)
        {
            orientation = Quaternion.identity;
            return false;
        }

        orientation = Quaternion.LookRotation(forward, normal);
        return true;
    }

    public void GetTriangleTransform(out Vector3 pos, out Quaternion rot)
    {
        pos = _centroidPosition;
        rot = _worldWristRotation * _triangleRotation;
    }

    public void GetTriangleTransformRaw(out Vector3 forward, out Vector3 roughUp)
    {
        forward = _triangleForward;
        roughUp = _triangleUp;
    }

    public void SetCube(GameObject cube)
    {
        _cube = cube;
    }

    public bool IsGrabbed
    {
        get { return _isGrabbed; }
        set
        {
            _isGrabbed = value;

            if (_isGrabbed)
            {
                _grabOffsetPosition = _cube.transform.position - _centroidPosition;
                _grabOffsetRotation = Quaternion.Inverse(_wristBone.Transform.rotation) * _cube.transform.rotation;
                _prevCubeRotation = Quaternion.identity;
                _outline.enabled = true;
            }
            else
            {
                _grabOffsetPosition = Vector3.zero;
                _grabOffsetRotation = Quaternion.identity;
                _outline.enabled = false;
            }
        }
    }

    public bool IsClutching
    {
        get { return _isClutching; }
        set { _isClutching = value; }
    }

    public bool IsOnTarget
    {
        get { return _isOnTarget; }
        set { _isOnTarget = value; }
    }

    public bool IsTaskComplete
    {
        get { return _isTaskComplete; }
        set
        {
            if (_isTaskComplete != value)
            {
                _isTaskComplete = value;
                if (_isTaskComplete)
                {
                    _outline.OutlineColor = Color.green;
                }
                else
                {
                    _outline.OutlineColor = Color.blue;
                }
            }
        }
    }
}
