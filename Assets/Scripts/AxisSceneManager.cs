using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class AxisSceneManager : MonoBehaviour
{
    [SerializeField]
    private int _participantNum;
    private const int EXP_CONDITION = 1; // 0: Baseline, 1: Linear, 2: Power, 3: Tanh
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
    private AxisLogManager _logManager;


    private GameObject _die, _target;
    private const float CUBE_SCALE = 0.04f;
    private List<float> _initRotationDeg = new List<float> { 30f, 45f, 60f, 75f};
    private Dictionary<string, Vector3> _rotationAxis = new Dictionary<string, Vector3>();
    private Dictionary<int, string> _latinSquare6 = new Dictionary<int, string>();
    private string _axisSequence, _axisCond;
    private int _axisNum = 0, _angleNum = 0;
    private float _angleCond = 0f;
    private Vector3 _diePosition = new Vector3(-0.05f, 1.1f, 0.3f);
    private Vector3 _targetPosition = new Vector3(0.05f, 1.1f, 0.3f);
    private Vector3 _targetOffsetPosition;
    private Quaternion _targetOffsetRotation;
    private const float POSITION_THRESHOLD = 0.01f, ROTATION_THRESHOLD_DEG = 10f;

    private bool _isOnTarget = false, _isTimeout = false, _isInTrial = false, _isRightAngle = false;

    private const float DWELL_THRESHOLD = 1f, TIMEOUT_THRESHOLD = 20f;
    private float _dwellDuration, _trialDuration;

    private const int MAX_SET_NUM = 4;
    private int _setNum = 1;

    private int _maxTrialNum;
    private int _trialNum = 1;

    private DieGrabHandler _grabHandler;
    private DieReleaseHandler _releaseHandler;

    public event Action OnTrialEnd, OnTrialStart, OnTrialReset, OnSceneLoad, OnTarget, OffTarget, OnAngle, OffAngle, OnTimeout;
    public event Action<string> OnEvent;

    private LineRenderer _arrowRenderer;


    void Awake()
    {
        _latinSquare6.Add(0, "abfced");
        _latinSquare6.Add(1, "bcadfe");
        _latinSquare6.Add(2, "cdbeaf");
        _latinSquare6.Add(3, "decfba");
        _latinSquare6.Add(4, "efdacb");
        _latinSquare6.Add(5, "faebdc");

        _axisSequence = _latinSquare6[(int)_participantNum % 6];

        _rotationAxis.Add("a", new Vector3(0f, 0f, 1f));
        _rotationAxis.Add("b", new Vector3(0f, 0f, -1f));
        _rotationAxis.Add("c", new Vector3(1f, 0f, 0f));
        _rotationAxis.Add("d", new Vector3(-1f, 0f, 0f));
        _rotationAxis.Add("e", new Vector3(0f, 1f, 0f));
        _rotationAxis.Add("f", new Vector3(0f, -1f, 0f));

        ShuffleNumbers(_initRotationDeg);

        _maxTrialNum = MAX_SET_NUM * _initRotationDeg.Count * _rotationAxis.Count;

        _axisCond = _axisSequence[_axisNum].ToString();
        _angleCond = _initRotationDeg[_angleNum];

        GenerateDie();
        _rotationInteractor.SetCube(_die);

        if (_isLeftHanded) _rotationInteractor.SetOVRSkeleton(_ovrLeftSkeleton);
        else _rotationInteractor.SetOVRSkeleton(_ovrRightSkeleton);

        // _rotationInteractor.SetTransferFunction(EXP_CONDITION);

        _text.text = $"Set {_setNum}/{MAX_SET_NUM}, Trial {_trialNum % (_initRotationDeg.Count * _rotationAxis.Count) }/{_maxTrialNum/MAX_SET_NUM}";

        _logManager.SetExpConditions(_participantNum, EXP_CONDITION);
    }

    // Start is called before the first frame update
    void Start()
    {
        _grabHandler = _die.GetComponentInChildren<DieGrabHandler>();
        _releaseHandler = _die.GetComponentInChildren<DieReleaseHandler>();

        // all events
        OnEvent += _logManager.OnEvent;

        // scene load
        OnSceneLoad += LoadNewScene;
        OnSceneLoad += () => { OnEvent?.Invoke("Scene Loaded"); };

        // trial start
        OnTrialStart += StartTrial;
        OnTrialStart += () => { OnEvent?.Invoke("Trial Start"); };

        // trial end
        OnTrialEnd += _rotationInteractor.Reset;
        OnTrialEnd += () => { OnEvent?.Invoke("Trial End"); };
        OnTrialEnd += EndTrial;

        // trial reset
        OnTrialReset += ResetTrial;
        OnTrialReset += _rotationInteractor.Reset;
        OnTrialReset += () => { OnEvent?.Invoke("Trial Reset"); };

        // grab
        _grabHandler.OnGrab += OnGrab;
        _grabHandler.OnGrab += _rotationInteractor.OnGrab;
        _grabHandler.OnGrab += () => { OnEvent?.Invoke("Grab"); };

        // release
        _releaseHandler.OnRelease += _rotationInteractor.OnRelease;
        _releaseHandler.OnRelease += () => { OnEvent?.Invoke("Release"); };

        // clutch start
        _rotationInteractor.OnClutchEnd += _rotationInteractor.EndClutching;
        _rotationInteractor.OnClutchEnd += () => { OnEvent?.Invoke("Clutch Start"); };

        // clutch end
        _rotationInteractor.OnClutchStart += _rotationInteractor.StartClutching;
        _rotationInteractor.OnClutchStart += () => { OnEvent?.Invoke("Clutch End"); };

        // on target
        OnTarget += _rotationInteractor.OnTarget;
        OnTarget += _releaseHandler.OnTarget;
        OnTarget += () => { OnEvent?.Invoke("On Target"); };

        // off target
        OffTarget += _rotationInteractor.OffTarget;
        OffTarget += _releaseHandler.OffTarget;
        OffTarget += () => { OnEvent?.Invoke("Off Target"); };

        // on angle
        OnAngle += HighlightOn;
        OnAngle += () => { OnEvent?.Invoke("HighlightOn"); };

        // off angle
        OffAngle += HighlightOff;
        OffAngle += () => { OnEvent?.Invoke("HighlightOff"); };

        // timeout
        OnTimeout += Timeout;
        OnTimeout += () => { OnEvent?.Invoke("Timed Out"); };

        OnSceneLoad?.Invoke();
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
            if (_trialNum <= _maxTrialNum) OnSceneLoad?.Invoke();
            return;
        }

        if (!_isInTrial) return;

        bool isErrorSmall = CalculateRotError(out _targetOffsetRotation);
        bool isClose = CalculatePosError(out _targetOffsetPosition);

        if (!isClose || !isErrorSmall)
        {
            _dwellDuration = 0f;
        }

        if (isErrorSmall && !_isRightAngle)
        {
            _isRightAngle = true;
            OnAngle?.Invoke();
        }
        else if (!isErrorSmall && _isRightAngle)
        {
            _isRightAngle = false;
            OffAngle?.Invoke();
        }

        if (isClose && !_isOnTarget)
        {
            _dwellDuration = 0f;
            _isOnTarget = true;
            OnTarget?.Invoke();
        }
        else if (!isClose && _isOnTarget)
        {
            _isOnTarget = false;
            OffTarget?.Invoke();
        }

        if (_isOnTarget && _isRightAngle)
        {
            _dwellDuration += Time.deltaTime;
            if (_dwellDuration > DWELL_THRESHOLD)
            {
                OnTrialEnd?.Invoke();
                if (_trialNum <= _maxTrialNum) OnSceneLoad?.Invoke();
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
        GenerateTarget();
        ResetDie();
        _text.text = $"Set {_setNum}/{MAX_SET_NUM}, Trial {_trialNum % (_initRotationDeg.Count * _rotationAxis.Count) }/{_maxTrialNum/MAX_SET_NUM}";
    }

    private void StartTrial()
    {
        _isTimeout = false;
        _isInTrial = true;
        _trialDuration = 0f;
    }

    private void EndTrial()
    {
        DestroyTarget();
        _isOnTarget = false;
        _isRightAngle = false;
        _isInTrial = false;

        if (_trialNum == _maxTrialNum)
        {
            _die.SetActive(false);
        }
        HighlightOff();
        _trialNum++;
        _angleNum++;
        if (_angleNum == _initRotationDeg.Count) { _angleNum = 0; _axisNum++; ShuffleNumbers(_initRotationDeg); }
        if (_axisNum == _rotationAxis.Count) { _axisNum = 0; _setNum++; }
        _axisCond = _axisSequence[_axisNum].ToString();
        _angleCond = _initRotationDeg[_angleNum];
    }

    private void ResetTrial()
    {
        ResetDie();
        _isOnTarget = false;
        _isRightAngle = false;
        _trialDuration = 0f;
    }

    private void OnGrab()
    {
        if (!_isInTrial)
        {
            OnTrialStart?.Invoke();
        }
    }

    private void HighlightOn()
    {
        Transform t = _die.transform.Find("arrow");
        LineRenderer lr = t.gameObject.GetComponent<LineRenderer>();
        lr.startColor = Color.red;
        lr.endColor = Color.red;
    }

    private void HighlightOff()
    {
        Transform t = _die.transform.Find("arrow");
        LineRenderer lr = t.gameObject.GetComponent<LineRenderer>();
        lr.startColor = Color.white;
        lr.endColor = Color.white;
    }

    private void Timeout()
    {
        _isTimeout = true;
        OnTrialEnd?.Invoke();
    }

    public bool CalculatePosError(out Vector3 deltaPos)
    {
        deltaPos = _target.transform.position - _die.transform.position; //
        float pError = deltaPos.magnitude;
        return pError < POSITION_THRESHOLD;
    }

    public bool CalculateRotError(out Quaternion deltaRot)
    {
        deltaRot = _target.transform.rotation * Quaternion.Inverse(_die.transform.rotation);
        deltaRot.ToAngleAxis(out float rError, out Vector3 axis);
        return (rError < ROTATION_THRESHOLD_DEG) || (rError > 360f - ROTATION_THRESHOLD_DEG);
    }

    private void GenerateDie()
    {
        _die = Instantiate(_diePrefab);
        _die.transform.position = _diePosition;
        _die.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        _die.transform.rotation = Quaternion.identity;
        DrawAxisOnGO(_die);
        DrawArrow(_die);
    }

    private void DestroyDie()
    {
        Destroy(_die);
    }

    private void ResetDie()
    {
        _die.transform.position = _diePosition;
        _die.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        _die.transform.rotation = Quaternion.identity;
        DrawArrow(_die);
        DrawAxisOnGO(_die);
    }

    private void GenerateTarget()
    {
        _target = Instantiate(_targetPrefab);
        _target.transform.Rotate(_rotationAxis[_axisCond], _initRotationDeg[_angleNum]);
        _target.transform.position = new Vector3(_diePosition.x - 0.1f, _diePosition.y, _diePosition.z);
        _target.transform.localScale = new Vector3(CUBE_SCALE, CUBE_SCALE, CUBE_SCALE);
        DrawAxisOnGO(_target);
    }

    private void DestroyTarget()
    {
        Destroy(_target);
    }

    private void DrawAxisOnGO(GameObject go)
    {
        LineRenderer lr = go.GetComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.SetPosition(0, _rotationAxis[_axisCond] * -1f);
        lr.SetPosition(1, _rotationAxis[_axisCond]);
    }

    private void DrawArrow(GameObject go)
    {
        Transform t = go.transform.Find("arrow");
        if (t == null) return;
        GameObject child = t.gameObject;
        LineRenderer lr = t.GetComponentInChildren<LineRenderer>();
        int steps = 100;
        lr.useWorldSpace = false;
        lr.positionCount = steps + 1;
        float angleStep = 360f / (steps * 1.5f);
        Vector3 point = Vector3.zero;
        for (int i = 0; i < steps; i++)
        {
            float angle = angleStep * i * Mathf.Deg2Rad;
            point = new Vector3(Mathf.Cos(angle) * 0.6f, Mathf.Sin(angle) * 0.6f, 0);
            lr.SetPosition(i, point);
        }
        lr.SetPosition(steps, point + new Vector3(-0.7f, 0.5f, 0f));
        Vector3 axis = _rotationAxis[_axisCond];
        axis.Normalize();
        Quaternion r = Quaternion.LookRotation(axis);
        t.localRotation = r;

    }

    public void GetHeadTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _centerEyeAnchor.transform.position;
        rotation = _centerEyeAnchor.transform.rotation;
    }

    public void GetDieTransform(out Vector3 position, out Quaternion rotation)
    {
        position = _die.transform.position;
        rotation = _die.transform.rotation;
    }

    public void GetTargetOffset(out Vector3 position, out Quaternion rotation)
    {
        position = _targetOffsetPosition;
        rotation = _targetOffsetRotation;
    }

    public void GetStatus(out bool isGrabbing, out bool isClutching, out bool isOverlapped, out bool isRightAngle, out bool isTimeout)
    {
        isGrabbing = _rotationInteractor.IsGrabbed;
        isClutching = _rotationInteractor.IsRotating;
        isOverlapped = _isOnTarget;
        isRightAngle = _isRightAngle;
        isTimeout = _isTimeout;
    }

    public void ShuffleNumbers(List<float> list)
    {
        System.Random rng = new System.Random();
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            float value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }

    public bool IsInTrial
    {
        get { return _isInTrial; }
        set { _isInTrial = value; }
    }

    public int TrialNum
    {
        get { return _trialNum; }
        set { _trialNum = value; }
    }

    public float TrialDuration
    {
        get { return _trialDuration; }
        set { _trialDuration = value; }
    }

    public int AxisNum
    {
        get { return _axisNum; }
        set { _axisNum = value; }
    }

    public int AngleNum
    {
        get { return _angleNum; }
        set { _angleNum = value; }
    }

    public float AngleCond
    {
        get
        {
            return _angleCond;
        }
    }

    public int SetNum
    {
        get { return _setNum; }
        set { _setNum = value; }
    }

    public string AxisCond
    {
        get { return _axisCond; }
        set { _axisCond = value; }
    }
    
}
