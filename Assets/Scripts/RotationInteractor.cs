using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using Unity.VisualScripting;
using System.Data;

public class RotationInteractor : MonoBehaviour
{
    [SerializeField]
    private bool _isComponentsVisible = true;
    [SerializeField]
    private bool _isShearFactorOn = true;
    private OVRSkeleton _ovrSkeleton;
    private OVRBone _indexTipBone, _middleTipBone, _thumbTipBone, _thumbMetacarpal, _wristBone;
    private OVRBone _indexMetacarpal, _indexProximal, _indexMiddle, _indexDistal;
    private OVRBone _middleMetacarpal, _middleProximal, _middleMiddle, _middleDistal;
    private OVRBone _thumbProximal, _thumbDistal;

    private GameObject _cube;

    private List<GameObject> _spheres = new List<GameObject>();
    private GameObject _thumbSphere, _indexSphere, _middleSphere;
    private float _sphereScale = 0.01f;
    private float _areaThreshold = 0.0001f, _magThreshold = 0.00001f, _dotThreshold = 0.999f;


    private LineRenderer _lineRenderer;

    private bool _isGrabbed = false, _isRotating = false, _isOnTarget = false;
    private bool _pinched = false, _dwelled = false;
    private float _clutchDwellDuration = 0f;
    private bool _isBaseline = false;
    private int _clutchingMethod = 0; // 0: pinch, 1: dwell
    // private float _powFactorA = 1.910f, _tanhFactorA = 0.547f;
    // private double _powFactorB = 2d, _tanhFactorB = 3.657d;
    private float _angleScaleFactor = 0.5f;
    private const float MIN_SCALE_FACTOR = 0.1f, MAX_SCALE_FACTOR = 2f, MIN_FLOAT = 1e-4f;
    private const float MIN_TRIANGLE_AREA = 0.5f, MAX_TRIANGLE_AREA = 7f; // area is in cm2
    private const float MIN_TRAVEL_DISTANCE = 3f, MAX_TRAVEL_DISTANCE = 10f; // distance is in cm
    private const float MAX_CURL = 180f, MAX_THUMB_CURL = 90f;
    private const float MAX_PROXIMAL_CURL = 70f, MAX_MIDDLE_CURL = 95f, MAX_DISTAL_CURL = 70f;
    private const float MAX_THUMB_PROXIMAL_CURL = 55f, MAX_THUMB_DISTAL_CURL = 45f;
    private const float MIN_FINGER_DISTANCE = 1.5f;
    private const float MIN_THUMB_ANGLE = 25f, MAX_THUMB_ANGLE = 60f;
    private const float MAX_ANGLE_BTW_FRAMES = 15f;
    private const float CLUTCH_DWELL_TIME = 1.0f, CLUTCH_DWELL_ROTATION = 1f;
    private Dictionary<KeyCode, Action> _keyActions;

    private Quaternion _origThumbRotation, _origScaledThumbRotation;
    private Quaternion _worldWristRotation, _deltaThumbRotation, _scaledDeltaRotation;
    private Quaternion _cubeRotation, _prevCubeRotation;
    private Quaternion _prevTriangleRotation, _triangleRotation;
    private float _thumbWeight, _triangleArea, _triangleP1Angle, _deltaTriangleP1Angle, _prevP1Angle;
    private float _deltaAngle;
    private Vector3 _deltaAxis;
    private Vector3 _grabOffsetPosition, _centroidPosition;
    private Quaternion _grabOffsetRotation;
    private Vector3 _triangleForward, _triangleUp;
    private Vector3 _origThumbPosition, _origIndexPosition, _origMiddlePosition;

    private Renderer _dieRenderer;
    private const float DEFAULT_TRANSPARENCY = 0.75f;
    private Outline _outline;
    private const float OUTLINE_WIDTH_CLUTCHING = 10f, OUTLINE_WIDTH_DEFAULT = 3f;

    [SerializeField]
    private TextMeshProUGUI _textbox;

    private string _tempstr = "";

    private bool _isCentroidCentered = true;
    private GameObject _centroidSphere;

    public event Action OnClutchEnd, OnClutchStart;

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

        if (_isCentroidCentered)
        {
            _centroidSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _centroidSphere.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);
            if (!_isComponentsVisible)
            {
                _centroidSphere.GetComponent<Renderer>().enabled = false;
            }
        }

        _thumbSphere = _spheres[0];
        _indexSphere = _spheres[1];
        _middleSphere = _spheres[2];

        Renderer[] rList = _cube.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in rList)
        {
            if (string.Equals(r.name, "default")) _dieRenderer = r;
        }

        InitGeometry();

        _keyActions = new Dictionary<KeyCode, Action>
        {
            {KeyCode.KeypadEnter, () => _cube.transform.rotation = _wristBone.Transform.rotation},
            {KeyCode.KeypadMinus, () => ToggleComponentsVisibility(false)},
            {KeyCode.KeypadPlus, () => ToggleComponentsVisibility(true)},
            {KeyCode.Keypad0, () => _isBaseline = true},
            {KeyCode.Keypad1, () => _isBaseline = false},
            {KeyCode.Keypad5, () => _isCentroidCentered = true},
            {KeyCode.Keypad6, () => _isCentroidCentered = false},
            {KeyCode.Keypad2, () => _clutchingMethod = 0},
            {KeyCode.Keypad3, () => _clutchingMethod = 1}
        };

        _cube.GetComponentInChildren<DieGrabHandler>().SetRotationInteractor(this);
        _cube.GetComponentInChildren<DieReleaseHandler>().SetRotationInteractor(this);
        _outline = _cube.GetComponentInChildren<Outline>();
        _outline.OutlineColor = Color.blue;
        _outline.enabled = false;
    }

    // Update is called once per frame
    void Update()
    {
        foreach (var entry in _keyActions) if (Input.GetKey(entry.Key)) entry.Value.Invoke();

        // transforms are on the local coordinates based on the wrist if not stated otherwise
        _worldWristRotation = _wristBone.Transform.rotation;

        Vector3 ThumbTipPosition = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        Vector3 indexTipPosition = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        Vector3 middleTipPosition = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        _thumbSphere.transform.position = _thumbTipBone.Transform.position;
        _indexSphere.transform.position = _indexTipBone.Transform.position;
        _middleSphere.transform.position = _middleTipBone.Transform.position;

        _lineRenderer.SetPosition(0, _thumbTipBone.Transform.position);
        _lineRenderer.SetPosition(1, _indexTipBone.Transform.position);
        _lineRenderer.SetPosition(2, _middleTipBone.Transform.position);
        _lineRenderer.SetPosition(3, _thumbTipBone.Transform.position);

        bool isAngleValid = CalculateAngleAtVertex(ThumbTipPosition, indexTipPosition, middleTipPosition, out _triangleP1Angle);
        bool isTriangleValid = CalculateTriangleOrientation(ThumbTipPosition, indexTipPosition, middleTipPosition, out _triangleRotation);
        bool isTriangleAreaValid = CalculateTriangleArea(ThumbTipPosition, indexTipPosition, middleTipPosition, out float _triangleArea);
        // position cube with bones since spheres are modified. use world coordinates here.
        _centroidPosition = GetWeightedTriangleCentroid(_thumbTipBone.Transform.position, _indexTipBone.Transform.position, _middleTipBone.Transform.position);
        CalculateFingerDistance(out float indexDistance, out float middleDistance);
        float distance = GetFingerTravelDistance();
        if (isTriangleAreaValid) _angleScaleFactor = GetScaleFactorFromArea(_triangleArea);

        // calculate the rotation
        if (isAngleValid && isTriangleValid && isTriangleAreaValid)
        {
            _deltaTriangleP1Angle = _triangleP1Angle - _prevP1Angle;
            Vector3 triangleAxis = _triangleRotation * Vector3.up;
            Quaternion deltaShearRotation, deltaTriangleRotation, deltaTotalRotation;
            if (_isShearFactorOn) deltaShearRotation = Quaternion.AngleAxis(_deltaTriangleP1Angle, triangleAxis);
            else deltaShearRotation = Quaternion.identity;
            deltaTriangleRotation = _triangleRotation * Quaternion.Inverse(_prevTriangleRotation);
            deltaTotalRotation = deltaShearRotation * deltaTriangleRotation;
            deltaTotalRotation.ToAngleAxis(out _deltaAngle, out _deltaAxis);
            _prevP1Angle = _triangleP1Angle;
            _prevTriangleRotation = _triangleRotation;
        }

        // check clutching
        if (IsIndexFingerCurled() || IsMiddleFingerCurled() || IsThumbCurled()) _pinched = true;
        if ((indexDistance < MIN_FINGER_DISTANCE) && (middleDistance < MIN_FINGER_DISTANCE)) _pinched = true;
        if (_deltaAngle < CLUTCH_DWELL_ROTATION)
        {
            _clutchDwellDuration += Time.deltaTime;
        }
        if (_clutchDwellDuration > CLUTCH_DWELL_TIME) _dwelled = true;
        else _dwelled = false;

        if (_isBaseline) _isRotating = false;
        else if (_clutchingMethod == 0)
        {
            if (_pinched && _isRotating) OnClutchStart?.Invoke();
            if (!_pinched && !_isRotating) OnClutchEnd?.Invoke();
        }
        else if (_clutchingMethod == 1)
        {
            if (_dwelled && _isRotating) OnClutchStart?.Invoke();
            if (!_dwelled && !_isRotating) OnClutchEnd?.Invoke();
        }

        if (_isCentroidCentered) _centroidSphere.transform.position = _centroidPosition;

        if (_isGrabbed)
        {
            if (_isRotating)
            {
                if (_deltaAngle < MAX_ANGLE_BTW_FRAMES)
                {
                    Quaternion deltaScaledRotation = Quaternion.AngleAxis(_deltaAngle * _angleScaleFactor, _deltaAxis);
                    _cubeRotation = deltaScaledRotation * _prevCubeRotation;
                    _cube.transform.rotation = _worldWristRotation * _cubeRotation * _grabOffsetRotation;
                    Color c = _dieRenderer.material.color;
                    c.a = 1f - _angleScaleFactor / MAX_SCALE_FACTOR;
                    _dieRenderer.material.color = c;
                }
                _prevCubeRotation = _cubeRotation;

                // Color c = _dieRenderer.material.color;
                // if (_clutchDwellDuration > 0.4f)
                // {
                //     c.a = 1f;
                // }
                // else if (_clutchDwellDuration > 0.3f)
                // {
                //     c.a = 0.75f;
                // }
                // else
                // {
                //     c.a = 0.5f;
                // }
                // _dieRenderer.material.color = c;
            }
            else
            {
                _cubeRotation = _prevCubeRotation;
                _cube.transform.rotation = _worldWristRotation * _cubeRotation * _grabOffsetRotation;
            }
            if (_isCentroidCentered) _cube.transform.position = _worldWristRotation * _cubeRotation * Quaternion.Inverse(_worldWristRotation) * _grabOffsetPosition + _centroidPosition;
            else _cube.transform.position = _grabOffsetPosition + _centroidPosition;
        }
    }

    public void Reset()
    {
        OffTarget();
        EndClutching();
        OnRelease();
        InitGeometry();
    }

    private void ResetThumbOrigin()
    {
        _origThumbRotation = Quaternion.Inverse(_worldWristRotation) * _thumbMetacarpal.Transform.rotation;
        _origScaledThumbRotation = _origThumbRotation;
    }

    private void ResetFingersOrigin()
    {
        _origThumbPosition = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        _origIndexPosition = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        _origMiddlePosition = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);
    }

    private void ResetGrabOffset()
    {
        _grabOffsetPosition = _cube.transform.position - _centroidPosition;
        _grabOffsetRotation = Quaternion.Inverse(_wristBone.Transform.rotation) * _cube.transform.rotation;
        _prevCubeRotation = Quaternion.identity;
    }

    private void InitGeometry()
    {
        _indexTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip);
        _middleTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleTip);
        _thumbTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbTip);
        _wristBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_Wrist);
        _thumbMetacarpal = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbMetacarpal);

        _thumbProximal = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbProximal);
        _thumbDistal = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbDistal);
        _indexMetacarpal = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexMetacarpal);
        _indexProximal = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexProximal);
        _indexMiddle = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexIntermediate);
        _indexDistal = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexDistal);
        _middleMetacarpal = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleMetacarpal);
        _middleProximal = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleProximal);
        _middleMiddle = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleIntermediate);
        _middleDistal = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleDistal);

        _thumbSphere.transform.position = _thumbTipBone.Transform.position;
        _indexSphere.transform.position = _indexTipBone.Transform.position;
        _middleSphere.transform.position = _middleTipBone.Transform.position;

        _worldWristRotation = _wristBone.Transform.rotation;
        ResetThumbOrigin();
        ResetFingersOrigin();
        _prevP1Angle = 0f;
        _deltaAngle = 0f;
        _prevTriangleRotation = Quaternion.identity;
        // prevCubeRotation = Quaternion.Inverse(worldWristRotation) * cube.transform.rotation;
        _prevCubeRotation = Quaternion.identity;
        _grabOffsetPosition = Vector3.zero;
        _grabOffsetRotation = Quaternion.identity;

        Color c = _dieRenderer.material.color;
        c.a = DEFAULT_TRANSPARENCY;
        _dieRenderer.material.color = c;
    }

    private void ToggleComponentsVisibility(bool b)
    {
        _isComponentsVisible = b;
        foreach (GameObject sphere in _spheres)
        {
            sphere.GetComponent<Renderer>().enabled = b;
        }
        if (_isCentroidCentered)
        {
            _centroidSphere.GetComponent<Renderer>().enabled = b;
        }
        _lineRenderer.enabled = b;
    }

    public Vector3 GetTriangleCentroid(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        return (p1 + p2 + p3) / 3f;
    }

    public Vector3 GetWeightedTriangleCentroid(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        if (CalculateAngleAtVertex(p1, p2, p3, out float angle))
        {
            // _thumbWeight = GetThumbWeight(angle);
            // return (_thumbWeight * p1 + p2 + p3) / (_thumbWeight + 1f + 1f);
            // return (3.0f * p1 + 1.5f * p2 + 1.0f * p3) / 5.5f;

            // float alpha = 1.5f;
            // float wT = Mathf.Pow(Vector3.Distance(p1, p2) + Vector3.Distance(p1, p3), alpha);
            // float wI = Mathf.Pow(Vector3.Distance(p2, p1) + Vector3.Distance(p2, p3), alpha);
            // float wM = Mathf.Pow(Vector3.Distance(p3, p1) + Vector3.Distance(p3, p2), alpha);

            float wT = Angle(p1, p2, p3);
            float wI = Angle(p2, p3, p1);
            float wM = Angle(p3, p1, p2);
            return (p1 / wT + p2 / wI + p3 / wM) / (1 / wT + 1 / wI + 1 / wM);
        }
        else return (p1 + p2 + p3) / 3f;
    }

    float Angle(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = (b - a).normalized;
        Vector3 ac = (c - a).normalized;
        return Vector3.Angle(ab, ac);
    }
    public float GetScaleFactorFromArea(float area)
    {
        // if (area < MIN_TRIANGLE_AREA) { _pinched = true; _outline.OutlineWidth = OUTLINE_WIDTH_CLUTCHING; return MIN_SCALE_FACTOR; }
        if (area < MIN_TRIANGLE_AREA) return MIN_SCALE_FACTOR;
        else if (area > MAX_TRIANGLE_AREA) return MAX_SCALE_FACTOR;
        else return (MAX_SCALE_FACTOR - MIN_SCALE_FACTOR) / (MAX_TRIANGLE_AREA - MIN_TRIANGLE_AREA) * (area - MIN_TRIANGLE_AREA) + MIN_SCALE_FACTOR;
    }

    public float GetScaleFactorFromFingers(float distance)
    {
        if (distance < MIN_TRAVEL_DISTANCE) return MIN_SCALE_FACTOR;
        else if (distance > MAX_TRAVEL_DISTANCE) return MAX_SCALE_FACTOR;
        else return (MAX_SCALE_FACTOR - MIN_SCALE_FACTOR) / (MAX_TRAVEL_DISTANCE - MIN_TRAVEL_DISTANCE) * (distance - MIN_TRAVEL_DISTANCE) + MIN_SCALE_FACTOR;
    }

    public float GetHarmonicMean(float a, float b)
    {
        return 2f / (1f / a + 1f / b);
    }

    public bool CalculateTriangleArea(Vector3 p1, Vector3 p2, Vector3 p3, out float area)
    {
        Vector3 vectorAB = (p2 - p1) * 100f;
        Vector3 vectorAC = (p3 - p1) * 100f;
        Vector3 vectorBC = (p3 - p2) * 100f;

        if (vectorAB.sqrMagnitude < MIN_FLOAT || vectorAC.sqrMagnitude < MIN_FLOAT
            || vectorBC.sqrMagnitude < MIN_FLOAT)
        {
            area = float.NaN;
            return false;
        }
        // _textbox.text = $"{vectorAB.magnitude}\n {vectorAC.magnitude}\n {vectorBC.magnitude}";

        Vector3 crossProduct = Vector3.Cross(vectorAB, vectorAC);

        area = crossProduct.magnitude / 2f;

        // _textbox.text = $"{area}";

        if (area < MIN_FLOAT)
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
    private float GetThumbWeight(float deg)
    {
        if (deg < 10f) return 2f;
        else if (deg > 90f) return 1f;
        else return (170f - deg) / 80f;
    }

    private float GetIndexFingerCurl()
    {
        Vector3 metacarpalDir = (_indexProximal.Transform.position - _indexMetacarpal.Transform.position).normalized;
        Vector3 proximalDir = (_indexMiddle.Transform.position - _indexProximal.Transform.position).normalized;
        Vector3 middleDir = (_indexDistal.Transform.position - _indexMiddle.Transform.position).normalized;
        Vector3 distalDir = (_indexTipBone.Transform.position - _indexDistal.Transform.position).normalized;

        float proximalAngle = Vector3.Angle(metacarpalDir, proximalDir);
        float middleAngle = Vector3.Angle(proximalDir, middleDir);
        float distalAngle = Vector3.Angle(middleDir, distalDir);

        float rawCurl = proximalAngle + middleAngle + distalAngle;

        return rawCurl;
    }

    private bool IsIndexFingerCurled()
    {
        Vector3 metacarpalDir = (_indexProximal.Transform.position - _indexMetacarpal.Transform.position).normalized;
        Vector3 proximalDir = (_indexMiddle.Transform.position - _indexProximal.Transform.position).normalized;
        Vector3 middleDir = (_indexDistal.Transform.position - _indexMiddle.Transform.position).normalized;
        Vector3 distalDir = (_indexTipBone.Transform.position - _indexDistal.Transform.position).normalized;

        float proximalAngle = Vector3.Angle(metacarpalDir, proximalDir);
        float middleAngle = Vector3.Angle(proximalDir, middleDir);
        float distalAngle = Vector3.Angle(middleDir, distalDir);

        return (proximalAngle > MAX_PROXIMAL_CURL) || (middleAngle > MAX_MIDDLE_CURL);
    }

    private float GetMiddleFingerCurl()
    {
        Vector3 metacarpalDir = (_middleProximal.Transform.position - _middleMetacarpal.Transform.position).normalized;
        Vector3 proximalDir = (_middleMiddle.Transform.position - _middleProximal.Transform.position).normalized;
        Vector3 middleDir = (_middleDistal.Transform.position - _middleMiddle.Transform.position).normalized;
        Vector3 distalDir = (_middleTipBone.Transform.position - _middleDistal.Transform.position).normalized;

        float proximalAngle = Vector3.Angle(metacarpalDir, proximalDir);
        float middleAngle = Vector3.Angle(proximalDir, middleDir);
        float distalAngle = Vector3.Angle(middleDir, distalDir);

        float rawCurl = proximalAngle + middleAngle + distalAngle;

        return rawCurl;
    }

    private bool IsMiddleFingerCurled()
    {
        Vector3 metacarpalDir = (_middleProximal.Transform.position - _middleMetacarpal.Transform.position).normalized;
        Vector3 proximalDir = (_middleMiddle.Transform.position - _middleProximal.Transform.position).normalized;
        Vector3 middleDir = (_middleDistal.Transform.position - _middleMiddle.Transform.position).normalized;
        Vector3 distalDir = (_middleTipBone.Transform.position - _middleDistal.Transform.position).normalized;

        float proximalAngle = Vector3.Angle(metacarpalDir, proximalDir);
        float middleAngle = Vector3.Angle(proximalDir, middleDir);
        float distalAngle = Vector3.Angle(middleDir, distalDir);

        return (proximalAngle > MAX_PROXIMAL_CURL) || (middleAngle > MAX_MIDDLE_CURL);
    }

    private float GetThumbCurl()
    {
        Vector3 metacarpalDir = (_thumbProximal.Transform.position - _thumbMetacarpal.Transform.position).normalized;
        Vector3 proximalDir = (_thumbDistal.Transform.position - _thumbProximal.Transform.position).normalized;
        Vector3 distalDir = (_thumbTipBone.Transform.position - _thumbDistal.Transform.position).normalized;

        float proximalAngle = Vector3.Angle(metacarpalDir, proximalDir);
        float distalAngle = Vector3.Angle(proximalDir, distalDir);

        float rawCurl = proximalAngle + distalAngle;

        return rawCurl;
    }

    private bool IsThumbCurled()
    {
        Vector3 metacarpalDir = (_thumbProximal.Transform.position - _thumbMetacarpal.Transform.position).normalized;
        Vector3 proximalDir = (_thumbDistal.Transform.position - _thumbProximal.Transform.position).normalized;
        Vector3 distalDir = (_thumbTipBone.Transform.position - _thumbDistal.Transform.position).normalized;

        float proximalAngle = Vector3.Angle(metacarpalDir, proximalDir);
        float distalAngle = Vector3.Angle(proximalDir, distalDir);

        return (proximalAngle > MAX_THUMB_PROXIMAL_CURL) || (distalAngle > MAX_THUMB_DISTAL_CURL);
    }

    private void CalculateFingerDistance(out float index, out float middle)
    {
        Vector3 thumbIndex = _thumbTipBone.Transform.position - _indexTipBone.Transform.position;
        Vector3 thumbMiddle = _thumbTipBone.Transform.position - _middleTipBone.Transform.position;

        index = thumbIndex.magnitude * 100f;
        middle = thumbMiddle.magnitude * 100f;
    }

    private float GetThumbAngle()
    {
        Vector3 thumb = (_thumbProximal.Transform.position - _thumbMetacarpal.Transform.position).normalized;
        Vector3 index = (_indexProximal.Transform.position - _indexMetacarpal.Transform.position).normalized;
        float angle = Vector3.Angle(thumb, index);
        return angle;
    }

    private float GetFingerTravelDistance()
    {
        Vector3 thumb = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        Vector3 index = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        Vector3 middle = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        float deltaThumb = (thumb - _origThumbPosition).magnitude * 100f;
        float deltaIndex = (index - _origIndexPosition).magnitude * 100f;
        float deltaMiddle = (middle - _origMiddlePosition).magnitude * 100f;

        return deltaThumb + deltaIndex + deltaMiddle;
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

    public void GetDieLocalTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _wristBone.Transform.InverseTransformPoint(_cube.transform.position);
        rotation = Quaternion.Inverse(_worldWristRotation) * _cube.transform.rotation;
    }

    public void GetWristWorldTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _wristBone.Transform.position;
        rotation = _wristBone.Transform.rotation;
    }

    public Transform GetWristWorldTransform()
    {
        return _wristBone.Transform;
    }

    public void GetThumbTipWorldTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _thumbTipBone.Transform.position;
        rotation = _thumbTipBone.Transform.rotation;
    }

    public void GetIndexTipWorldTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _indexTipBone.Transform.position;
        rotation = _indexTipBone.Transform.rotation;
    }

    public void GetMiddleTipWorldTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _middleTipBone.Transform.position;
        rotation = _middleTipBone.Transform.rotation;
    }

    public void GetMetacarpalWorldTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _thumbMetacarpal.Transform.position;
        rotation = _thumbMetacarpal.Transform.rotation;
    }

    public void GetThumbTipLocalTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        rotation = Quaternion.Inverse(_worldWristRotation) * _thumbTipBone.Transform.rotation;
    }

    public void GetIndexTipLocalTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        rotation = Quaternion.Inverse(_worldWristRotation) * _indexTipBone.Transform.rotation;
    }

    public void GetMiddleTipLocalTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);
        rotation = Quaternion.Inverse(_worldWristRotation) * _middleTipBone.Transform.rotation;
    }

    public void GetMetacarpalLocalTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _wristBone.Transform.InverseTransformPoint(_thumbMetacarpal.Transform.position);
        rotation = Quaternion.Inverse(_worldWristRotation) * _thumbMetacarpal.Transform.rotation;
    }

    public void GetDeltaMetacarpalRotation(out Quaternion rotation, out float angle, out Vector3 axis)
    {
        rotation = _deltaThumbRotation;
        _deltaThumbRotation.ToAngleAxis(out angle, out axis);
    }

    public void GetModifiedDeltaMetacarpalRotation(out Quaternion rotation, out float angle, out Vector3 axis)
    {
        rotation = _scaledDeltaRotation;
        _scaledDeltaRotation.ToAngleAxis(out angle, out axis);
    }

    public void GetTriangleWorldRotation(out Quaternion rotation)
    {
        rotation = _worldWristRotation * _triangleRotation;
    }

    public void GetTriangleLocalRotation(out Quaternion rotation)
    {
        rotation = _triangleRotation;
    }

    public void GetWeightedCentroidWorldPosition(out Vector3 position)
    {
        position = _centroidPosition;
    }

    public void GetWeightedCentroidLocalPosition(out Vector3 position)
    {
        position = _wristBone.Transform.InverseTransformPoint(_centroidPosition);
    }

    public void GetTriangleProperties(out float weight, out float area, out float angle, out float deltaAngle)
    {
        weight = _thumbWeight;
        area = _triangleArea;
        angle = _triangleP1Angle;
        deltaAngle = _deltaTriangleP1Angle;
    }

    public void SetCube(GameObject cube)
    {
        _cube = cube;
    }

    public void SetBaseline(bool b)
    {
        _isBaseline = b;
    }

    public void SetOVRSkeleton(OVRSkeleton s)
    {
        _ovrSkeleton = s;
    }

    public void OnGrab()
    {
        _isGrabbed = true;
        ResetGrabOffset();
        ResetFingersOrigin();
        _outline.enabled = true;
        _pinched = false;
        _dwelled = false;
        _clutchDwellDuration = 0f;
    }

    public void OnTarget()
    {
        _isOnTarget = true;
        _outline.OutlineColor = Color.green;
    }

    public void OffTarget()
    {
        _isOnTarget = false;
        _outline.OutlineColor = Color.blue;
    }

    public void OnRelease()
    {
        _isGrabbed = false;
        _grabOffsetPosition = Vector3.zero;
        _grabOffsetRotation = Quaternion.identity;
        // _outline.OutlineWidth = OUTLINE_WIDTH_DEFAULT;
        _outline.enabled = false;
        _pinched = false;
        _dwelled = false;
        _clutchDwellDuration = 0f;
    }

    public void EndClutching()
    {
        ResetThumbOrigin();
        ResetFingersOrigin();

        if (_isGrabbed)
        {
            ResetGrabOffset();
            _cubeRotation = Quaternion.identity;
        }
        _outline.OutlineWidth = OUTLINE_WIDTH_DEFAULT;
        _isRotating = true;
    }

    public void StartClutching()
    {
        _outline.OutlineWidth = OUTLINE_WIDTH_CLUTCHING;
        _isRotating = false;
        _tempstr = "";
    }

    public bool IsGrabbed
    {
        get { return _isGrabbed; }
        set { _isGrabbed = value; }
    }

    public bool IsRotating
    {
        get { return _isRotating; }
        set { _isRotating = value; }
    }

    public bool IsOnTarget
    {
        get { return _isOnTarget; }
        set { _isOnTarget = value; }
    }    
}
