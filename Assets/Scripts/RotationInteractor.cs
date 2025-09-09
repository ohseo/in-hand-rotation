using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

public class RotationInteractor : MonoBehaviour
{
    [SerializeField]
    private bool _isComponentsVisible = true;
    [SerializeField]
    private bool _isShearFactorOn = true;
    private OVRSkeleton _ovrSkeleton;
    private OVRBone _indexTipBone, _middleTipBone, _thumbTipBone, _thumbMetacarpal, _wristBone;

    private GameObject _cube;

    private List<GameObject> _spheres = new List<GameObject>();
    private GameObject _thumbSphere, _indexSphere, _middleSphere;
    private float _sphereScale = 0.01f;
    private float _areaThreshold = 0.0001f, _magThreshold = 0.00001f, _dotThreshold = 0.999f;
    private float _thumbAngleThreshold = 0.01f, _triAngleThreshold = 30f;

    private LineRenderer _lineRenderer;

    private bool _isGrabbed = false, _isClutching = false, _isReset = false, _isOnTarget = false;
    private int _scaleMode = 0; // 0: angle-based, 1: cmc-based
    private int _transferFunction = 0; // 0: Baseline, 1: linear, 2: accelerating(power), 3: decelerating(hyperbolic tangent)
    private float _powFactorA = 1.910f, _tanhFactorA = 0.547f;
    private double _powFactorB = 2d, _tanhFactorB = 3.657d;
    private float _angleScaleFactor = 0.5f;
    private const float MIN_SCALE_FACTOR = 0.5f, MAX_SCALE_FACTOR = 2.0f, MIN_FLOAT = 1e-4f;
    private const float MIN_TRIANGLE_AREA = 1f, MAX_TRIANGLE_AREA = 50f; // area is in cm2
    private Dictionary<KeyCode, Action> _keyActions;

    private Quaternion _origThumbRotation, _origScaledThumbRotation;
    private Quaternion _worldWristRotation, _deltaThumbRotation, _scaledDeltaRotation;
    private Vector3 _scaledWorldThumbTipPosition, _scaledThumbTipPosition;
    private Quaternion _cubeRotation, _prevCubeRotation;
    private Quaternion _prevTriangleRotation, _triangleRotation;
    private float _thumbWeight, _triangleArea, _triangleP1Angle, _deltaTriangleP1Angle, _prevAngle;
    private Vector3 _grabOffsetPosition, _centroidPosition;
    private Quaternion _grabOffsetRotation;
    Vector3 _triangleForward, _triangleUp;

    private Outline _outline;
    private const float OUTLINE_WIDTH_DEFAULT = 5f, OUTLINE_WIDTH_CLUTCHING = 10f;

    [SerializeField]
    private TextMeshProUGUI _textbox;
    // private string[] _transferText = { "linear", "power", "tanh" };
    private string[] _transferText = { "O", "A", "B", "C" };

    private bool _isCentroidCentered = false;
    private GameObject _centroidSphere;

    public event Action OnClutchStart, OnClutchEnd;

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

        InitGeometry();

        _keyActions = new Dictionary<KeyCode, Action>
        {
            {KeyCode.KeypadEnter, () => _cube.transform.rotation = _wristBone.Transform.rotation},
            {KeyCode.KeypadMinus, () => ToggleComponentsVisibility(false)},
            {KeyCode.KeypadPlus, () => ToggleComponentsVisibility(true)},
            {KeyCode.Keypad0, () => _transferFunction = 0},
            {KeyCode.Keypad1, () => _transferFunction = 1},
            {KeyCode.Keypad2, () => _transferFunction = 2},
            {KeyCode.Keypad3, () => _transferFunction = 3},
            {KeyCode.Keypad5, () => _isCentroidCentered = true},
            {KeyCode.Keypad6, () => _isCentroidCentered = false},
            {KeyCode.Keypad7, () => _scaleMode = 0},
            {KeyCode.Keypad8, () => _scaleMode = 1}
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
        if (_transferFunction == 0) _isClutching = false;
        else
        {
            if (!Input.anyKey) _isReset = false;
            if (!Input.GetKey(KeyCode.Space) && _isClutching) OnClutchEnd?.Invoke();
            if (Input.GetKey(KeyCode.Space) && !_isClutching) OnClutchStart?.Invoke();
            if (Input.anyKey && !_isClutching) ResetThumbOrigin();
        }

        foreach (var entry in _keyActions) if (Input.GetKey(entry.Key)) entry.Value.Invoke();

        // _textbox.text = _transferText[_transferFunction];

        // transforms are on the local coordinates based on the wrist if not stated otherwise
        _worldWristRotation = _wristBone.Transform.rotation;

        if (_scaleMode == 1) _scaledThumbTipPosition = TransferBoneMovement();
        else _scaledThumbTipPosition = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);

        Vector3 indexTipPosition = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        Vector3 middleTipPosition = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);
        _scaledWorldThumbTipPosition = _wristBone.Transform.TransformPoint(_scaledThumbTipPosition);

        // _thumbSphere.transform.position = worldThumbPosition;
        // _thumbSphere.transform.position = _thumbTipBone.Transform.position;
        _thumbSphere.transform.position = _scaledWorldThumbTipPosition;
        _indexSphere.transform.position = _indexTipBone.Transform.position;
        _middleSphere.transform.position = _middleTipBone.Transform.position;

        _lineRenderer.SetPosition(0, _scaledWorldThumbTipPosition);
        _lineRenderer.SetPosition(1, _indexTipBone.Transform.position);
        _lineRenderer.SetPosition(2, _middleTipBone.Transform.position);
        _lineRenderer.SetPosition(3, _scaledWorldThumbTipPosition);

        bool isAngleValid = CalculateAngleAtVertex(_scaledThumbTipPosition, indexTipPosition, middleTipPosition, out _triangleP1Angle);
        bool isTriangleValid = CalculateTriangleOrientation(_scaledThumbTipPosition, indexTipPosition, middleTipPosition, out _triangleRotation);
        bool isTriangleAreaValid = CalculateTriangleArea(_scaledThumbTipPosition, indexTipPosition, middleTipPosition, out float _triangleArea);
        // position cube with bones since spheres are modified. use world coordinates here.
        _centroidPosition = GetWeightedTriangleCentroid(_thumbTipBone.Transform.position, _indexTipBone.Transform.position, _middleTipBone.Transform.position);
        // _centroidPosition = GetWeightedTriangleCentroid(_scaledWorldThumbTipPosition, _indexTipBone.Transform.position, _middleTipBone.Transform.position);
        if (isTriangleAreaValid) _angleScaleFactor = GetScaleFactorFromArea(_triangleArea);

        _textbox.text = $"{_angleScaleFactor}";
        

        if (_isCentroidCentered) _centroidSphere.transform.position = _centroidPosition;

        if (_isGrabbed)
        {
            if (_isClutching)
            {
                if (_isReset)
                {
                    if (isAngleValid && isTriangleValid && isTriangleAreaValid)
                    {
                        _deltaTriangleP1Angle = _triangleP1Angle - _prevAngle;
                        Vector3 triangleAxis = _triangleRotation * Vector3.up;
                        Quaternion deltaShearRotation, deltaTriangleRotation;
                        if (_isShearFactorOn) deltaShearRotation = Quaternion.AngleAxis(_deltaTriangleP1Angle, triangleAxis);
                        else deltaShearRotation = Quaternion.identity;
                        deltaTriangleRotation = _triangleRotation * Quaternion.Inverse(_prevTriangleRotation);
                        deltaTriangleRotation.ToAngleAxis(out float deltaAngle, out Vector3 deltaAxis);
                        if (deltaAngle < _triAngleThreshold)
                        {
                            if (_scaleMode == 0) deltaTriangleRotation = Quaternion.AngleAxis(deltaAngle * _angleScaleFactor, deltaAxis);
                            _cubeRotation = deltaShearRotation * deltaTriangleRotation * _prevCubeRotation;
                            _cube.transform.rotation = _worldWristRotation * _cubeRotation * _grabOffsetRotation;
                        }
                        _prevCubeRotation = _cubeRotation;
                    }
                }
                else _isReset = true;
            }
            else
            {
                _cubeRotation = _prevCubeRotation;
                _cube.transform.rotation = _worldWristRotation * _cubeRotation * _grabOffsetRotation;
            }
            if (_isCentroidCentered) _cube.transform.position = _worldWristRotation * _cubeRotation * Quaternion.Inverse(_worldWristRotation) * _grabOffsetPosition + _centroidPosition;
            else _cube.transform.position = _grabOffsetPosition + _centroidPosition;
        }

        if (isAngleValid && isTriangleValid && isTriangleAreaValid)
        {
            _prevAngle = _triangleP1Angle;
            _prevTriangleRotation = _triangleRotation;
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

        _thumbSphere.transform.position = _thumbTipBone.Transform.position;
        _indexSphere.transform.position = _indexTipBone.Transform.position;
        _middleSphere.transform.position = _middleTipBone.Transform.position;

        _worldWristRotation = _wristBone.Transform.rotation;
        ResetThumbOrigin();
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
        if (_isCentroidCentered)
        {
            _centroidSphere.GetComponent<Renderer>().enabled = b;
        }
        _lineRenderer.enabled = b;
    }

    private Vector3 TransferBoneMovement()
    {
        Quaternion worldThumbRotation = _thumbMetacarpal.Transform.rotation;

        Vector3 thumbPosition = _wristBone.Transform.InverseTransformPoint(_thumbMetacarpal.Transform.position);
        Vector3 thumbTipPosition = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        Quaternion thumbRotation = Quaternion.Inverse(_worldWristRotation) * worldThumbRotation;
        _deltaThumbRotation = thumbRotation * Quaternion.Inverse(_origThumbRotation);
        _deltaThumbRotation.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle < _thumbAngleThreshold) return thumbTipPosition;
        if (_transferFunction == 0) return thumbTipPosition;

        double angleRadian = angle / 180.0 * Math.PI;
        float modifiedAngle = angle;

        if (_transferFunction == 1)
        {
            modifiedAngle = angle;
        }
        else if (_transferFunction == 2)
        {
            modifiedAngle = _powFactorA * (float)Math.Pow(angleRadian, _powFactorB) * 180f / (float)Math.PI;
        }
        else if (_transferFunction == 3)
        {
            modifiedAngle = _tanhFactorA * (float)Math.Tanh(_tanhFactorB * angleRadian) * 180f / (float)Math.PI;
        }

        _scaledDeltaRotation = Quaternion.AngleAxis(modifiedAngle, axis);
        Quaternion scaledThumbRotation = _scaledDeltaRotation * _origScaledThumbRotation;

        Vector3 localTipPosition = _thumbMetacarpal.Transform.InverseTransformPoint(_thumbTipBone.Transform.position); // local based on thumb metacarpal
        Vector3 scaledTipPosition = thumbPosition + scaledThumbRotation * localTipPosition;

        return scaledTipPosition;
    }

    public Vector3 GetTriangleCentroid(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        return (p1 + p2 + p3) / 3f;
    }

    public Vector3 GetWeightedTriangleCentroid(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        if (CalculateAngleAtVertex(p1, p2, p3, out float angle))
        {
            _thumbWeight = GetThumbWeight(angle);
            return (_thumbWeight * p1 + p2 + p3) / (_thumbWeight + 1f + 1f);
        }
        else return (p1 + p2 + p3) / 3f;
    }

    public float GetScaleFactorFromArea(float area)
    {
        if (area < MIN_TRIANGLE_AREA) return MIN_SCALE_FACTOR;
        else if (area > MAX_TRIANGLE_AREA) return MAX_SCALE_FACTOR;
        else return (MAX_SCALE_FACTOR - MIN_SCALE_FACTOR) / (MAX_TRIANGLE_AREA - MIN_TRIANGLE_AREA) * (area - MIN_TRIANGLE_AREA) + MIN_SCALE_FACTOR;
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

    public void GetModifiedThumbTipWorldPosition(out Vector3 position)
    {
        position = _scaledWorldThumbTipPosition;
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

    public void GetModifiedThumbTipLocalPosition(out Vector3 position)
    {
        position = _scaledThumbTipPosition;
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

    public void SetTransferFunction(int i)
    {
        _transferFunction = i;
    }

    public void SetOVRSkeleton(OVRSkeleton s)
    {
        _ovrSkeleton = s;
    }

    public void SetScaleMode(int i)
    {
        _scaleMode = i;
    }

    public void OnGrab()
    {
        _isGrabbed = true;
        ResetGrabOffset();
        _outline.enabled = true;
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
        _outline.enabled = false;
    }

    public void StartClutching()
    {
        ResetThumbOrigin();

        if (_isGrabbed)
        {
            ResetGrabOffset();
            _cubeRotation = Quaternion.identity;
        }
        _outline.OutlineWidth = OUTLINE_WIDTH_CLUTCHING;
        _isClutching = true;
    }

    public void EndClutching()
    {
        _outline.OutlineWidth = OUTLINE_WIDTH_DEFAULT;
        _isClutching = false;
    }

    public bool IsGrabbed
    {
        get { return _isGrabbed; }
        set { _isGrabbed = value; }
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
}
