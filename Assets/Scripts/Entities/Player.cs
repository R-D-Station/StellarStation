using UnityEngine;
using ValuesEye;
using System;
using System.Collections.Generic;

public class Player : Entity
{
    [SerializeField] PlayerMoveController moveController;

    private void Start()
    {
        moveController.Init(this);
    }

    private void Update()
    {
        Fsm.Update();
    }
}
