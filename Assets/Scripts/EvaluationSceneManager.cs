using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class EvaluationSceneManager : MonoBehaviour
{
    [SerializeField]
    private RotationInteractor _rotationInteractor;
    [SerializeField]
    private GameObject _diePrefab, _targetPrefab;
    [SerializeField]
    protected TextMeshProUGUI _text;
    [SerializeField]
    private int _expCondition = 0; // 0: Baseline, 1: Linear, 2: Power, 3: Tanh
    [SerializeField]
    private bool _isLeftHanded = false;
    [SerializeField]
    private OVRSkeleton _ovrRightSkeleton, _ovrLeftSkeleton;

    private GameObject _die, _target;
    private const float CUBE_SCALE = 0.04f;
    private const float INIT_ROTATION_DEG = 135f;
    private Vector3 _initPosition = new Vector3(0.1f, 1.1f, 0.3f);
    private Vector3 _posError;
    private Quaternion _rotError;
    private const float POSITION_THRESHOLD = 0.01f, ROTATION_THRESHOLD_DEG = 5f;

    private bool _isDwelling = false, isOverlapped = false, _isTimeout = false, _isInTrial = false;

    private const float DWELL_THRESHOLD = 1f, TIMEOUT_THRESHOLD = 20f;
    private float _dwellDuration, _trialDuration;

    private const int MAX_TRIAL_NUM = 15;
    private int _trialNum = 1;

    private DieGrabHandler _grabHandler;
    private DieReleaseHandler _releaseHandler;

    public event Action OnOverlap, OnDepart, OnTrialEnd, OnTrialStart, OnTrialReset;


    void Awake()
    {
        GenerateTarget();
        GenerateDie();
        _rotationInteractor.SetCube(_die);
        if (_isLeftHanded) _rotationInteractor.SetOVRSkeleton(_ovrLeftSkeleton);
        else _rotationInteractor.SetOVRSkeleton(_ovrRightSkeleton);
        _rotationInteractor.SetTransferFunction(_expCondition);
        _text.text = $"Trial {_trialNum}/{MAX_TRIAL_NUM}";
    }

    // Start is called before the first frame update
    void Start()
    {
        _grabHandler = _die.GetComponentInChildren<DieGrabHandler>();
        _grabHandler.OnGrab += _rotationInteractor.OnGrab;
        _grabHandler.OnGrab += StartTrial;
        _grabHandler.OnTarget += _rotationInteractor.OnTarget;
        _grabHandler.OffTarget += _rotationInteractor.OffTarget;
        _releaseHandler = _die.GetComponentInChildren<DieReleaseHandler>();
        _releaseHandler.OnRelease += _rotationInteractor.OnRelease;
        OnOverlap += _rotationInteractor.OnOverlap;
        OnDepart += _rotationInteractor.OnDepart;
        OnTrialEnd += EndTrial;
        OnTrialEnd += _rotationInteractor.Reset;
        OnTrialReset += ResetTrial;
        OnTrialReset += _rotationInteractor.Reset;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.Return))
        {
            OnTrialReset?.Invoke();
            return;
        }

        if (_isInTrial && _trialDuration > TIMEOUT_THRESHOLD)
        {
            OnTrialEnd?.Invoke();
        }

        bool _isErrorSmall = CalculateError(out _posError, out _rotError);

        if (_isErrorSmall && !isOverlapped)
        {
            _dwellDuration = 0f;
            _isDwelling = true;
            isOverlapped = true;
            OnOverlap?.Invoke();
        }
        else if (!_isErrorSmall && isOverlapped)
        {
            _isDwelling = false;
            isOverlapped = false;
            OnDepart?.Invoke();
        }

        if (_isDwelling)
        {
            _dwellDuration += Time.deltaTime;
            if (_dwellDuration > DWELL_THRESHOLD)
            {
                OnTrialEnd?.Invoke();
                LoadNewScene();
            }
        }
    }

    private void LoadNewScene()
    {
        GenerateTarget();
        _trialNum++;
        _text.text = $"Trial {_trialNum}/{MAX_TRIAL_NUM}";
    }

    private void StartTrial()
    {
        if (!_isInTrial)
        {
            _isInTrial = true;
            _trialDuration = 0f;
        }
    }

    private void EndTrial()
    {
        ResetDie();
        DestroyTarget();
        _isDwelling = false;
        _isInTrial = false;
    }

    private void ResetTrial()
    {
        ResetDie();
        _isDwelling = false;
        _trialDuration = 0f;
    }

    public bool CalculateError(out Vector3 deltaPos, out Quaternion deltaRot)
    {
        deltaPos = _target.transform.position - _die.transform.position;
        deltaRot = _target.transform.rotation * Quaternion.Inverse(_die.transform.rotation);
        float pError = deltaPos.magnitude;
        deltaRot.ToAngleAxis(out float rError, out Vector3 axis);
        // Debug.Log(string.Format("Error: {0}, {1}",pError, rError));
        return (pError < POSITION_THRESHOLD) && ((rError < ROTATION_THRESHOLD_DEG) || (rError > 360f - ROTATION_THRESHOLD_DEG));
    }

    private void GenerateDie()
    {
        _die = Instantiate(_diePrefab);
        _die.transform.position = new Vector3(-_initPosition.x, _initPosition.y, _initPosition.z);
        _die.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        _die.transform.rotation = Quaternion.identity;
    }

    private void DestroyDie()
    {
        Destroy(_die);
    }

    private void ResetDie()
    {
        _die.transform.position = new Vector3(-_initPosition.x, _initPosition.y, _initPosition.z);
        _die.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        _die.transform.rotation = Quaternion.identity;
    }

    private void GenerateTarget()
    {
        _target = Instantiate(_targetPrefab);
        Vector3 axis = UnityEngine.Random.onUnitSphere;
        _target.transform.Rotate(axis.normalized, INIT_ROTATION_DEG);
        _target.transform.position = _initPosition;
        _target.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
    }

    private void DestroyTarget()
    {
        Destroy(_target);
    }
}
