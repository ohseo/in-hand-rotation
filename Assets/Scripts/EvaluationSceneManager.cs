using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EvaluationSceneManager : MonoBehaviour
{
    [SerializeField]
    private RotationInteractor _rotationInteractor;
    [SerializeField]
    private GameObject _diePrefab, _targetPrefab;
    private GameObject _die, _target;
    private float _cubeScale = 0.03f;
    private float _initRotationDeg = 120f;
    private Vector3 _initPosition = new Vector3(0.1f, 1f, 0.3f);
    private Vector3 _posError;
    private Quaternion _rotError;
    private float _posThreshold = 0.005f, _rotThresholdDeg = 5f;
    private bool _isTaskComplete = false;

    void Awake()
    {
        GenerateTarget();
        GenerateDie();
        _rotationInteractor.SetCube(_die);
    }

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.Return))
        {
            DestroyTarget();
            GenerateTarget();
        }

        _isTaskComplete = CalculateError(out _posError, out _rotError);
        _rotationInteractor.IsTaskComplete = _isTaskComplete;
        if (_isTaskComplete)
        {
            ResetDie();
            DestroyTarget();
            GenerateTarget();
            _rotationInteractor.Reset();
        }
    }

    public bool CalculateError(out Vector3 deltaPos, out Quaternion deltaRot)
    {
        deltaPos = _target.transform.position - _die.transform.position;
        deltaRot = _target.transform.rotation * Quaternion.Inverse(_die.transform.rotation);
        float pError = deltaPos.magnitude;
        deltaRot.ToAngleAxis(out float rError, out Vector3 axis);
        return (pError < _posThreshold) && (rError < _rotThresholdDeg);
    }

    private void GenerateDie()
    {
        _die = Instantiate(_diePrefab);
        _die.transform.position = new Vector3(-_initPosition.x, _initPosition.y, _initPosition.z);
        _die.transform.localScale = new Vector3(_cubeScale, _cubeScale, _cubeScale);
    }

    private void DestroyDie()
    {
        Destroy(_die);
    }

    private void ResetDie()
    {
        _die.transform.position = new Vector3(-_initPosition.x, _initPosition.y, _initPosition.z);
        _die.transform.localScale = new Vector3(_cubeScale, _cubeScale, _cubeScale);
    }

    private void GenerateTarget()
    {
        _target = Instantiate(_targetPrefab);
        Vector3 axis = Random.onUnitSphere;
        _target.transform.Rotate(axis.normalized, _initRotationDeg);
        _target.transform.position = _initPosition;
        _target.transform.localScale = new Vector3(_cubeScale, _cubeScale, _cubeScale);
    }

    private void DestroyTarget()
    {
        Destroy(_target);
    }
}
