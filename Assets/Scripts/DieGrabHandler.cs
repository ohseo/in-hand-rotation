using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DieGrabHandler : MonoBehaviour
{
    private int spheresInContact = 0;
    private RotationInteractor _rotationInteractor;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Target"))
        {
            _rotationInteractor.IsOnTarget = true;
        }
        else if (other.CompareTag("TipSphere"))
        {
            spheresInContact++;

            if (!_rotationInteractor.IsGrabbed)
            {
                if (spheresInContact > 2)
                {
                    _rotationInteractor.IsGrabbed = true;
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Target"))
        {
            _rotationInteractor.IsOnTarget = false;
        }
        else if (other.CompareTag("TipSphere"))
        {
            spheresInContact--;

            if (_rotationInteractor.IsGrabbed)
            {
                if (spheresInContact < 2)
                {
                    // _rotationInteractor.SetIsGrabbed(false);
                }
            }
        }
    }

    public void SetRotationInteractor(RotationInteractor r)
    {
        _rotationInteractor = r;
    }
}
