#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using InputKey = UnityEngine.InputSystem.Key;

/// <summary>
/// Run in a dedicated Editor after other smoke tests finish:
/// -batchmode -projectPath "..." -executeMethod V1CameraValidation.Run -logFile "camera.log"
/// Do NOT pass -quit: Run enters Play Mode asynchronously and exits batch mode itself.
/// Loads Debug-Scene only; never saves a scene or asset. Exit code: 0 pass, 1 failure.
/// Cursor coverage exercises the V state, not hardware/input-event injection.
/// </summary>
[InitializeOnLoad]
public static class V1CameraValidation {
    private const string Key = "OurFinalDawn.V1CameraValidation.Session";
    private const string Prefix = "[V1CameraValidation] ";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Serializable]
    private sealed class Session {
        public long started;
        public bool finishing;
        public bool failed;
        public int assertions;
    }

    private static Session session;
    private static Game game;
    private static Player player;
    private static Camera camera;
    private static Rigidbody hips;
    private static Transform target;
    private static V1CameraValidationRunner runner;
    private static bool sampling;
    private static int samples;
    private static float maxPositionError;
    private static float maxRotationError;
    private static bool captureRequested;
    private static string PreviewSuffix = "";
    private static Keyboard TestKeyboard;
    private static InputSettings.BackgroundBehavior PreviousBackground;
    private static InputSettings.EditorInputBehaviorInPlayMode PreviousEditorInput;
    private static bool ChangedInputSettings;
    private static bool MeasuringStability;
    private static ActiveRagdollMuscle MeasuredMuscle;
    private static Transform MeasuredHead;
    private static int StabilitySamples;
    private static float FootErrorSum;
    private static float MaxFootError;
    private static float MaxBodyTilt;

    static V1CameraValidation() {
        string saved = SessionState.GetString(Key, "");
        if (saved.Length == 0) return;
        session = JsonUtility.FromJson<Session>(saved);
        Subscribe();
    }

    public static void Run() {
        // Refuse to interfere with the main smoke or an interactive Play Mode session.
        if (session != null || EditorApplication.isPlayingOrWillChangePlaymode ||
            SessionState.GetString("OurFinalDawn.V1Validation.Session", "").Length != 0)
            throw new InvalidOperationException(Prefix + "Wait for the active validation/Play Mode session to finish.");
        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg.Equals("-quit", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(Prefix + "Remove -quit; this validation is asynchronous.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException(Prefix + "Open scene has unsaved edits; use a dedicated Editor.");

        session = new Session { started = DateTime.UtcNow.Ticks };
        Save();
        Subscribe();
        try {
            EditorSceneManager.OpenScene("Assets/Scenes/Debug-Scene.unity", OpenSceneMode.Single);
            Debug.Log(Prefix + "Starting camera follow, pause/resume, and cursor-state validation.");
            EditorApplication.isPlaying = true;
        } catch (Exception exception) { Finish(exception); }
    }

    private static void Subscribe() {
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.update -= Watchdog;
        EditorApplication.update += Watchdog;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    private static void Save() { SessionState.SetString(Key, JsonUtility.ToJson(session)); }

    private static void OnPlayMode(PlayModeStateChange change) {
        if (session == null) return;
        if (change == PlayModeStateChange.EnteredPlayMode && !session.finishing) {
            var host = new GameObject("V1 Camera Validation (temporary)") { hideFlags = HideFlags.HideAndDontSave };
            runner = host.AddComponent<V1CameraValidationRunner>();
            runner.StartCoroutine(Execute());
        } else if (change == PlayModeStateChange.ExitingPlayMode && !session.finishing) {
            session.failed = true;
            session.finishing = true;
            Save();
            Debug.Log(Prefix + "FAIL: Play Mode ended before validation completed.");
        } else if (change == PlayModeStateChange.EnteredEditMode) {
            Exit();
        }
    }

    private static void Watchdog() {
        if (session == null) return;
        if (TimeSpan.FromTicks(DateTime.UtcNow.Ticks - session.started).TotalSeconds <= 90) return;
        if (!session.finishing) Finish(new TimeoutException("Validation exceeded 90 seconds of wall time."));
        else if (Application.isBatchMode) {
            session.failed = true;
            Exit();
        }
    }

    private static void OnLog(string message, string stack, LogType type) {
        if (session == null || session.finishing ||
            (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        session.failed = true;
        Save();
        Debug.Log(Prefix + "FAIL runtime: " + message);
    }

    private static IEnumerator Execute() {
        IEnumerator scenario = Scenario();
        while (true) {
            object next;
            try {
                if (!scenario.MoveNext()) break;
                next = scenario.Current;
            } catch (Exception exception) {
                Finish(exception);
                yield break;
            }
            yield return next;
        }
        Finish(null);
    }

    private static IEnumerator Scenario() {
        // Allow Awake/Start and initial physical settling before resolving runtime references.
        yield return new WaitForSecondsRealtime(1f);
        game = UnityEngine.Object.FindFirstObjectByType<Game>();
        player = UnityEngine.Object.FindFirstObjectByType<Player>();
        camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
        Check(game != null && player != null && camera != null && camera.enabled, "Scene has active Game, Player and Camera.");
        hips = Read<Rigidbody>(player, "m_Hips");
        target = Read<Transform>(camera, "CameraTarget");
        Check(hips != null && !hips.isKinematic && target != null, "Physical hips and camera target are assigned.");
        Check(Read<Transform>(camera, "FollowHips") == hips.transform, "Camera resolved the same physical hips as Player.");

        // Suppress real mouse motion throughout this automated run; movement remains physical.
        Game.isMouseGame = true;
        Invoke(game, "ApplyCursorState");
        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-eightDirections")) {
            PreviousBackground = InputSystem.settings.backgroundBehavior;
            PreviousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            ChangedInputSettings = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            TestKeyboard = InputSystem.AddDevice<Keyboard>();
            MeasuredMuscle = player.GetComponent<ActiveRagdollMuscle>();
            MeasuredHead = player.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            StabilitySamples = 0;
            FootErrorSum = MaxFootError = MaxBodyTilt = 0f;
            MeasuringStability = false;
            InputKey[][] keys = {
                new[] { InputKey.W }, new[] { InputKey.W, InputKey.D }, new[] { InputKey.D }, new[] { InputKey.S, InputKey.D },
                new[] { InputKey.S }, new[] { InputKey.S, InputKey.A }, new[] { InputKey.A }, new[] { InputKey.W, InputKey.A }
            };
            for (int i = 0; i < keys.Length; i++) {
                InputSystem.QueueStateEvent(TestKeyboard, new KeyboardState());
                Invoke(player, "SetDebugMovement", new object[] { null });
                player.Respawn();
                yield return new WaitForSecondsRealtime(0.5f);
                Vector3 origin = hips.position;
                InputSystem.QueueStateEvent(TestKeyboard, new KeyboardState(keys[i]));
                InputSystem.Update();
                yield return new WaitForSecondsRealtime(0.1f);
                MeasuringStability = true;
                Vector3 direction = player.GetMovementDirection();
                Check(Mathf.Abs(direction.magnitude - 1f) < 0.01f, $"Direction {i}: normalized keyboard input (no diagonal boost).");
                Vector3 forward = Vector3.ProjectOnPlane(Read<Transform>(player, "m_Camera").forward, Vector3.up).normalized;
                float angle = i * 45f * Mathf.Deg2Rad;
                Vector3 expected = forward * Mathf.Cos(angle) + Vector3.Cross(Vector3.up, forward) * Mathf.Sin(angle);
                Check(Vector3.Dot(direction, expected) > 0.99f, $"Direction {i}: keyboard direction agrees with horizontal camera orientation.");
                yield return new WaitForSecondsRealtime(1.4f);
                Vector3 displacement = Vector3.ProjectOnPlane(hips.position - origin, Vector3.up);
                Check(Vector3.Dot(displacement, direction) > 0.5f, $"Direction {i}: keyboard moved physical hips {Vector3.Dot(displacement, direction):F2} m along command.");
                ActiveRagdollMuscle muscle = player.GetComponent<ActiveRagdollMuscle>();
                Debug.Log(Prefix + $"DIRECTION {i} speed={hips.linearVelocity.magnitude:F3} footError={muscle.FootTargetError:F3} steps={muscle.CompletedSteps}");
                MeasuringStability = false;
            }
            MeasuringStability = true;
            for (int i = 0; i < keys.Length; i++) {
                InputSystem.QueueStateEvent(TestKeyboard, new KeyboardState(keys[i]));
                yield return new WaitForSecondsRealtime(0.9f);
                ActiveRagdollMuscle muscle = player.GetComponent<ActiveRagdollMuscle>();
                Check(muscle.FootTargetError < 0.8f, $"Continuous turn {i}: feet remain within a leg length of their targets ({muscle.FootTargetError:F3} m).");
            }
            InputSystem.QueueStateEvent(TestKeyboard, new KeyboardState(InputKey.W, InputKey.LeftShift));
            yield return new WaitForSecondsRealtime(0.1f);
            Check(player.MoveSpeed > 1.8f && player.MoveSpeed < 2.7f, "Running speed transitions gradually rather than snapping.");
            yield return new WaitForSecondsRealtime(1.9f);
            Check(player.MoveSpeed > 2.5f && Vector3.ProjectOnPlane(hips.linearVelocity, Vector3.up).magnitude > 2.1f, "Shift smoothly increases physical running speed.");
            InputSystem.QueueStateEvent(TestKeyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(2f);
            Check(Vector3.ProjectOnPlane(hips.linearVelocity, Vector3.up).magnitude < 0.15f, "Release after running settles physical movement.");
            yield return null;
            InputSystem.RemoveDevice(TestKeyboard);
            TestKeyboard = null;
            MeasuringStability = false;
            Debug.Log(Prefix + $"STABILITY samples={StabilitySamples} meanFootError={FootErrorSum / Mathf.Max(1, StabilitySamples):F4} maxFootError={MaxFootError:F4} maxBodyTilt={MaxBodyTilt:F2}");
            Check(StabilitySamples > 100 && FootErrorSum / StabilitySamples < 0.2f, "Foot tracking stays precise across the complete walking/running sequence.");
            Check(MaxFootError < 0.6f && MaxBodyTilt < 25f, "Continuous tracking stays bounded and the torso stays upright during locomotion.");
            player.Respawn();
            yield return new WaitForSecondsRealtime(0.6f);
        }
        Invoke(player, "SetDebugMovement", Vector3.forward);
        Vector3 moveStart = hips.position;
        samples = 0;
        maxPositionError = maxRotationError = 0f;
        sampling = true;
        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-gaitPreview")) {
            for (int i = 0; i < 4; i++) {
                yield return new WaitForSecondsRealtime(0.13f);
                PreviewSuffix = "-step-" + i;
                captureRequested = true;
                while (captureRequested) yield return null;
            }
            PreviewSuffix = "";
            yield return new WaitForSecondsRealtime(0.48f);
        } else {
            yield return new WaitForSecondsRealtime(1f);
        }
        sampling = false;
        float travelled = Vector3.ProjectOnPlane(hips.position - moveStart, Vector3.up).magnitude;
        Check(travelled > 0.1f, $"Scripted movement actually displaced hips ({travelled:F3} m).");
        Check(samples >= 3 && maxPositionError < 0.01f,
            $"Camera follows moving hips after LateUpdate: {samples} samples, max error {maxPositionError:F5} m.");
        Check(maxRotationError < 0.1f, $"Orbit stays independent of ragdoll rotation (max error {maxRotationError:F4} deg).");

        Invoke(player, "SetDebugMovement", Vector3.zero);
        yield return new WaitForSecondsRealtime(0.6f);
        captureRequested = true;
        while (captureRequested) yield return null;
        // Pause a moving body, rather than allowing an already stopped body to pass trivially.
        Invoke(player, "SetDebugMovement", Vector3.forward);
        yield return new WaitForSecondsRealtime(0.5f);
        Check(hips.linearVelocity.magnitude > 0.05f, "Hips are moving immediately before pause.");

        // Non-default scale catches ResumeGame incorrectly hardcoding timeScale = 1.
        Time.timeScale = 0.65f;
        Invoke(game, "SetPaused", true);
        Check(Game.isGamePaused && Time.timeScale == 0f, "Pause flag and timeScale freeze together.");
        GameObject panel = Read<GameObject>(game, "PausePanel");
        Check(panel != null && panel.activeSelf, "Pause panel becomes visible.");
        Invoke(game, "SetPaused", true); // Repeated pause must not overwrite the saved scale.
        yield return null;
        float pausedTime = Time.time;
        Vector3 pausedPosition = hips.position;
        Quaternion pausedRotation = hips.rotation;
        yield return new WaitForSecondsRealtime(0.4f);
        Check(Mathf.Abs(Time.time - pausedTime) < 0.00001f, "Time.time does not advance during a real-time pause interval.");
        Check(Vector3.Distance(hips.position, pausedPosition) < 0.0001f &&
            Quaternion.Angle(hips.rotation, pausedRotation) < 0.01f, "Physical hips do not translate or rotate while paused.");

        // V state must never hide/lock the cursor while the game is paused.
        Game.isMouseGame = false;
        Invoke(game, "ApplyCursorState");
        Check(Cursor.lockState == CursorLockMode.None && Cursor.visible, "Pause takes precedence over the cursor toggle.");
        Game.isMouseGame = true;
        game.ResumeGame();
        Check(!Game.isGamePaused && Mathf.Abs(Time.timeScale - 0.65f) < 0.00001f,
            "Resume restores the pre-pause scale, including after repeated SetPaused(true).");
        Check(!panel.activeSelf && Cursor.visible && Cursor.lockState == CursorLockMode.None,
            "Resume hides the panel and preserves the V cursor preference.");
        yield return new WaitForSecondsRealtime(0.7f);
        Check(Time.time > pausedTime + 0.1f, "Simulation time advances again after Resume.");
        Check(Vector3.ProjectOnPlane(hips.position - pausedPosition, Vector3.up).magnitude > 0.05f,
            "Physical hips move again after Resume with the movement command still active.");

        // Exercise the production LateUpdate guard with V active. No input is injected.
        float yaw = Read<float>(camera, "YRotation");
        float pitch = Read<float>(camera, "XRotation");
        Field(camera, "WasLooking").SetValue(camera, true);
        Invoke(camera, "LateUpdate");
        Check(!Read<bool>(camera, "WasLooking") && Read<float>(camera, "YRotation") == yaw &&
            Read<float>(camera, "XRotation") == pitch, "V cursor state clears look eligibility and preserves yaw/pitch.");
        Debug.Log(Prefix + "Cursor test covers state/guard behavior, not a hardware V press or nonzero injected mouse delta.");
    }

    // Called by the late-running host, after the real Camera.LateUpdate (no manual follow update).
    internal static void SampleAfterCamera() {
        if (MeasuringStability && hips != null && MeasuredMuscle != null) {
            float error = MeasuredMuscle.FootTargetError;
            FootErrorSum += error;
            MaxFootError = Mathf.Max(MaxFootError, error);
            MaxBodyTilt = Mathf.Max(MaxBodyTilt, Vector3.Angle(MeasuredHead.position - hips.position, Vector3.up));
            StabilitySamples++;
        }
        if (!sampling || session == null || session.finishing) return;
        try {
            Vector3 expected = hips.transform.position + Vector3.up * Read<float>(camera, "FollowHeight");
            Quaternion rotation = Quaternion.Euler(Read<float>(camera, "XRotation"), Read<float>(camera, "YRotation"), 0);
            float positionError = Vector3.Distance(target.position, expected);
            float rotationError = Quaternion.Angle(target.rotation, rotation);
            if (float.IsNaN(positionError) || float.IsInfinity(positionError) || float.IsNaN(rotationError))
                throw new InvalidOperationException("Camera target contains a non-finite pose.");
            maxPositionError = Mathf.Max(maxPositionError, positionError);
            maxRotationError = Mathf.Max(maxRotationError, rotationError);
            samples++;
        } catch (Exception exception) { Finish(exception); }
    }

    // Run after Cinemachine has positioned Main Camera, without WaitForEndOfFrame
    // (which does not resume reliably in a batch-mode Editor).
    internal static void CaptureIfRequested() {
        if (!captureRequested || session == null || session.finishing) return;
        captureRequested = false;
        try { CapturePreview(); }
        catch (Exception exception) { Finish(exception); }
    }

    private static void CapturePreview() {
        UnityEngine.Camera output = UnityEngine.Camera.main;
        Check(output != null, "Main Camera is available for the rig preview.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Preview requires graphics; do not run with -nographics.");
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = output.targetTexture;
        float previousAspect = output.aspect;
        var render = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D pixels = null;
        try {
            Check(render.Create(), "Created the 1280x720 preview RenderTexture.");
            output.aspect = 1280f / 720f;
            if (GraphicsSettings.currentRenderPipeline != null) {
                var request = new RenderPipeline.StandardRequest { destination = render };
                if (!RenderPipeline.SupportsRenderRequest(output, request))
                    throw new InvalidOperationException("Active render pipeline does not support the camera preview request.");
                RenderPipeline.SubmitRenderRequest(output, request);
            } else {
                output.targetTexture = render;
                output.Render();
            }
            RenderTexture.active = render;
            pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            pixels.Apply();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "v1-preview" + PreviewSuffix + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] png = pixels.EncodeToPNG();
            Check(png != null && png.Length > 0, "Main Camera preview encoded as PNG.");
            File.WriteAllBytes(path, png);
            Debug.Log(Prefix + "PREVIEW " + path + " (1280x720; inspect visually for rig framing/pose).");
        } finally {
            output.targetTexture = previousTarget;
            output.aspect = previousAspect;
            RenderTexture.active = previousActive;
            if (pixels != null) UnityEngine.Object.Destroy(pixels);
            render.Release();
            UnityEngine.Object.Destroy(render);
        }
    }

    private static FieldInfo Field(object obj, string name) {
        return obj.GetType().GetField(name, Instance) ?? throw new MissingFieldException(obj.GetType().Name, name);
    }
    private static T Read<T>(object obj, string name) { return (T)Field(obj, name).GetValue(obj); }
    private static void Invoke(object obj, string name, params object[] args) {
        MethodInfo method = obj.GetType().GetMethod(name, Instance) ?? throw new MissingMethodException(obj.GetType().Name, name);
        method.Invoke(obj, args);
    }
    private static void Check(bool passed, string message) {
        if (!passed) throw new InvalidOperationException(message);
        session.assertions++;
        Debug.Log(Prefix + "PASS: " + message);
    }

    private static void Finish(Exception failure) {
        if (session == null || session.finishing) return;
        MeasuringStability = false;
        sampling = false;
        session.finishing = true;
        if (failure != null) {
            session.failed = true;
            Debug.Log(Prefix + "FAIL: " + failure);
        }
        try {
            if (TestKeyboard != null && TestKeyboard.added) InputSystem.RemoveDevice(TestKeyboard);
            TestKeyboard = null;
            if (ChangedInputSettings) {
                InputSystem.settings.backgroundBehavior = PreviousBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = PreviousEditorInput;
                ChangedInputSettings = false;
            }
            if (player != null) Invoke(player, "SetDebugMovement", new object[] { null });
            if (game != null) game.ResumeGame();
            if (EditorApplication.isPlaying) Time.timeScale = 1f;
        } catch (Exception exception) {
            session.failed = true;
            Debug.Log(Prefix + "FAIL cleanup: " + exception);
        }
        Save();
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        else Exit();
    }

    private static void Exit() {
        if (session == null) return;
        int code = session.failed ? 1 : 0;
        Debug.Log(Prefix + $"RESULT {(code == 0 ? "PASS" : "FAIL")} assertions={session.assertions} exitCode={code}");
        SessionState.EraseString(Key);
        session = null;
        EditorApplication.update -= Watchdog;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        Application.logMessageReceived -= OnLog;
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}

// A transient host lets observations run after the production camera's LateUpdate.
[DefaultExecutionOrder(10000)]
public sealed class V1CameraValidationRunner : MonoBehaviour {
    private void LateUpdate() {
        V1CameraValidation.SampleAfterCamera();
        V1CameraValidation.CaptureIfRequested();
    }
}
#endif
