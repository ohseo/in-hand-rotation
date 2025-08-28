using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    private RotationInteractor _rotationInteractor;
    [SerializeField]
    private ErgonomicsLogManager _logManager;

    private GameObject _die, _target;
    private const float CUBE_SCALE = 0.04f;
    private const float INIT_ROTATION_DEG = 135f;
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

    private List<int> _gridNumbers = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

    void Awake()
    {
        GenerateDie();
        _rotationInteractor.SetCube(_die);

        if (_isLeftHanded) _rotationInteractor.SetOVRSkeleton(_ovrLeftSkeleton);
        else _rotationInteractor.SetOVRSkeleton(_ovrRightSkeleton);

        _rotationInteractor.SetTransferFunction(TRANSFERFUNCTION);

    }
    // Start is called before the first frame update
    void Start()
    {
        _grabHandler = _die.GetComponentInChildren<DieGrabHandler>();
        _releaseHandler = _die.GetComponentInChildren<DieReleaseHandler>();

        

        _gridPositions.Add(1, new Vector3(-0.1f, 1.2f, 0.3f));
        _gridPositions.Add(2, new Vector3(0f, 1.2f, 0.3f));
        _gridPositions.Add(3, new Vector3(0.1f, 1.2f, 0.3f));
        _gridPositions.Add(4, new Vector3(-0.1f, 1.1f, 0.3f));
        _gridPositions.Add(5, new Vector3(0f, 1.1f, 0.3f));
        _gridPositions.Add(6, new Vector3(0.1f, 1.1f, 0.3f));
        _gridPositions.Add(7, new Vector3(-0.1f, 1f, 0.3f));
        _gridPositions.Add(8, new Vector3(0f, 1f, 0.3f));
        _gridPositions.Add(9, new Vector3(0.1f, 1f, 0.3f));

        ShuffleNumbers(_gridNumbers);
    }

    // Update is called once per frame
    void Update()
    {

    }

    void OnDestroy()
    {
        DestroyTarget();
        DestroyDie();
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

    private void GenerateDie()
    {
        _die = Instantiate(_diePrefab);
        _die.transform.position = _gridPositions[_gridNumbers[_setNum]];
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
}
