using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DieGrabHandler : MonoBehaviour
{
    private int spheresInContact = 0;
    [SerializeField]
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
            //
        }
        else if (other.CompareTag("TipSphere"))
        {                                                                                                                                                                                                                                                          
            spheresInContact++;

            if (!_rotationInteractor.GetIsGrabbed())
            {
                if (spheresInContact > 2)
                {
                    _rotationInteractor.SetIsGrabbed(true);
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

            if (_rotationInteractor.GetIsGrabbed())
            {
                if (spheresInContact < 2)
                {
                    // _rotationInteractor.SetIsGrabbed(false);
                }
            }
        }
    }
}
