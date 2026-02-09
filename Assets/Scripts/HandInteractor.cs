using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;
using Unity.VisualScripting;
using System.Numerics;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
using System.Runtime.CompilerServices;

public class HandInteractor : MonoBehaviour
{
    // [SerializeField]
    // private TextMeshProUGUI _textbox;
    [SerializeField]
    private bool _isInDebugMode;
    private Dictionary<KeyCode, Action> _keyActions;
    [SerializeField]
    private OVRSkeleton _ovrSkeleton;
    private OVRBone _indexTipBone, _middleTipBone, _thumbTipBone, _wristBone;
    [SerializeField]
    private LayerMask _interactableLayer;
    private GrabbableObject _grabbedObject;
    private GameObject _rotator;

    private List<GameObject> _spheres = new List<GameObject>();
    private GameObject _thumbSphere, _indexSphere, _middleSphere;

    private Pose _wristWorld, _thumbTipWorld, _indexTipWorld, _middleTipWorld;
    private Pose _thumbTip, _indexTip, _middleTip;
    private Pose _prevThumbTip, _prevIndexTip, _prevMiddleTip;
    private Pose _thumbProj, _indexProj, _middleProj;
    private Pose _prevThumbProj, _prevIndexProj, _prevMiddleProj;
    private Pose _triangle, _prevTriangle;
    private Pose _prevObject, _object, _objectWorld;
    private Pose _grabOffset, _grabOffsetTriangle;
    private float _angleScaleFactor, _prevScaleFactor;
    private float _clutchDwellDuration = 0f;
    private bool _isDwelled = false, _isRotating = true;
    private OneEuroFilter<Vector3>[] _oneEuroFiltersVector3;
    private const float GRAB_DETECTION_RADIUS = 0.01f;
    private const float MIN_SCALE_FACTOR = 0.1f, MAX_SCALE_FACTOR = 5f;
    private const float MIN_TRAVEL_DISTANCE = 0.02f, MAX_TRAVEL_DISTANCE = 1f; // distance is in cm
    private const float LERP_SMOOTHING_FACTOR = 2f, MAX_ANGLE_BTW_FRAMES = 5f;
    private const float EURO_MIN_CUTOFF = 1.0f, EURO_BETA = 0.1f, EURO_D_CUTOFF = 1.0f;
    private const float CLUTCH_DWELL_TIME = 0.1f, CLUTCH_DWELL_ROTATION = 0.1f;
    private const float OUTLINE_WIDTH_DEFAULT = 3f, OUTLINE_WIDTH_CLUTCHING = 10f;

    public event Action OnGrab, OnRelease, OnClutchEnd, OnClutchStart;

    // Start is called before the first frame update
    void Start()
    {
        // for (int i = 0; i < 6; i++)
        // {
        //     GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        //     sphere.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        //     sphere.GetComponent<Collider>().isTrigger = true;
        //     _spheres.Add(sphere);
        // }

        _oneEuroFiltersVector3 = new OneEuroFilter<Vector3>[3];
        for (int i = 0; i < 3; i++)
        {
            _oneEuroFiltersVector3[i] = new OneEuroFilter<Vector3>(EURO_MIN_CUTOFF, EURO_BETA, EURO_D_CUTOFF);
        }

        InitGeometry();
        ResetGeometry();

        _keyActions = new Dictionary<KeyCode, Action>
        {
            // {KeyCode.P, () => _doProjection = true},
            // {KeyCode.O, () => _doProjection = false}
        };
    }

    // Update is called once per frame
    void Update()
    {
        if (_grabbedObject == null) CheckGrab();
        else CheckRelease();

        if (_clutchDwellDuration > CLUTCH_DWELL_TIME) _isDwelled = true;
        else _isDwelled = false;

        if (_isDwelled && _isRotating) StartClutching();
        // if (!_isDwelled && !_isRotating) EndClutching();

        // should be performed even if no object is grabbed
        // transforms are on the local coordinates based on the wrist if not stated otherwise
        _wristWorld.position = _wristBone.Transform.position;
        _wristWorld.rotation = _wristBone.Transform.rotation;

        _thumbTipWorld.position = _thumbTipBone.Transform.position;
        _indexTipWorld.position = _indexTipBone.Transform.position;
        _middleTipWorld.position = _middleTipBone.Transform.position;

        _thumbTip.position = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        _indexTip.position = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        _middleTip.position = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        _triangle.position = GetWeightedTriangleCentroid(_thumbTip.position, _indexTip.position, _middleTip.position);

        Pose thumbEuro, indexEuro, middleEuro;
        thumbEuro.position = _oneEuroFiltersVector3[0].Filter(_thumbTip.position, Time.deltaTime);
        indexEuro.position = _oneEuroFiltersVector3[1].Filter(_indexTip.position, Time.deltaTime);
        middleEuro.position = _oneEuroFiltersVector3[2].Filter(_middleTip.position, Time.deltaTime);

        Vector3 thumb, index, middle;
            thumb = thumbEuro.position;
            index = indexEuro.position;
            middle = middleEuro.position;

        // _spheres[0].transform.position = thumb;
        // _spheres[1].transform.position = index;
        // _spheres[2].transform.position = middle;

        // _spheres[3].transform.position = _thumbTip.position;
        // _spheres[4].transform.position = _indexTip.position;
        // _spheres[5].transform.position = _middleTip.position;

        bool isAngleValid, isTriangleValid, isAreaValid;

        isAngleValid = CalculateAngleAtVertex(thumb, index, middle, out float triangleP1Angle);
        isTriangleValid = CalculateTriangleOrientationWithOffset(thumb, index, middle, out _triangle.rotation);
        isAreaValid = CalculateTriangleArea(thumb, index, middle, out float triangleArea);

        float fingerTravel = GetFingerTravelDistance();
        float scaleFactor = GetScaleFactor(fingerTravel);
        _angleScaleFactor = Mathf.Lerp(_prevScaleFactor, scaleFactor, LERP_SMOOTHING_FACTOR * Time.deltaTime);
        // _angleScaleFactor = MAX_SCALE_FACTOR;
        _prevScaleFactor = _angleScaleFactor;

        _prevThumbTip.position = _thumbTip.position;
        _prevIndexTip.position = _indexTip.position;
        _prevMiddleTip.position = _middleTip.position;

        float deltaAngle = 0f;
        Vector3 deltaAxis = Vector3.one;

        // calculate the rotation
        if (isAngleValid && isTriangleValid && isAreaValid)
        {
            Quaternion deltaRotation = _triangle.rotation * Quaternion.Inverse(_prevTriangle.rotation);
            deltaRotation.ToAngleAxis(out deltaAngle, out deltaAxis);
            _prevTriangle.rotation = _triangle.rotation;
        }

        // skip from here if no object is grabbed
        if (_grabbedObject == null) return;

        if (deltaAngle < CLUTCH_DWELL_ROTATION) _clutchDwellDuration += Time.deltaTime;

        if (_isRotating)
        {
            float angleWithCeiling = Math.Min(deltaAngle, MAX_ANGLE_BTW_FRAMES);
            Quaternion deltaScaledRoation = Quaternion.AngleAxis(angleWithCeiling * _angleScaleFactor, deltaAxis);
            _object.rotation = deltaScaledRoation * _prevObject.rotation;
            _objectWorld.rotation = _wristWorld.rotation * _object.rotation * _grabOffset.rotation;
            _prevObject.rotation = _object.rotation;
        }
        else
        {
            _object.rotation = _prevObject.rotation;
            _objectWorld.rotation = _wristWorld.rotation * _object.rotation * _grabOffset.rotation;
        }

        _objectWorld.position = _wristBone.Transform.TransformPoint(_triangle.position) + _grabOffsetTriangle.position;

        _rotator.transform.position = _objectWorld.position;
        _rotator.transform.rotation = _objectWorld.rotation;
    }

    private void InitGeometry()
    {
        _indexTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip);
        _middleTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleTip);
        _thumbTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbTip);
        _wristBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_Wrist);

        _wristWorld.position = _wristBone.Transform.position;
        _wristWorld.rotation = _wristBone.Transform.rotation;

        _prevTriangle.rotation = Quaternion.identity;
        _prevObject.rotation = Quaternion.identity;

        if (_rotator != null) Destroy(_rotator);
        _rotator = new GameObject();
    }

    private void ResetGeometry()
    {
        _prevThumbTip.position = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        _prevIndexTip.position = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        _prevMiddleTip.position = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        _prevThumbProj.position = _prevThumbTip.position;
        _prevIndexProj.position = _prevIndexTip.position;
        _prevMiddleProj.position = _prevMiddleTip.position;

        _prevObject.rotation = Quaternion.identity;
        _prevScaleFactor = MIN_SCALE_FACTOR;
    }

    private void ResetOffset()
    {
        _grabOffset.position = Vector3.zero;
        _grabOffset.rotation = Quaternion.identity;
        _grabOffsetTriangle.position = Vector3.zero;
    }

    private void GetOffset()
    {
        if (_grabbedObject == null) return;
        _grabOffset.position = _wristBone.Transform.InverseTransformPoint(_grabbedObject.transform.position);
        _grabOffset.rotation = Quaternion.Inverse(_wristBone.Transform.rotation) * _grabbedObject.transform.rotation;
        _grabOffsetTriangle.position = _grabbedObject.transform.position - _wristBone.Transform.TransformPoint(_triangle.position);
    }

    private void CheckGrab()
    {
        GameObject thumb = GetObjectAtFinger(_thumbTipBone.Transform.position);
        GameObject index = GetObjectAtFinger(_indexTipBone.Transform.position);
        GameObject middle = GetObjectAtFinger(_middleTipBone.Transform.position);

        if (thumb != null && thumb == index && thumb == middle)
        {
            if (thumb.TryGetComponent(out GrabbableObject grabbable))
            {
                _grabbedObject = grabbable;
                _grabbedObject.OnGrab(_rotator.transform);
                OnGrab?.Invoke();
            }
        }        
    }

    private void CheckRelease()
    {
        GameObject thumb = GetObjectAtFinger(_thumbTipBone.Transform.position);
        GameObject index = GetObjectAtFinger(_indexTipBone.Transform.position);
        GameObject middle = GetObjectAtFinger(_middleTipBone.Transform.position);

        int contactCount = 0;
        if (thumb == _grabbedObject.gameObject) contactCount++;
        if (index == _grabbedObject.gameObject) contactCount++;
        if (middle == _grabbedObject.gameObject) contactCount++;

        if (contactCount < 2)
        {
            OnClutchEnd?.Invoke();
            OnRelease?.Invoke();
            _grabbedObject.OnRelease();
            _grabbedObject = null;

        }
    }

    public GameObject GetObjectAtFinger(Vector3 tipPosition)
    {
        Collider[] colliders = Physics.OverlapSphere(tipPosition, GRAB_DETECTION_RADIUS, _interactableLayer);
        return colliders.Length > 0 ? colliders[0].gameObject : null;
    }

    private float GetFingerTravelDistance()
    {
        Vector3 thumb = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        Vector3 index = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        Vector3 middle = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        // in mm
        float deltaThumb = (thumb - _prevThumbTip.position).magnitude * 1000f;
        float deltaIndex = (index - _prevIndexTip.position).magnitude * 1000f;
        float deltaMiddle = (middle - _prevMiddleTip.position).magnitude * 1000f;

        float speedThumb = deltaThumb / Time.deltaTime;
        float speedIndex = deltaIndex / Time.deltaTime;
        float speedMiddle = deltaMiddle / Time.deltaTime;

        // _text.text = $"{speedThumb:F2}\n{speedIndex:F2}\n{speedMiddle:F2}";

        return deltaThumb + deltaIndex + deltaMiddle;
    }

    public float GetScaleFactor(float distance)
    {
        // init overshoot exception needed
        
        if (distance < MIN_TRAVEL_DISTANCE) return MIN_SCALE_FACTOR;
        else if (distance > MAX_TRAVEL_DISTANCE) return MAX_SCALE_FACTOR;
        else return (MAX_SCALE_FACTOR - MIN_SCALE_FACTOR) / (MAX_TRAVEL_DISTANCE - MIN_TRAVEL_DISTANCE) * distance + (MAX_TRAVEL_DISTANCE * MIN_SCALE_FACTOR - MIN_TRAVEL_DISTANCE * MAX_SCALE_FACTOR) / (MAX_TRAVEL_DISTANCE - MIN_TRAVEL_DISTANCE);
    }

    public Vector3 GetTriangleCentroid(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        return (p1 + p2 + p3) / 3f;
    }

    public Vector3 GetWeightedTriangleCentroid(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        if (CalculateAngleAtVertex(p1, p2, p3, out float angle))
        {
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

    private bool CalculateAngleAtVertex(Vector3 vertex, Vector3 other1, Vector3 other2, out float angle)
    {
        Vector3 vec1 = other1 - vertex;
        Vector3 vec2 = other2 - vertex;

        if (vec1.sqrMagnitude < 0.00001f || vec2.sqrMagnitude < 0.00001f)
        {
            angle = float.NaN;
            return false;
        }

        angle = Vector3.Angle(vec1, vec2);
        return true;
    }

        public bool CalculateTriangleArea(Vector3 p1, Vector3 p2, Vector3 p3, out float area)
    {
        Vector3 vectorAB = (p2 - p1) * 100f;
        Vector3 vectorAC = (p3 - p1) * 100f;
        Vector3 vectorBC = (p3 - p2) * 100f;

        if (vectorAB.sqrMagnitude < 0.001f || vectorAC.sqrMagnitude < 0.001f
            || vectorBC.sqrMagnitude < 0.001f)
        {
            area = float.NaN;
            return false;
        }
        // _textbox.text = $"{vectorAB.magnitude}\n {vectorAC.magnitude}\n {vectorBC.magnitude}";

        Vector3 crossProduct = Vector3.Cross(vectorAB, vectorAC);

        area = crossProduct.magnitude / 2f;

        // _textbox.text = $"{area}";

        if (area < 0.001f)
        {
            area = float.NaN;
            return false;
        }

        return true;
    }

    private bool CalculateTriangleOrientationWithOffset(Vector3 p1, Vector3 p2, Vector3 p3, out Quaternion orientation)
    {
        // 1. 기본 축 계산
        Vector3 forward = (p2 - p1).normalized;
        Vector3 toP3 = (p3 - p1).normalized;
        Vector3 normal = Vector3.Cross(forward, toP3).normalized;

        // 안전 장치
        if (forward.sqrMagnitude < 0.001f || normal.sqrMagnitude < 0.001f)
        {
            orientation = Quaternion.identity;
            return false;
        }

        // 2. 기본 회전 (Z축: P2 방향, Y축: 평면 수직)
        Quaternion baseRotation = Quaternion.LookRotation(forward, normal);

        // 3. [핵심] P1->P2와 P1->P3 사이의 평면상 각도 계산
        // forward를 기준으로 toP3가 평면 위에서 몇 도 돌아가 있는지 구합니다.
        // Vector3.SignedAngle을 사용하면 법선(normal)을 기준으로 시계/반시계 방향을 구분합니다.
        float angleOffset = Vector3.SignedAngle(forward, toP3, normal);

        // 4. 결과값: 기본 회전에 Y축(normal축) 기준 오프셋 적용
        // angleOffset을 그대로 쓰거나, 특정 기준 각도를 빼서 '0도' 지점을 설정할 수 있습니다.
        orientation = baseRotation * Quaternion.Euler(0, angleOffset, 0);

        return true;
    }

    public void GrabObject()
    {
        ResetGeometry();
        GetOffset();
        _clutchDwellDuration = 0f;
        _isDwelled = false;
    }

    public void ReleaseObject()
    {
        ResetGeometry();
        ResetOffset();
        _clutchDwellDuration = 0f;
        _isDwelled = false;
    }

    public void StartClutching()
    {
        ResetGeometry();
        GetOffset();
        _isRotating = false;
        if (_grabbedObject != null)
        {
            _grabbedObject.SetOutlineWidth(OUTLINE_WIDTH_CLUTCHING);
        }
    }

    public void EndClutching()
    {
        _isRotating = true;
        if (_grabbedObject != null)
        {
            _grabbedObject.SetOutlineWidth(OUTLINE_WIDTH_DEFAULT);
        }
    }

    public void OnTarget()
    {
        if (_grabbedObject != null)
        {
            _grabbedObject.SetOutlineColor(Color.green);
        }
    }

    public void OffTarget()
    {
        if (_grabbedObject != null)
        {
            _grabbedObject.SetOutlineColor(Color.blue);
        }
    }

    public void Reset()
    {
        OffTarget();
        EndClutching();
        ReleaseObject();
        InitGeometry();
    }

    public void SetOVRSkeleton(OVRSkeleton s)
    {
        _ovrSkeleton = s;
    }

    public void SetInteractableLayer(LayerMask l)
    {
        _interactableLayer = l;
    }
}
