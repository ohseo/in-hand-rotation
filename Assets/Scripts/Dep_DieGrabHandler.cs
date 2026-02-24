using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DieGrabHandler : MonoBehaviour
{
    private int spheresInContact = 0;
    private RotationInteractor _rotationInteractor;

    public event Action OnGrab;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    public void Reset()
    {
        spheresInContact = 0;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("TipSphere"))
        {
            spheresInContact++;

            if (!_rotationInteractor.IsGrabbed)
            {
                if (spheresInContact > 2)
                {
                    OnGrab?.Invoke();
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("TipSphere")) spheresInContact--;
    }

    public void SetRotationInteractor(RotationInteractor r)
    {
        _rotationInteractor = r;
    }
}
