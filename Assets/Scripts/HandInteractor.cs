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

public class HandInteractor : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI _textbox;
    [SerializeField]
    private bool _isInDebugMode;
    private bool _doProjection = true;
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
    private Pose _thumbProjWorld, _indexProjWorld, _middleProjWorld;
    private Pose _prevThumbProjWorld, _prevIndexProjWorld, _prevMiddleProjWorld;
    private Pose _triangle, _prevTriangle;
    private Pose _prevObject, _object, _objectWorld;
    private Pose _grabOffset;
    private float _angleScaleFactor, _prevScaleFactor;
    private float _clutchDwellDuration = 0f;
    private bool _isDwelled = false, _isRotating = false;
    private const float GRAB_DETECTION_RADIUS = 0.01f;
    private const float MIN_SCALE_FACTOR = 0.1f, MAX_SCALE_FACTOR = 5f;
    private const float MIN_TRAVEL_DISTANCE = 0.02f, MAX_TRAVEL_DISTANCE = 1f; // distance is in cm
    private const float LERP_SMOOTHING_FACTOR = 2f, MAX_ANGLE_BTW_FRAMES = 5f;
    private const float CLUTCH_DWELL_TIME = 0.2f, CLUTCH_DWELL_ROTATION = 0.25f;

    // Start is called before the first frame update
    void Start()
    {
        // for (int i = 0; i < 3; i++)
        // {
        //     GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        //     sphere.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        //     sphere.GetComponent<Collider>().isTrigger = true;
        //     _spheres.Add(sphere);
        // }
        InitGeometry();
        ResetGeometry();

        _keyActions = new Dictionary<KeyCode, Action>
        {
            {KeyCode.P, () => _doProjection = true},
            {KeyCode.O, () => _doProjection = false}
        };
    }

    // Update is called once per frame
    void Update()
    {
        if (_grabbedObject == null) CheckGrab();
        else CheckRelease();

        // if (_grabbedObject == null) return;

        //transforms are on the local coordinates based on the wrist if not stated otherwise
        _wristWorld.position = _wristBone.Transform.position;
        _wristWorld.rotation = _wristBone.Transform.rotation;

        _thumbTipWorld.position = _thumbTipBone.Transform.position;
        _indexTipWorld.position = _indexTipBone.Transform.position;
        _middleTipWorld.position = _middleTipBone.Transform.position;

        _thumbTip.position = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        _indexTip.position = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        _middleTip.position = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        _triangle.position = GetWeightedTriangleCentroid(_thumbTipWorld.position, _indexTipWorld.position, _middleTipWorld.position);

        _thumbProjWorld.position = ProjectClosestToPrevious(_thumbTipWorld.position, _triangle.position, 1f, _prevThumbProjWorld.position);
        _indexProjWorld.position = ProjectClosestToPrevious(_indexTipWorld.position, _triangle.position, 1f, _prevIndexProjWorld.position);
        _middleProjWorld.position = ProjectClosestToPrevious(_middleTipWorld.position, _triangle.position, 1f, _prevMiddleProjWorld.position);

        _prevThumbProjWorld.position = _thumbProjWorld.position;
        _prevIndexProjWorld.position = _indexProjWorld.position;
        _prevMiddleProjWorld.position = _middleProjWorld.position;

        _thumbProj.position = _wristBone.Transform.InverseTransformPoint(_thumbProjWorld.position);
        _indexProj.position = _wristBone.Transform.InverseTransformPoint(_indexProjWorld.position);
        _middleProj.position = _wristBone.Transform.InverseTransformPoint(_middleProjWorld.position);

        Vector3 thumb, index, middle;
        if (_doProjection)
        {
            thumb = _thumbProj.position;
            index = _indexProj.position;
            middle = _middleProj.position;
        }
        else
        {
            thumb = _thumbTip.position;
            index = _indexTip.position;
            middle = _middleTip.position;
        }

        // _spheres[0].transform.position = thumbTipPositionWorld;
        // _spheres[1].transform.position = indexTipPositionWorld;
        // _spheres[2].transform.position = middleTipPositionWorld;

        bool isAngleValid, isTriangleValid, isAreaValid;

        isAngleValid = CalculateAngleAtVertex(thumb, index, middle, out float triangleP1Angle);
        isTriangleValid = CalculateTriangleOrientationWithOffset(thumb, index, middle, out _triangle.rotation);
        isAreaValid = CalculateTriangleArea(thumb, index, middle, out float triangleArea);

        float fingerTravel = GetFingerTravelDistance();
        float scaleFactor = GetScaleFactor(fingerTravel);
        _angleScaleFactor = Mathf.Lerp(_prevScaleFactor, scaleFactor, LERP_SMOOTHING_FACTOR * Time.deltaTime);
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

        if (deltaAngle < CLUTCH_DWELL_ROTATION) _clutchDwellDuration += Time.deltaTime;
        if (_clutchDwellDuration > CLUTCH_DWELL_TIME) _isDwelled = true;
        else _isDwelled = false;

        // if (_isDwelled && _isRotating) StartClutching();
        // if (!_isDwelled && !_isRotating) EndClutching();

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

        _objectWorld.position = _wristBone.Transform.TransformPoint(_grabOffset.position);

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

        _rotator = new GameObject();
    }

    private void ResetGeometry()
    {
        _prevThumbProjWorld.position = _thumbTipBone.Transform.position;
        _prevIndexProjWorld.position = _indexTipBone.Transform.position;
        _prevMiddleProjWorld.position = _middleTipBone.Transform.position;

        _prevThumbTip.position = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        _prevIndexTip.position = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        _prevMiddleTip.position = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        _prevObject.rotation = Quaternion.identity;
        _prevScaleFactor = MIN_SCALE_FACTOR;
        _clutchDwellDuration = 0f;
    }

    private void ResetOffset()
    {
        _grabOffset.position = Vector3.zero;
        _grabOffset.rotation = Quaternion.identity;
    }

    private void GetOffset()
    {
        if (_grabbedObject == null) return;
        _grabOffset.position = _wristBone.Transform.InverseTransformPoint(_grabbedObject.transform.position);
        _grabOffset.rotation = Quaternion.Inverse(_wristBone.Transform.rotation) * _grabbedObject.transform.rotation;
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
                OnGrab();
                _grabbedObject.OnGrab(_rotator.transform);
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
            _grabbedObject.OnRelease();
            OnRelease();
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

        float deltaThumb = (thumb - _prevThumbTip.position).magnitude * 100f;
        float deltaIndex = (index - _prevIndexTip.position).magnitude * 100f;
        float deltaMiddle = (middle - _prevMiddleTip.position).magnitude * 100f;

        return deltaThumb + deltaIndex + deltaMiddle;
    }

    public float GetScaleFactor(float distance)
    {
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

    private Vector3 ProjectClosestToPrevious(Vector3 point, Vector3 center, float radius, Vector3 prevPoint)
    {
        Vector3 direction = point - center;
        if (direction.sqrMagnitude < 0.000001f)
        {
            return prevPoint;
        }
        direction.Normalize();

        Vector3 pos = center + direction * radius;
        Vector3 neg = center - direction * radius;

        float distPos = (pos - prevPoint).sqrMagnitude;
        float distNeg = (neg - prevPoint).sqrMagnitude;

        if (distPos < distNeg) return pos;
        else return neg;
    }

    public void OnGrab()
    {
        ResetGeometry();
        GetOffset();
        _isDwelled = false;
        _isRotating = true;
    }

    public void OnRelease()
    {
        ResetGeometry();
        ResetOffset();
        _isDwelled = false;
        _isRotating = false;
    }

    public void StartClutching()
    {
        GetOffset();
        _isRotating = false;
    }

    public void EndClutching()
    {
        _isRotating = true;
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
