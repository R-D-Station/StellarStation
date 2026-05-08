using FinalStateMachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using PlayerControls;
using ValuesEye;

public class Player : Entity
{
    [SerializeField] PlayerControl _playerControls;

    private void Start()
    {
        

        CreateFSM();
    }
    private void OnEnable()
    {
        if (_playerControls == null)
        {
            _playerControls = new PlayerControl();

            _playerControls.Player.Move.performed += OnMovementPerformed;
            _playerControls.Player.Move.canceled += OnMovementCanceled;
        }
        _playerControls.Enable();
    }
    private void OnDisable()
    {
        _playerControls.Disable();
    }

    protected override void CreateFSM()
    {
        base.CreateFSM();

        Fsm.AddState(new FSM_StateMovePlayer(Fsm, this));
        Fsm.AddState(new FSM_StateStandPlayer(Fsm, this));

        Fsm.SetState<FSM_StateStandPlayer>();
    }

    private void Update()
    {
        Fsm.Update();
    }

    public PlayerControl GetPlayerControl()
    {
        return _playerControls;
    }
    private void OnMovementPerformed(InputAction.CallbackContext context)
    {
        // —читываем вектор движени€ (значение от -1 до 1 по ос€м X и Y)
        MoveDirection = context.ReadValue<Vector2>();
    }

    private void OnMovementCanceled(InputAction.CallbackContext context)
    {
        MoveDirection = Vector2.zero;
    }
}
