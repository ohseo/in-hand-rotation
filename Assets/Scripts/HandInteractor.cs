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
    GameObject _cube;
    [SerializeField]
    private TextMeshProUGUI _textbox;
    [SerializeField]
    private bool _isInDebugMode;
    private bool _doProjection = false;
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
    private Quaternion _wristRotationWorld;
    private Vector3 _wristPositionWorld;
    private Vector3 _prevThumbProjWorld, _prevIndexProjWorld, _prevMiddleProjWorld;
    private Vector3 _prevThumbPosition, _prevIndexPosition, _prevMiddlePosition;
    private Vector3 _triangleCentroid;
    private Quaternion _prevTriangleRotation, _triangleRotation;
    private Quaternion _prevObjectRotation, _objectRotation, _objectRotationWorld;
    private Vector3 _prevObjectPosition, _objectPosition, _objectPositionWorld;
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
        _wristPositionWorld = _wristBone.Transform.position;
        _wristRotationWorld = _wristBone.Transform.rotation;

        Vector3 thumbTipPositionWorld = _thumbTipBone.Transform.position;
        Vector3 indexTipPositionWorld = _indexTipBone.Transform.position;
        Vector3 middleTipPositionWorld = _middleTipBone.Transform.position;

        Vector3 thumbTipPosition = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        Vector3 indexTipPosition = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        Vector3 middleTipPosition = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        _triangleCentroid = GetWeightedTriangleCentroid(thumbTipPositionWorld, indexTipPositionWorld, middleTipPositionWorld);

        Vector3 thumbProjWorld = ProjectClosestToPrevious(thumbTipPositionWorld, _triangleCentroid, 1f, _prevThumbProjWorld);
        Vector3 indexProjWorld = ProjectClosestToPrevious(indexTipPositionWorld, _triangleCentroid, 1f, _prevIndexProjWorld);
        Vector3 middleProjWorld = ProjectClosestToPrevious(middleTipPositionWorld, _triangleCentroid, 1f, _prevMiddleProjWorld);

        _prevThumbProjWorld = thumbProjWorld;
        _prevIndexProjWorld = indexProjWorld;
        _prevMiddleProjWorld = middleProjWorld;

        Vector3 thumbProj = _wristBone.Transform.InverseTransformPoint(thumbProjWorld);
        Vector3 indexProj = _wristBone.Transform.InverseTransformPoint(indexProjWorld);
        Vector3 middleProj = _wristBone.Transform.InverseTransformPoint(middleProjWorld);

        Vector3 thumb, index, middle;
        if (_doProjection)
        {
            thumb = thumbProj;
            index = indexProj;
            middle = middleProj;
        }
        else
        {
            thumb = thumbTipPosition;
            index = indexTipPosition;
            middle = middleTipPosition;
        }

        // _spheres[0].transform.position = thumbTipPositionWorld;
        // _spheres[1].transform.position = indexTipPositionWorld;
        // _spheres[2].transform.position = middleTipPositionWorld;

        bool isAngleValid, isTriangleValid, isAreaValid;

        isAngleValid = CalculateAngleAtVertex(thumb, index, middle, out float triangleP1Angle);
        isTriangleValid = CalculateTriangleOrientationWithOffset(thumb, index, middle, out _triangleRotation);
        isAreaValid = CalculateTriangleArea(thumb, index, middle, out float triangleArea);

        float fingerTravel = GetFingerTravelDistance();
        float scaleFactor = GetScaleFactor(fingerTravel);
        _angleScaleFactor = Mathf.Lerp(_prevScaleFactor, scaleFactor, LERP_SMOOTHING_FACTOR * Time.deltaTime);
        _prevScaleFactor = _angleScaleFactor;

        _prevThumbPosition = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        _prevIndexPosition = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        _prevMiddlePosition = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        float deltaAngle = 0f;
        Vector3 deltaAxis = Vector3.one;

        // calculate the rotation
        if (isAngleValid && isTriangleValid && isAreaValid)
        {
            Quaternion deltaRotation = _triangleRotation * Quaternion.Inverse(_prevTriangleRotation);
            deltaRotation.ToAngleAxis(out deltaAngle, out deltaAxis);
            _prevTriangleRotation = _triangleRotation;
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
            _objectRotation = deltaScaledRoation * _prevObjectRotation;
            _objectRotationWorld = _wristRotationWorld * _objectRotation * _grabOffset.rotation;
            _prevObjectRotation = _objectRotation;
        }
        else
        {
            _objectRotation = _prevObjectRotation;
            _objectRotationWorld = _wristRotationWorld * _objectRotation * _grabOffset.rotation;
        }

        _objectPositionWorld = _wristBone.Transform.TransformPoint(_grabOffset.position);

        _rotator.transform.position = _objectPositionWorld;
        _rotator.transform.rotation = _objectRotationWorld;
    }

    private void InitGeometry()
    {
        _indexTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip);
        _middleTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_MiddleTip);
        _thumbTipBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_ThumbTip);
        _wristBone = _ovrSkeleton.Bones.FirstOrDefault(bone => bone.Id == OVRSkeleton.BoneId.XRHand_Wrist);

        _wristPositionWorld = _wristBone.Transform.position;
        _wristRotationWorld = _wristBone.Transform.rotation;

        _prevTriangleRotation = Quaternion.identity;
        _prevObjectRotation = Quaternion.identity;

        _rotator = new GameObject();
    }

    private void ResetGeometry()
    {
        _prevThumbProjWorld = _thumbTipBone.Transform.position;
        _prevIndexProjWorld = _indexTipBone.Transform.position;
        _prevMiddleProjWorld = _middleTipBone.Transform.position;

        _prevThumbPosition = _wristBone.Transform.InverseTransformPoint(_thumbTipBone.Transform.position);
        _prevIndexPosition = _wristBone.Transform.InverseTransformPoint(_indexTipBone.Transform.position);
        _prevMiddlePosition = _wristBone.Transform.InverseTransformPoint(_middleTipBone.Transform.position);

        _prevObjectRotation = Quaternion.identity;
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

    public GameObject GetObjectAtFingers()
    {
        Collider[] collidersAtThumb = Physics.OverlapSphere(_thumbTipBone.Transform.position, GRAB_DETECTION_RADIUS, _interactableLayer);
        Collider[] collidersAtIndex = Physics.OverlapSphere(_indexTipBone.Transform.position, GRAB_DETECTION_RADIUS, _interactableLayer);
        Collider[] collidersAtMiddle = Physics.OverlapSphere(_middleTipBone.Transform.position, GRAB_DETECTION_RADIUS, _interactableLayer);
        if (collidersAtThumb.Length < 1) return null;
        if (collidersAtIndex.Length < 1) return null;
        if (collidersAtMiddle.Length < 1) return null;

        if (collidersAtThumb[0].gameObject == collidersAtIndex[0].gameObject && collidersAtThumb[0].gameObject == collidersAtMiddle[0].gameObject)
        {
            return collidersAtThumb[0].gameObject;
        }
        return null;
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

        float deltaThumb = (thumb - _prevThumbPosition).magnitude * 100f;
        float deltaIndex = (index - _prevIndexPosition).magnitude * 100f;
        float deltaMiddle = (middle - _prevMiddlePosition).magnitude * 100f;

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
