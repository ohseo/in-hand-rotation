using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ErgonomicsSceneManager : MonoBehaviour
{
    [SerializeField]
    private int _participantNum;
    [SerializeField]
    private int _expCondition = 0; // 0: Palmar, 1: Radial, 2: Dorsal
    [SerializeField]
    private bool _isLeftHanded = false;
    [SerializeField]
    private GameObject _diePrefab, _targetPrefab;
    [SerializeField]
    private OVRSkeleton _ovrRightSkeleton, _ovrLeftSkeleton;
    [SerializeField]
    private GameObject _centerEyeAnchor;
    [SerializeField]
    protected TextMeshProUGUI _text;
    [SerializeField]
    private RotationInteractor _rotationInteractor;
    [SerializeField]
    private ErgonomicsLogManager _logManager;

    private GameObject _die, _target;
    private const float CUBE_SCALE = 0.04f;
    private const float INIT_ROTATION_DEG = 90f;
    private Dictionary<int, Vector3> _gridPositions = new Dictionary<int, Vector3>();
    private Vector3 _targetOffsetPosition;
    private Quaternion _targetOffsetRotation;
    private const float POSITION_THRESHOLD = 0.01f, ROTATION_THRESHOLD_DEG = 5f;

    private const float DWELL_THRESHOLD = 1f, TIMEOUT_THRESHOLD = 30f;
    private float _dwellDuration, _trialDuration;

    private const int MAX_TRIAL_NUM = 4, MAX_SET_NUM = 9;
    private int _trialNum = 1, _setNum = 1;

    private const int TRANSFERFUNCTION = 1; // 0: Baseline, 1: linear, 2: accelerating(power), 3: decelerating(hyperbolic tangent)

    private DieGrabHandler _grabHandler;
    private DieReleaseHandler _releaseHandler;

    public event Action OnTrialEnd, OnTrialStart, OnTrialReset, OnSceneLoad, OnTarget, OffTarget, OnTimeout;

    private bool _isOnTarget = false, _isTimeout = false, _isInTrial = false;

    private List<int> _gridNumbers = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

    void Awake()
    {
        _gridPositions.Add(1, new Vector3(-0.1f, 1.2f, 0.3f));
        _gridPositions.Add(2, new Vector3(0f, 1.2f, 0.3f));
        _gridPositions.Add(3, new Vector3(0.1f, 1.2f, 0.3f));
        _gridPositions.Add(4, new Vector3(-0.1f, 1.1f, 0.3f));
        _gridPositions.Add(5, new Vector3(0f, 1.1f, 0.3f));
        _gridPositions.Add(6, new Vector3(0.1f, 1.1f, 0.3f));
        _gridPositions.Add(7, new Vector3(-0.1f, 1f, 0.3f));
        _gridPositions.Add(8, new Vector3(0f, 1f, 0.3f));
        _gridPositions.Add(9, new Vector3(0.1f, 1f, 0.3f));

        GenerateDie();
        _rotationInteractor.SetCube(_die);

        if (_isLeftHanded) _rotationInteractor.SetOVRSkeleton(_ovrLeftSkeleton);
        else _rotationInteractor.SetOVRSkeleton(_ovrRightSkeleton);

        _rotationInteractor.SetTransferFunction(TRANSFERFUNCTION);

        _text.text = $"Trial {_trialNum}/{MAX_TRIAL_NUM}, Set {_setNum}/{MAX_SET_NUM}";

    }
    // Start is called before the first frame update
    void Start()
    {
        _grabHandler = _die.GetComponentInChildren<DieGrabHandler>();
        _releaseHandler = _die.GetComponentInChildren<DieReleaseHandler>();

        ShuffleNumbers(_gridNumbers);

        // scene load
        OnSceneLoad += LoadNewScene;
        // OnSceneLoad += () => { OnEvent?.Invoke("Scene Loaded"); };

        // trial start
        OnTrialStart += StartTrial;
        // OnTrialStart += () => { OnEvent?.Invoke("Trial Start"); };

        // trial end
        OnTrialEnd += EndTrial;
        OnTrialEnd += _rotationInteractor.Reset;
        // OnTrialEnd += () => { OnEvent?.Invoke("Trial End"); };

        // trial reset
        OnTrialReset += ResetTrial;
        OnTrialReset += _rotationInteractor.Reset;
        // OnTrialReset += () => { OnEvent?.Invoke("Trial Reset"); };

        // grab
        _grabHandler.OnGrab += OnGrab;
        _grabHandler.OnGrab += _rotationInteractor.OnGrab;
        // _grabHandler.OnGrab += () => { OnEvent?.Invoke("Grab"); };

        // release
        _releaseHandler.OnRelease += _rotationInteractor.OnRelease;
        // _releaseHandler.OnRelease += () => { OnEvent?.Invoke("Release"); };

        // clutch start
        _rotationInteractor.OnClutchStart += _rotationInteractor.StartClutching;
        // _rotationInteractor.OnClutchStart += () => { OnEvent?.Invoke("Clutch Start"); };

        // clutch end
        _rotationInteractor.OnClutchEnd += _rotationInteractor.EndClutching;
        // _rotationInteractor.OnClutchEnd += () => { OnEvent?.Invoke("Clutch End"); };

        // on target
        OnTarget += _rotationInteractor.OnTarget;
        OnTarget += _releaseHandler.OnTarget;
        // OnTarget += () => { OnEvent?.Invoke("On Target"); };

        // off target
        OffTarget += _rotationInteractor.OffTarget;
        OffTarget += _releaseHandler.OffTarget;
        // OffTarget += () => { OnEvent?.Invoke("Off Target"); };

        // timeout
        OnTimeout += Timeout;
        // OnTimeout += () => { OnEvent?.Invoke("Timed Out"); };

        OnSceneLoad?.Invoke();
        ResetDie();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.Return))
        {
            OnTrialReset?.Invoke();
            return;
        }

        _trialDuration += Time.deltaTime;

        if (_isInTrial && _trialDuration > TIMEOUT_THRESHOLD)
        {
            OnTimeout?.Invoke();
            if (_setNum <= MAX_SET_NUM) OnSceneLoad?.Invoke();
            return;
        }

        if (!_isInTrial) return;

        bool _isErrorSmall = CalculateError(out _targetOffsetPosition, out _targetOffsetRotation);

        if (_isErrorSmall && !_isOnTarget)
        {
            _dwellDuration = 0f;
            _isOnTarget = true;
            OnTarget?.Invoke();
        }
        else if (!_isErrorSmall && _isOnTarget)
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
                if (_setNum <= MAX_SET_NUM) OnSceneLoad?.Invoke();
            }
        }
    }

    void OnDestroy()
    {
        DestroyTarget();
        DestroyDie();
    }

    private void LoadNewScene()
    {
        ResetDie();
        GenerateTarget();
        _text.text = $"Trial {_trialNum}/{MAX_TRIAL_NUM}, Set {_setNum}/{MAX_SET_NUM}";
    }

    private void StartTrial()
    {
        _isTimeout = false;
        _isInTrial = true;
        _trialDuration = 0f;
    }

    private void EndTrial()
    {
        // ResetDie();
        DestroyTarget();
        _isOnTarget = false;
        _isInTrial = false;

        if (_trialNum == MAX_TRIAL_NUM)
        {
            if (_setNum == MAX_SET_NUM)
            {
                _die.SetActive(false);
            }
            _setNum++;
            _trialNum = 0;
        }
        _trialNum++;
    }

    private void ResetTrial()
    {
        ResetDie();
        _isOnTarget = false;
        _trialDuration = 0f;
    }

    private void OnGrab()
    {
        if (!_isInTrial)
        {
            OnTrialStart?.Invoke();
        }
    }

    private void Timeout()
    {
        _isTimeout = true;
        OnTrialEnd?.Invoke();
    }

    private void GenerateDie()
    {
        _die = Instantiate(_diePrefab);
        _die.transform.position = new Vector3(1f, 1f, 1f);
        _die.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        _die.transform.rotation = Quaternion.identity;
    }

    private void DestroyDie()
    {
        Destroy(_die);
    }

    private void ResetDie()
    {
        _die.transform.position = _gridPositions[_gridNumbers[_setNum]];
        _die.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        _die.transform.rotation = Quaternion.identity;
    }

    private void GenerateTarget()
    {
        _target = Instantiate(_targetPrefab);
        Vector3 axis = UnityEngine.Random.onUnitSphere;
        _target.transform.Rotate(axis.normalized, INIT_ROTATION_DEG);
        _target.transform.position = _gridPositions[_gridNumbers[_setNum]];
        _target.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
    }

    private void DestroyTarget()
    {
        Destroy(_target);
    }

    public void ShuffleNumbers(List<int> list)
    {
        System.Random rng = new System.Random();
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            int value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    public bool CalculateError(out Vector3 deltaPos, out Quaternion deltaRot)
    {
        deltaPos = _target.transform.position - _die.transform.position; //
        deltaRot = _target.transform.rotation * Quaternion.Inverse(_die.transform.rotation);
        float pError = deltaPos.magnitude;
        deltaRot.ToAngleAxis(out float rError, out Vector3 axis);
        return (pError < POSITION_THRESHOLD) && ((rError < ROTATION_THRESHOLD_DEG) || (rError > 360f - ROTATION_THRESHOLD_DEG));
    }

    // public bool CalculateWristError(out Vector3 deltaPos, out Quaternion deltaRot)
    // {
        
    // }
}
