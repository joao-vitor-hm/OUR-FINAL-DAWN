using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class Game : MonoBehaviour {

    [Header("Panels")]
    [SerializeField] private GameObject PausePanel = null;

    [Header("Variables")]
    [SerializeField] private Animator PlayerAnimator = null;

    [Header("Game Configs")]
    [SerializeField] public static bool isGamePaused = false;
    [SerializeField] public static bool isMouseGame = false;
    [SerializeField] public static bool isDebugGame = true;

    private float TimeScaleBeforePause = 1f;
    private bool PauseApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() {
        isGamePaused = false;
        isMouseGame = false;
        isDebugGame = true;

        Time.timeScale = 1f;
    }

    private void OnEnable() {
        SetPaused(isGamePaused);
    }

    private void Update() {
        if (Keyboard.current != null && Keyboard.current[GameControlls.Pause].wasPressedThisFrame) {
            SetPaused(!isGamePaused);
        }

        if (Keyboard.current != null && Keyboard.current[GameControlls.Mouse].wasPressedThisFrame) {
            isMouseGame = !isMouseGame;
            ApplyCursorState();
        }

        if (Keyboard.current != null && Keyboard.current[GameControlls.Teste].wasPressedThisFrame) {
            isDebugGame = !isDebugGame;
        }

        if (PauseApplied != isGamePaused) {
            SetPaused(isGamePaused);
        }
    }

    public void ResumeGame() {
        SetPaused(false);
    }

    private void SetPaused(bool paused) {
        if (paused != PauseApplied) {
            if (paused) {
                TimeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            } else {
                Time.timeScale = TimeScaleBeforePause;
            }

            PauseApplied = paused;
        }

        isGamePaused = paused;

        if (PausePanel != null) {
            PausePanel.SetActive(paused);
        }
        
        ApplyCursorState();
    }

    private void ApplyCursorState() {
        bool showCursor = isGamePaused || isMouseGame;

        Cursor.lockState = showCursor ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = showCursor;
    }

    private void OnDisable() {
        if (PauseApplied) {
            Time.timeScale = TimeScaleBeforePause;
        }

        PauseApplied = false;
        isGamePaused = false;
        isMouseGame = false;

        if (PausePanel != null) {
            PausePanel.SetActive(false);
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
