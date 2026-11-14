#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PlayableAcceptance {
    private const string SessionKey = "OFD.PlayableAcceptance";
    private static bool Failed;
    private static bool Finishing;
    private static Player Player;
    private static Rigidbody Hips;
    private static ProceduralBalance Balance;
    private static ProceduralPosture Posture;
    private static Transform Head;
    private static Quaternion InitialRotation;
    private static float MaxTilt;
    private static float MaxTorsoTilt;
    private static int Samples;
    private static int SupportedSamples;
    private static bool Sampling;
    private static bool Monitoring;
    private static float MovementTilt;
    private static float ReportedTilt;
    private static float MovementTorsoTilt;
    private static float MovementFootError;
    private static Keyboard TestKeyboard;
    private static InputSettings.BackgroundBehavior PreviousBackground;
    private static InputSettings.EditorInputBehaviorInPlayMode PreviousEditorInput;

    static PlayableAcceptance() {
        EditorApplication.playModeStateChanged += OnPlayMode;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Our Final Dawn/Validate Playable Controller")]
    public static void Run() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            throw new InvalidOperationException("Exit Play Mode before running acceptance.");
        }
        for (int i = 0; i < SceneManager.sceneCount; i++) {
            if (SceneManager.GetSceneAt(i).isDirty) {
                throw new InvalidOperationException("Save scene edits before running acceptance.");
            }
        }
        Failed = Finishing = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Debug-Scene.unity");
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayMode(PlayModeStateChange state) {
        if (!SessionState.GetBool(SessionKey, false)) {
            return;
        }
        if (state == PlayModeStateChange.EnteredPlayMode) {
            new GameObject("Acceptance Runner").AddComponent<PlayableAcceptanceRunner>().StartCoroutine(Execute());
        } else if (state == PlayModeStateChange.EnteredEditMode) {
            Debug.Log($"[PlayableAcceptance] RESULT {(Failed ? "FAIL" : "PASS")}");
            SessionState.EraseBool(SessionKey);
            if (Application.isBatchMode) {
                EditorApplication.Exit(Failed ? 1 : 0);
            }
        }
    }

    private static void OnLog(string message, string stack, LogType type) {
        if (SessionState.GetBool(SessionKey, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) {
            Failed = true;
            Debug.Log("[PlayableAcceptance] Runtime failure: " + message + "\n" + stack);
            if (!Finishing) {
                EditorApplication.delayCall += Finish;
            }
        }
    }

    private static IEnumerator Execute() {
        IEnumerator scenario = Scenario();
        while (!Failed) {
            object next;
            try {
                if (!scenario.MoveNext()) {
                    break;
                }
                next = scenario.Current;
            } catch (Exception exception) {
                Failed = true;
                Debug.Log("[PlayableAcceptance] FAIL " + exception);
                break;
            }
            yield return next;
        }
        Finish();
    }

    private static IEnumerator Scenario() {
        yield return new WaitForSeconds(0.1f);
        Player = UnityEngine.Object.FindFirstObjectByType<Player>();
        Hips = Player.Hips;
        Balance = Player.GetComponent<ProceduralBalance>();
        Posture = Player.GetComponent<ProceduralPosture>();
        Head = Player.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        Debug.Log($"[PlayableAcceptance] RIG bodies={Player.GetComponentsInChildren<Rigidbody>().Length} legacy={Player.GetComponentsInChildren<ProceduralWalking>().Length + Player.GetComponentsInChildren<ProceduralController>().Length} animatorEnabled={Player.GetComponent<Animator>().enabled}");
        foreach (ConfigurableJoint joint in Player.GetComponentsInChildren<ConfigurableJoint>()) {
            Debug.Log($"[PlayableAcceptance] JOINT {joint.name} connected={joint.connectedBody.name} axis={joint.axis} secondary={joint.secondaryAxis} motion={joint.angularXMotion}/{joint.angularYMotion}/{joint.angularZMotion} limits={joint.lowAngularXLimit.limit}/{joint.highAngularXLimit.limit}/{joint.angularYLimit.limit}/{joint.angularZLimit.limit}");
        }
        InitialRotation = (Quaternion)typeof(ProceduralPosture).GetField("InitialRotation", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Posture);
        foreach (Rigidbody body in Player.GetComponentsInChildren<Rigidbody>()) {
            body.position += Vector3.left * 6f;
        }
        Physics.SyncTransforms();
        Player.GetComponent<ActiveRagdollMuscle>().ResetGait();
        Balance.ResetBalance();
        Player.SetDebugMovement(Vector3.zero);
        yield return new WaitForSeconds(2f);
        Check((Hips.constraints & (RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ)) == 0, "Pelvis tilt is physically controlled, not frozen.");
        Vector3 origin = Hips.position;
        MaxTilt = MaxTorsoTilt = 0f;
        Samples = SupportedSamples = 0;
        Sampling = true;
        bool quick = Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-quick");
        yield return new WaitForSeconds(quick ? 1f : 30f);
        Sampling = false;
        float speed = Vector3.ProjectOnPlane(Balance.Velocity, Vector3.up).magnitude;
        Debug.Log($"[PlayableAcceptance] IDLE{(quick ? 1 : 30)} tilt={MaxTilt:F2} torsoTilt={MaxTorsoTilt:F2} drift={Vector3.ProjectOnPlane(Hips.position - origin, Vector3.up).magnitude:F3} speed={speed:F3} supported={SupportedSamples}/{Samples} height={Hips.position.y - Balance.SupportPoint.y:F3} target={Player.GetComponent<ProceduralBalanceMotor>().Height:F3}");
        Check(MaxTilt < 15f && MaxTorsoTilt < 20f, "Upright posture remains stable for 30 seconds.");
        Check((quick || Samples > 1000) && SupportedSamples > Samples * 0.9f, "Idle is supported by actual foot contacts.");
        Check(Vector3.ProjectOnPlane(Hips.position - origin, Vector3.up).magnitude < 0.2f && speed < 0.15f, "Idle does not drift or accelerate spontaneously.");
        if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-phase1")) {
            yield break;
        }

        Monitoring = true;
        MovementTilt = 0f;
        ReportedTilt = 0f;
        MovementTorsoTilt = MovementFootError = 0f;
        origin = Hips.position;
        Player.SetDebugMovement(Vector3.forward);
        float deadline = Time.time + 12f;
        while (Hips.position.z - origin.z < 10f && Time.time < deadline && Player.CanControl) {
            yield return new WaitForFixedUpdate();
        }
        Check(Hips.position.z - origin.z >= 10f && Player.CanControl, $"Physical walking covers 10 m without falling (distance={Hips.position.z - origin.z:F2}, state={Player.State}).");
        Player.SetDebugMovement(Vector3.zero);
        yield return new WaitForSeconds(3f);
        Check(Balance.HasGroundSupport && Balance.Velocity.magnitude < 0.15f && Posture.TiltAngle < 15f, $"Stop returns to supported Idle (speed={Balance.Velocity.magnitude:F3}, tilt={Posture.TiltAngle:F2}, state={Player.State}).");

        origin = Hips.position;
        Player.SetDebugMovement(Vector3.back);
        yield return new WaitForSeconds(3f);
        Check(origin.z - Hips.position.z > 2f && Player.CanControl && Quaternion.Angle(Posture.DesiredRotation, Hips.rotation) < 20f, $"180-degree reversal remains controllable (distance={origin.z - Hips.position.z:F2}, rotationError={Quaternion.Angle(Posture.DesiredRotation, Hips.rotation):F2}, state={Player.State}).");
        Player.SetDebugMovement(Vector3.zero);
        yield return new WaitForSeconds(2f);

        PreviousBackground = InputSystem.settings.backgroundBehavior;
        PreviousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        TestKeyboard = InputSystem.AddDevice<Keyboard>();
        Player.SetDebugMovement(null);
        float walkingSpeed = Player.MoveSpeed;
        InputSystem.QueueStateEvent(TestKeyboard, new KeyboardState(UnityEngine.InputSystem.Key.W, UnityEngine.InputSystem.Key.LeftShift));
        yield return new WaitForSeconds(3f);
        Check(Player.MoveSpeed > walkingSpeed * 1.2f && Balance.Velocity.magnitude > walkingSpeed && Player.CanControl, $"Shift runs with physical movement (configured={Player.MoveSpeed:F2}, speed={Balance.Velocity.magnitude:F2}, state={Player.State}).");
        InputSystem.QueueStateEvent(TestKeyboard, new KeyboardState());
        yield return new WaitForSeconds(3f);
        Check(Balance.Velocity.magnitude < 0.15f && Player.State == Player.MovementState.Idle, $"Release returns to Idle (speed={Balance.Velocity.magnitude:F3}, state={Player.State}, reason={Player.RecoveryReason}).");
        Check(MovementTilt < 25f && MovementTorsoTilt < 25f && MovementFootError < 0.6f, $"Locomotion stays stable throughout, not only at endpoints (pelvis={MovementTilt:F1}, torso={MovementTorsoTilt:F1}, footError={MovementFootError:F3}).");
        Monitoring = false;

        Hips.AddForce(Vector3.right * Balance.TotalMass * 0.35f, ForceMode.Impulse);
        yield return new WaitForFixedUpdate();
        Check(Hips.linearVelocity.magnitude > 0.1f, "External impulse produces a physical response.");
        yield return new WaitForSeconds(3f);
        Check(Player.CanControl && Posture.TiltAngle < 20f && Balance.Velocity.magnitude < 0.2f, $"Light push recovers support (speed={Balance.Velocity.magnitude:F3}, tilt={Posture.TiltAngle:F2}, state={Player.State}).");

        foreach (Rigidbody body in Player.GetComponentsInChildren<Rigidbody>()) {
            body.AddForce(Vector3.up * 12f, ForceMode.VelocityChange);
        }
        yield return new WaitForSeconds(2.3f);
        Debug.Log($"[PlayableAcceptance] STRONG PUSH state={Player.State} tilt={Posture.TiltAngle:F2}");
        Check(Player.State == global::Player.MovementState.Falling, "Unsupported severe disturbance enters a physical fall instead of unlimited corrective torque.");
        InputSystem.QueueStateEvent(TestKeyboard, new KeyboardState(UnityEngine.InputSystem.Key.R));
        yield return new WaitForSeconds(0.1f);
        InputSystem.QueueStateEvent(TestKeyboard, new KeyboardState());
        yield return new WaitForSeconds(2f);
        Check(Player.State == Player.MovementState.Idle && Balance.HasGroundSupport && Posture.TiltAngle < 15f, $"R restores pose, contacts and control (state={Player.State}, tilt={Posture.TiltAngle:F2}).");

        foreach (Rigidbody body in Player.GetComponentsInChildren<Rigidbody>()) {
            body.AddForce(Vector3.up * 8f, ForceMode.VelocityChange);
        }
        yield return new WaitForSeconds(0.25f);
        Check(!Balance.HasGroundSupport && !Player.GetComponent<ProceduralBalanceMotor>().HasGround && Player.State == Player.MovementState.Airborne, "Loss of foot contact enters Airborne, even with a floor below the hips.");
        float vertical = Balance.Velocity.y;
        yield return new WaitForSeconds(0.2f);
        float gravityChange = vertical - Balance.Velocity.y;
        Check(!Balance.HasGroundSupport && gravityChange > 1.4f && gravityChange < 2.6f, $"Airborne COM follows gravity without suspension (deltaVy={gravityChange:F3}).");
        yield return new WaitForSeconds(3f);
        Check(Player.CanControl && Balance.HasGroundSupport && Posture.TiltAngle < 20f, $"Landing restores foot support (state={Player.State}, tilt={Posture.TiltAngle:F2}).");

        Player.Respawn();
        Player.SetDebugMovement(Vector3.zero);
        yield return new WaitForSeconds(1f);
        Player.SetDebugMovement(Vector3.forward);
        origin = Hips.position;
        yield return new WaitForSeconds(3.5f);
        Check(Player.CanControl && Hips.position.y > origin.y + 0.1f, $"Existing ramp is traversed with physical support (rise={Hips.position.y - origin.y:F3}, state={Player.State}).");
        Player.SetDebugMovement(Vector3.zero);
        yield return new WaitForSeconds(2f);
        Check(Player.CanControl && Balance.HasGroundSupport && Balance.Velocity.magnitude < 0.2f, "Ramp stop remains supported and controllable.");
    }

    internal static void Sample() {
        if (Monitoring && Hips != null) {
            MovementTilt = Mathf.Max(MovementTilt, Posture.TiltAngle);
            MovementTorsoTilt = Mathf.Max(MovementTorsoTilt, Vector3.Angle(Head.position - Hips.position, Vector3.up));
            MovementFootError = Mathf.Max(MovementFootError, Player.GetComponent<ActiveRagdollMuscle>().FootTargetError);
        }
        if (Monitoring && Hips != null && Posture.TiltAngle > ReportedTilt + 10f) {
            ReportedTilt = Posture.TiltAngle;
            ProceduralBalanceMotor motor = Player.GetComponent<ProceduralBalanceMotor>();
            ActiveRagdollMuscle muscles = Player.GetComponent<ActiveRagdollMuscle>();
            Debug.Log($"[PlayableAcceptance] TILT {MovementTilt:F1} state={Player.State} command={Player.GetMovementDirection()} velocity={Balance.Velocity} support={Balance.LeftSupported}/{Balance.RightSupported} error={Balance.SupportError} height={Hips.position.y - Balance.SupportPoint.y:F2}/{motor.Height:F2} feet={muscles.LeftState}/{muscles.RightState} tracking={muscles.FootTargetError:F2} yawError={Quaternion.Angle(Posture.DesiredRotation, Hips.rotation):F1}");
        }
        if (!Sampling || Hips == null) {
            return;
        }
        Quaternion delta = Hips.rotation * Quaternion.Inverse(InitialRotation);
        MaxTilt = Mathf.Max(MaxTilt, Vector3.Angle(delta * Vector3.up, Vector3.up));
        MaxTorsoTilt = Mathf.Max(MaxTorsoTilt, Vector3.Angle(Head.position - Hips.position, Vector3.up));
        Samples++;
        if (Balance.HasGroundSupport) {
            SupportedSamples++;
        }
    }

    private static void Check(bool value, string message) {
        if (!value) {
            throw new InvalidOperationException(message);
        }
        Debug.Log("[PlayableAcceptance] PASS " + message);
    }

    private static void Finish() {
        Finishing = true;
        Monitoring = false;
        Sampling = false;
        if (TestKeyboard != null) {
            InputSystem.RemoveDevice(TestKeyboard);
            TestKeyboard = null;
            InputSystem.settings.backgroundBehavior = PreviousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = PreviousEditorInput;
        }
        if (Player != null) {
            Player.SetDebugMovement(null);
        }
        EditorApplication.isPlaying = false;
    }
}

public class PlayableAcceptanceRunner : MonoBehaviour {
    private void FixedUpdate() {
        PlayableAcceptance.Sample();
    }
}
#endif
