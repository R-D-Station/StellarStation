using System;
using UnityEngine;

public class PlayerMoveController
{
    public Vector2 MoveDirection;
    [SerializeField] Entity _entity;

    KeyCode Up, Down, Right, Left;

    public void Init(Entity entity)
    {
         _entity = entity;

        SetKeyCode();
    }
    public void SetKeyCode()
    {
        Up = (KeyCode)Enum.Parse(typeof(KeyCode), PlayerPrefs.GetString("PC_KEY_Up"));
        Down = (KeyCode)Enum.Parse(typeof(KeyCode), PlayerPrefs.GetString("PC_KEY_Down"));
        Right = (KeyCode)Enum.Parse(typeof(KeyCode), PlayerPrefs.GetString("PC_KEY_Right"));
        Left = (KeyCode)Enum.Parse(typeof(KeyCode), PlayerPrefs.GetString("PC_KEY_Left"));
    }

    public Vector2 Move()
    {
        Vector2 Move = new Vector2(0, 0);

        if (Input.GetKey(Up))
        {
            Move.y = 1;
        }
        else
        {
            if (Input.GetKey(Down))
            {
                Move.y = -1;
            }
        }

        if (Input.GetKey(Left))
        {
            Move.x = -1;
        }
        else
        {
            if (Input.GetKey(Right))
            {
                Move.x = 1;
            }
        }

        return Move;
    }
}
