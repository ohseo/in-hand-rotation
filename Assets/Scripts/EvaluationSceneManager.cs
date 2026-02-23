using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;

public class EvaluationSceneManager : MonoBehaviour
{
    [SerializeField]
    private bool _isLeftHanded = false;
    [SerializeField]
    private GameObject _diePrefab, _targetPrefab;
    [SerializeField]
    private HandInteractor _handInteractorRight, _handInteractorLeft;
    [SerializeField]
    private OVRSkeleton _ovrSkeletonRight, _ovrSkeletonLeft;

    private const int MAX_TRIAL_NUM = 100;
    private int _trialNum = 1;

    private GameObject _die, _target;
    private const float CUBE_SCALE = 0.04f, INIT_ROTATION_DEG = 135f;
    private Vector3 _initPosition = new Vector3(0.1f, 1.1f, 0.3f);
    private Pose _targetOffset;
    private const float POSITION_THRESHOLD = 0.05f, ROTATION_THRESHOLD_DEG = 10f;
    private const float DWELL_THRESHOLD = 1f, TIMEOUT_THRESHOLD = 30f;
    private float _dwellDuration, _trialDuration;
    private bool _isOnTarget = false, _isTimeout = false, _isInTrial = false;

    public event Action OnTrialLoad, OnTrialStart, OnTrialEnd, OnTrialReset, OnTarget, OffTarget;

    void Awake()
    {
        _handInteractorRight.SetOVRSkeleton(_ovrSkeletonRight);
        _handInteractorLeft.SetOVRSkeleton(_ovrSkeletonLeft);
    }


    // Start is called before the first frame update
    void Start()
    {
        OnTrialLoad += LoadNewTrial;
        OnTrialStart += StartTrial;

        OnTrialEnd += EndTrial;
        OnTrialEnd += _handInteractorLeft.Reset;
        OnTrialEnd += _handInteractorRight.Reset;

        OnTrialReset += ResetTrial;
        OnTrialReset += _handInteractorLeft.Reset;
        OnTrialReset += _handInteractorRight.Reset;

        _handInteractorLeft.OnGrab += _handInteractorLeft.GrabObject;
        _handInteractorLeft.OnGrab += OnGrab;
        _handInteractorRight.OnGrab += _handInteractorRight.GrabObject;
        _handInteractorRight.OnGrab += OnGrab;

        _handInteractorLeft.OnRelease += _handInteractorLeft.ReleaseObject;
        _handInteractorRight.OnRelease += _handInteractorRight.ReleaseObject;

        _handInteractorLeft.OnClutchEnd += _handInteractorLeft.EndClutching;
        _handInteractorRight.OnClutchEnd += _handInteractorRight.EndClutching;

        _handInteractorLeft.OnClutchStart += _handInteractorLeft.StartClutching;
        _handInteractorRight.OnClutchStart += _handInteractorRight.StartClutching;

        OnTarget += _handInteractorLeft.OnTarget;
        OnTarget += _handInteractorRight.OnTarget;

        OffTarget += _handInteractorLeft.OffTarget;
        OffTarget += _handInteractorRight.OffTarget;

        OnTrialLoad?.Invoke();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.Return))
        {
            OnTrialReset?.Invoke();
            return;
        }

        if (!_isInTrial) return;

        bool isErrorSmall = CalculateError(out Pose offset);
        if (isErrorSmall && !_isOnTarget)
        {
            _dwellDuration = 0f;
            _isOnTarget = true;
            OnTarget?.Invoke();
        }
        else if (!isErrorSmall && _isOnTarget)
        {
            _isOnTarget = false;
            OffTarget?.Invoke();
        }

        if (_isOnTarget)
        {
            _dwellDuration += Time.deltaTime;
            if (_dwellDuration > DWELL_THRESHOLD)
            {
                OnTrialEnd?.Invoke();
                if (_trialNum <= MAX_TRIAL_NUM) OnTrialLoad?.Invoke();
            }
        }
    }

    void OnDestroy()
    {
        DestroyDie();
        DestroyTarget();
    }

    private void LoadNewTrial()
    {
        GenerateDie();
        GenerateTarget();
    }

    private void StartTrial()
    {
        _isTimeout = false;
        _isInTrial = true;
        _trialDuration = 0f;
    }

    private void EndTrial()
    {
        DestroyDie();
        DestroyTarget();
        _isOnTarget = false;
        _isInTrial = false;
        _trialNum++;
    }

    private void ResetTrial()
    {
        DestroyDie();
        GenerateDie();
        _isOnTarget = false;
    }

    private void OnGrab()
    {
        if (!_isInTrial) OnTrialStart?.Invoke();
    }

    private void GenerateDie()
    {
        _die = Instantiate(_diePrefab);
        _die.transform.position = _isLeftHanded ? _initPosition : new Vector3(-_initPosition.x, _initPosition.y, _initPosition.z);
        _die.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        _die.transform.rotation = Quaternion.identity;
    }

    private void DestroyDie()
    {
        Destroy(_die);
    }

    private void GenerateTarget()
    {
        _target = Instantiate(_targetPrefab);
        Vector3 axis = UnityEngine.Random.onUnitSphere;
        _target.transform.Rotate(axis.normalized, INIT_ROTATION_DEG);
        _target.transform.position = _isLeftHanded ? new Vector3(-_initPosition.x, _initPosition.y, _initPosition.z) : _initPosition;
        _target.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
    }

    private void DestroyTarget()
    {
        Destroy(_target);
    }

    private bool CalculateError(out Pose delta)
    {
        delta.position = _target.transform.position - _die.transform.position; //
        delta.rotation = _target.transform.rotation * Quaternion.Inverse(_die.transform.rotation);
        float pError = delta.position.magnitude;
        delta.rotation.ToAngleAxis(out float rError, out Vector3 axis);
        return (pError < POSITION_THRESHOLD) && ((rError < ROTATION_THRESHOLD_DEG) || (rError > 360f - ROTATION_THRESHOLD_DEG));
    }
}
