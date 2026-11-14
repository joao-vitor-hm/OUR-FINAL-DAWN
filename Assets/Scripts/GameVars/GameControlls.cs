using UnityEngine;
using UnityEngine.InputSystem;

public static class GameControlls {

    public static Key Pause = Key.Escape;
    public static Key Teste = Key.L;
    public static Key Mouse = Key.V;
    public static Key Sprint = Key.LeftShift;
    public static Key Recover = Key.R;

    public static Key MoveRight = Key.D;
    public static Key MoveLeft = Key.A;
    public static Key MoveDown = Key.S;
    public static Key MoveTop = Key.W;

    public static bool IsAiming() {
        return UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
    }

    public static float GetZoom() {
        return UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y : 0f;
    }

    public static Vector3 GetMovement() {
        Vector3 movement = Vector3.zero;

        if (Keyboard.current != null && Keyboard.current[GameControlls.MoveDown].isPressed) {
            movement.z -= 1;
        }

        if (Keyboard.current != null && Keyboard.current[GameControlls.MoveTop].isPressed) {
            movement.z += 1;
        }

        if (Keyboard.current != null && Keyboard.current[GameControlls.MoveRight].isPressed) {
            movement.x += 1;
        }

        if (Keyboard.current != null && Keyboard.current[GameControlls.MoveLeft].isPressed) {
            movement.x -= 1;
        }

        return Vector3.ClampMagnitude(movement, 1f);
    }
}
