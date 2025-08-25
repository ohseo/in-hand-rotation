using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class DieReleaseHandler : MonoBehaviour
{
    private int spheresInContact = 0;
    private RotationInteractor _rotationInteractor;
    private float _releaseScale = 1.5f;
    private bool _isTaskComplete = false;

    public event Action OnRelease;
    // Start is called before the first frame update
    void Start()
    {
        this.transform.localScale = new Vector3(_releaseScale, _releaseScale, _releaseScale);
    }

    // Update is called once per frame
    void Update()
    {
        if (_rotationInteractor.IsTaskComplete != _isTaskComplete)
        {
            _isTaskComplete = _rotationInteractor.IsTaskComplete;
            if (_isTaskComplete)
            {
                this.transform.localScale = new Vector3(1f, 1f, 1f);
            }
            else
            {
                this.transform.localScale = new Vector3(_releaseScale, _releaseScale, _releaseScale);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Target"))
        {
            //
        }
        else if (other.CompareTag("TipSphere"))
        {
            spheresInContact++;

            if (!_rotationInteractor.IsGrabbed)
            {
                if (spheresInContact > 2)
                {
                    // _rotationInteractor.SetIsGrabbed(true);
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Target"))
        {
            //
        }
        else if (other.CompareTag("TipSphere"))
        {
            spheresInContact--;

            if (_rotationInteractor.IsGrabbed)
            {
                if (spheresInContact < 2)
                {
                    // _rotationInteractor.IsGrabbed = false;
                    OnRelease?.Invoke();
                }
            }
        }
    }

    public void SetRotationInteractor(RotationInteractor r)
    {
        _rotationInteractor = r;
    }
}
