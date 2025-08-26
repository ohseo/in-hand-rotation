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

    private bool _isGrabbed = false, _isClutching = false, _isReset = false, _isOnTarget = false, _isOverlapped = false;

    [SerializeField]
    private int _transferFunction = 0; // 0: Baseline, 1: linear, 2: accelerating(power), 3: decelerating(hyperbolic tangent)
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
    private const float OUTLINE_WIDTH_DEFAULT = 5f, OUTLINE_WIDTH_CLUTCHING = 10f;

    [SerializeField]
    private TextMeshProUGUI _textbox;
    // private string[] _transferText = { "linear", "power", "tanh" };
    private string[] _transferText = { "O", "A", "B", "C" };

    private bool _isCentroidCentered = false;
    private GameObject _centroidSphere;

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

        if (_isLeftHanded) _ovrSkeleton = _ovrLeftSkeleton;
        else _ovrSkeleton = _ovrRightSkeleton;

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
            { KeyCode.Keypad5, () => _isCentroidCentered = true},
            {KeyCode.Keypad6, () => _isCentroidCentered = false}
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
        // transforms are on the local coordinates based on the wrist if not stated otherwise
        _worldWristRotation = _wristBone.Transform.rotation;

        if (!Input.anyKey) _isReset = false;
        if (!Input.GetKey(KeyCode.Space) && _isClutching) OnClutchEnd();
        if (Input.GetKey(KeyCode.Space) && !_isClutching) OnClutchStart();

        if (Input.anyKey && !_isClutching) ResetThumbOrigin();

        foreach (var entry in _keyActions)
        {
            if (Input.GetKey(entry.Key))
            {
                entry.Value.Invoke();
            }
        }

        _textbox.text = _transferText[_transferFunction];

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
        _centroidPosition = GetWeightedTriangleCentroid(_thumbTipBone.Transform.position, _indexTipBone.Transform.position, _middleTipBone.Transform.position);

        if (_isCentroidCentered) _centroidSphere.transform.position = _centroidPosition;

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

        Quaternion scaledDeltaRotation = Quaternion.AngleAxis(modifiedAngle, axis);
        Quaternion scaledThumbRotation = scaledDeltaRotation * _origScaledThumbRotation;

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
            float weight = GetThumbWeight(angle);
            return (weight * p1 + p2 + p3) / (weight + 1f + 1f);
        }
        else return ((p1 + p2 + p3) / 3f);
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

    private float GetThumbWeight(float deg)
    {
        if (deg < 10f) return 2f;
        else if (deg > 90f) return 1f;
        else return (170f - deg) / 80f;
    }

    public void SetCube(GameObject cube)
    {
        _cube = cube;
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
    }

    public void OffTarget()
    {
        _isOnTarget = false;
    }

    public void OnRelease()
    {
        _isGrabbed = false;
        _grabOffsetPosition = Vector3.zero;
        _grabOffsetRotation = Quaternion.identity;
        _outline.enabled = false;
    }

    public void OnOverlap()
    {
        _isOverlapped = true;
        _outline.OutlineColor = Color.green;
    }

    public void OnDepart()
    {
        _isOverlapped = false;
        _outline.OutlineColor = Color.blue;
    }

    public void OnClutchStart()
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

    public void OnClutchEnd()
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

    public bool IsTaskComplete
    {
        get { return _isOverlapped; }
        set
        {
            if (_isOverlapped != value)
            {
                _isOverlapped = value;
                if (_isOverlapped)
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
